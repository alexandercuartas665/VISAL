using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

/// <summary>
/// Motor generico de snapshots. Ver <see cref="IFacturacionSnapshotService"/>
/// para el contrato. Los builders por tipo se inyectan como
/// <c>IEnumerable&lt;ISnapshotBuilder&gt;</c>; el servicio elige por
/// <see cref="ISnapshotBuilder.TipoAplicable"/>.
/// </summary>
public sealed class FacturacionSnapshotService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IEnumerable<ISnapshotBuilder> builders,
    ISnapshotColumnaConfigService columnaConfig,
    Visal.Application.Facturacion.Rips.IRipsJsonBuilder ripsBuilder) : IFacturacionSnapshotService
{
    /// <summary>Tamano de lote para persistir filas del builder. Balance memoria vs. round-trips.</summary>
    private const int BatchSize = 500;

    /// <summary>Longitud minima del motivo de archivado. Fuerza al operador a justificar.</summary>
    private const int MotivoMinLen = 10;

    /// <summary>Serializacion estable — sin escapado unicode innecesario, permite jsonb query luego.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<Guid> GenerarAsync(GenerarSnapshotCmd cmd, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var builder = builders.FirstOrDefault(b => b.TipoAplicable == cmd.Tipo)
            ?? throw new InvalidOperationException($"No hay builder registrado para el tipo {cmd.Tipo}.");

        var ahora = DateTimeOffset.UtcNow;
        var nombre = string.IsNullOrWhiteSpace(cmd.Nombre)
            ? $"{cmd.Tipo} - {ahora:yyyy-MM-dd HH:mm:ss}"
            : cmd.Nombre.Trim();

        // El campo estructurado aseguradora_id se hidrata del JSON de filtros
        // para no tener que parsearlo en cada listado / filtro de la UI.
        var aseguradoraId = ExtraerAseguradoraIdDeFiltros(cmd.FiltrosJson);

        var snap = new FacturacionSnapshot
        {
            TenantId = tid,
            Nombre = nombre,
            Tipo = cmd.Tipo,
            AseguradoraId = aseguradoraId,
            FiltrosJson = string.IsNullOrWhiteSpace(cmd.FiltrosJson) ? "{}" : cmd.FiltrosJson,
            Estado = EstadoSnapshot.Ejecutando,
            FechaEjecucionInicio = ahora,
            CreatedBy = actor
        };
        db.FacturacionSnapshots.Add(snap);
        await db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        try
        {
            var numero = 0;
            var buffer = new List<FacturacionSnapshotFila>(BatchSize);
            await foreach (var fila in builder.ConstruirAsync(snap.FiltrosJson, ct).WithCancellation(ct))
            {
                numero++;
                buffer.Add(new FacturacionSnapshotFila
                {
                    TenantId = tid,
                    SnapshotId = snap.Id,
                    NumeroFila = numero,
                    DatosJson = JsonSerializer.Serialize(fila, JsonOpts)
                });
                if (buffer.Count >= BatchSize)
                {
                    db.FacturacionSnapshotFilas.AddRange(buffer);
                    await db.SaveChangesAsync(ct);
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
            {
                db.FacturacionSnapshotFilas.AddRange(buffer);
                await db.SaveChangesAsync(ct);
            }

            sw.Stop();
            // Politica "no basura": snapshots sin filas no se persisten. Evita
            // llenar el listado con combinaciones (EPS x sede) que no facturan.
            // El caller multi-EPS reintenta con otra EPS y esta simplemente no
            // deja rastro.
            if (numero == 0)
            {
                db.FacturacionSnapshots.Remove(snap);
                await db.SaveChangesAsync(ct);
                return Guid.Empty;
            }
            snap.Estado = EstadoSnapshot.Vigente;
            snap.FechaEjecucionFin = DateTimeOffset.UtcNow;
            snap.DuracionMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            snap.TotalFilas = numero;
            snap.UpdatedBy = actor;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            snap.Estado = EstadoSnapshot.Fallido;
            snap.FechaEjecucionFin = DateTimeOffset.UtcNow;
            snap.DuracionMs = (int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds);
            snap.ErrorMensaje = Truncate(ex.Message, 4000);
            snap.UpdatedBy = actor;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return snap.Id;
    }

    public async Task<IReadOnlyList<FacturacionSnapshotDto>> ListarAsync(
        EstadoSnapshot estado,
        TipoSnapshot? tipo = null,
        FiltrosListaSnapshotDto? filtros = null,
        CancellationToken ct = default)
    {
        var q = db.FacturacionSnapshots.AsNoTracking().Where(x => x.Estado == estado);
        if (tipo is TipoSnapshot t) { q = q.Where(x => x.Tipo == t); }
        if (filtros?.UsuarioId is Guid u) { q = q.Where(x => x.CreatedBy == u); }
        if (filtros?.AseguradoraId is Guid ase) { q = q.Where(x => x.AseguradoraId == ase); }
        if (filtros?.FechaInicio is DateOnly fi)
        {
            var d = new DateTimeOffset(fi.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(x => x.CreatedAt >= d);
        }
        if (filtros?.FechaFin is DateOnly ff)
        {
            var d = new DateTimeOffset(ff.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(x => x.CreatedAt <= d);
        }
        // Lookup en memoria: cargamos los snapshots y luego traducimos
        // AseguradoraId -> Nombre desde un dict prefetched. Evita GroupJoin
        // que EF no traduce limpio (patron heredado del selector).
        var rows = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var asegIds = rows.Where(r => r.AseguradoraId is not null).Select(r => r.AseguradoraId!.Value).Distinct().ToList();
        var aseguradoras = asegIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Aseguradoras.AsNoTracking()
                .Where(a => asegIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Nombre, ct);
        return rows.Select(r => Map(r, r.AseguradoraId is Guid ai && aseguradoras.TryGetValue(ai, out var n) ? n : null)).ToList();
    }

    public async Task<FacturacionSnapshotDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var snap = await db.FacturacionSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (snap is null) { return null; }
        var builder = builders.FirstOrDefault(b => b.TipoAplicable == snap.Tipo);
        var columnas = builder?.Columnas ?? Array.Empty<string>();
        string? asegNombre = null;
        if (snap.AseguradoraId is Guid aid)
        {
            asegNombre = await db.Aseguradoras.AsNoTracking()
                .Where(a => a.Id == aid).Select(a => a.Nombre).FirstOrDefaultAsync(ct);
        }
        return new FacturacionSnapshotDetalleDto(Map(snap, asegNombre), columnas, snap.FiltrosJson);
    }

    public async Task<PagedResult<IReadOnlyDictionary<string, object?>>> ListarFilasAsync(
        Guid snapshotId,
        int pagina,
        int tamanoPagina,
        string? ordenColumna = null,
        bool ordenDesc = false,
        string? buscar = null,
        CancellationToken ct = default)
    {
        if (pagina < 1) { pagina = 1; }
        if (tamanoPagina < 1) { tamanoPagina = 50; }
        if (tamanoPagina > 500) { tamanoPagina = 500; }

        var q = db.FacturacionSnapshotFilas.AsNoTracking().Where(x => x.SnapshotId == snapshotId);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim().ToLower();
            // Contains sobre el json crudo. Suficiente para tamanos esperados; los
            // indices gin-jsonb quedan para una fase posterior si hace falta.
            q = q.Where(x => x.DatosJson.ToLower().Contains(b));
        }

        var total = await q.CountAsync(ct);
        var filas = await q.OrderBy(x => x.NumeroFila)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(x => new { x.Id, x.DatosJson })
            .ToListAsync(ct);

        // Enriquecemos cada dict con __filaId para que la UI pueda ubicar la
        // fila al editar celdas. Clave con doble underscore para no chocar con
        // columnas del builder (que nunca empiezan asi).
        var items = filas.Select(f =>
        {
            var dict = new Dictionary<string, object?>(Deserializar(f.DatosJson), StringComparer.Ordinal);
            dict["__filaId"] = f.Id;
            return (IReadOnlyDictionary<string, object?>)dict;
        }).ToList();

        // TODO Fase 2/3: ordenar server-side por columna arbitraria (requiere
        // extraer campo del jsonb). Por ahora ordenamos client-side dentro de la
        // pagina actual — util para snapshots pequenos y no bloquea la UI.
        if (!string.IsNullOrWhiteSpace(ordenColumna))
        {
            IEnumerable<IReadOnlyDictionary<string, object?>> ordenados = items.OrderBy(x =>
                x.TryGetValue(ordenColumna, out var v) ? v?.ToString() ?? string.Empty : string.Empty);
            if (ordenDesc) { ordenados = ordenados.Reverse(); }
            items = ordenados.ToList();
        }

        return new PagedResult<IReadOnlyDictionary<string, object?>>(items, total, pagina, tamanoPagina);
    }

    public async Task ArchivarAsync(Guid id, string motivo, Guid actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < MotivoMinLen)
        {
            throw new InvalidOperationException(
                $"El motivo del archivado es obligatorio y debe tener al menos {MotivoMinLen} caracteres.");
        }

        var snap = await db.FacturacionSnapshots.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Snapshot no encontrado.");

        if (snap.Estado != EstadoSnapshot.Vigente)
        {
            throw new InvalidOperationException(
                $"Solo se pueden archivar snapshots en estado Vigente. Estado actual: {snap.Estado}.");
        }

        snap.Estado = EstadoSnapshot.Archivado;
        snap.MotivoArchivado = motivo.Trim();
        snap.FechaArchivado = DateTimeOffset.UtcNow;
        snap.ArchivadoPor = actor;
        snap.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ActualizarValorCeldaAsync(
        Guid snapshotId,
        Guid filaId,
        string columna,
        string? valorNuevo,
        Guid actor,
        string? motivo = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(columna)) { throw new ArgumentException("Columna requerida.", nameof(columna)); }
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var snap = await db.FacturacionSnapshots.FirstOrDefaultAsync(x => x.Id == snapshotId, ct)
            ?? throw new InvalidOperationException("Snapshot no encontrado.");
        if (snap.Estado != EstadoSnapshot.Vigente)
        {
            throw new InvalidOperationException(
                $"Solo se pueden editar celdas de snapshots Vigentes. Estado actual: {snap.Estado}.");
        }

        var fila = await db.FacturacionSnapshotFilas.FirstOrDefaultAsync(
            x => x.Id == filaId && x.SnapshotId == snapshotId, ct)
            ?? throw new InvalidOperationException("Fila no encontrada en el snapshot.");

        // Deserializamos, actualizamos y reserializamos con las mismas opciones
        // que usa el motor de escritura para mantener el formato coherente.
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fila.DatosJson) ?? new();
        string? valorAntesStr = null;
        if (dict.TryGetValue(columna, out var jeAntes))
        {
            valorAntesStr = jeAntes.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => jeAntes.GetString(),
                _ => jeAntes.GetRawText()
            };
        }

        // Normalizacion: string vacio se guarda como null para consistencia con
        // "columna sin valor".
        var valorLimpio = string.IsNullOrWhiteSpace(valorNuevo) ? null : valorNuevo.Trim();

        if (string.Equals(valorAntesStr, valorLimpio, StringComparison.Ordinal))
        {
            // Sin cambio real — no ensuciamos la auditoria con no-ops.
            return false;
        }

        // Regeneramos el dict con el nuevo valor y guardamos.
        var nuevo = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in dict)
        {
            nuevo[kv.Key] = kv.Key == columna ? valorLimpio : ExtraerValor(kv.Value);
        }
        // Si la columna no existia en la fila original, la agregamos ahora.
        if (!nuevo.ContainsKey(columna)) { nuevo[columna] = valorLimpio; }

        fila.DatosJson = JsonSerializer.Serialize(nuevo, JsonOpts);
        fila.UpdatedBy = actor;

        db.FacturacionSnapshotFilaCambios.Add(new FacturacionSnapshotFilaCambio
        {
            TenantId = tid,
            SnapshotId = snapshotId,
            FilaId = filaId,
            NumeroFila = fila.NumeroFila,
            ColumnaOriginal = columna,
            ValorAntes = Truncate(valorAntesStr, 4000),
            ValorDespues = Truncate(valorLimpio, 4000),
            ActorUserId = actor,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim(),
            CreatedBy = actor
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ActualizarColumnaEnLoteAsync(
        Guid snapshotId,
        string columna,
        string? valorNuevo,
        Guid actor,
        string? motivo = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(columna)) { throw new ArgumentException("Columna requerida.", nameof(columna)); }
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        var snap = await db.FacturacionSnapshots.FirstOrDefaultAsync(x => x.Id == snapshotId, ct)
            ?? throw new InvalidOperationException("Snapshot no encontrado.");
        if (snap.Estado != EstadoSnapshot.Vigente)
        {
            throw new InvalidOperationException(
                $"Solo se pueden editar celdas de snapshots Vigentes. Estado actual: {snap.Estado}.");
        }

        var valorLimpio = string.IsNullOrWhiteSpace(valorNuevo) ? null : valorNuevo.Trim();
        var motivoLimpio = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        // Recorremos todas las filas del snapshot; regeneramos el JSON con la
        // columna sustituida y creamos un cambio de auditoria por cada fila que
        // efectivamente cambio (no ensuciamos la trazabilidad con no-ops).
        var filas = await db.FacturacionSnapshotFilas
            .Where(f => f.SnapshotId == snapshotId)
            .ToListAsync(ct);

        var cambiadas = 0;
        foreach (var fila in filas)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fila.DatosJson) ?? new();
            string? valorAntesStr = null;
            if (dict.TryGetValue(columna, out var jeAntes))
            {
                valorAntesStr = jeAntes.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => jeAntes.GetString(),
                    _ => jeAntes.GetRawText()
                };
            }
            if (string.Equals(valorAntesStr, valorLimpio, StringComparison.Ordinal)) { continue; }

            var nuevo = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in dict)
            {
                nuevo[kv.Key] = kv.Key == columna ? valorLimpio : ExtraerValor(kv.Value);
            }
            if (!nuevo.ContainsKey(columna)) { nuevo[columna] = valorLimpio; }

            fila.DatosJson = JsonSerializer.Serialize(nuevo, JsonOpts);
            fila.UpdatedBy = actor;

            db.FacturacionSnapshotFilaCambios.Add(new FacturacionSnapshotFilaCambio
            {
                TenantId = tid,
                SnapshotId = snapshotId,
                FilaId = fila.Id,
                NumeroFila = fila.NumeroFila,
                ColumnaOriginal = columna,
                ValorAntes = Truncate(valorAntesStr, 4000),
                ValorDespues = Truncate(valorLimpio, 4000),
                ActorUserId = actor,
                Motivo = motivoLimpio,
                CreatedBy = actor
            });
            cambiadas++;
        }

        if (cambiadas > 0) { await db.SaveChangesAsync(ct); }
        return cambiadas;
    }

    public async Task<IReadOnlyList<CambioCeldaDto>> ListarCambiosAsync(Guid snapshotId, CancellationToken ct = default)
    {
        return await db.FacturacionSnapshotFilaCambios.AsNoTracking()
            .Where(x => x.SnapshotId == snapshotId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CambioCeldaDto(
                x.Id, x.FilaId, x.NumeroFila, x.ColumnaOriginal,
                x.ValorAntes, x.ValorDespues, x.ActorUserId, x.CreatedAt, x.Motivo))
            .ToListAsync(ct);
    }

    private static FacturacionSnapshotDto Map(FacturacionSnapshot x, string? aseguradoraNombre = null) => new(
        x.Id, x.Nombre, x.Tipo, x.Estado,
        x.FechaEjecucionInicio, x.FechaEjecucionFin, x.DuracionMs, x.TotalFilas,
        x.CreatedBy, x.ArchivadoPor, x.MotivoArchivado, x.FechaArchivado, x.ErrorMensaje,
        x.AseguradoraId, aseguradoraNombre);

    /// <summary>
    /// Extrae <c>aseguradoraId</c> del JSON de filtros para poder guardarlo en
    /// una columna estructurada. Devuelve null si el filtro no lo trae o no es
    /// parseable — el snapshot se persiste con AseguradoraId NULL.
    /// </summary>
    private static Guid? ExtraerAseguradoraIdDeFiltros(string? filtrosJson)
    {
        if (string.IsNullOrWhiteSpace(filtrosJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(filtrosJson);
            if (doc.RootElement.TryGetProperty("aseguradoraId", out var ae)
                && ae.ValueKind == JsonValueKind.String
                && Guid.TryParse(ae.GetString(), out var g))
            {
                return g;
            }
        }
        catch { /* filtros mal formados -> null */ }
        return null;
    }

    private static IReadOnlyDictionary<string, object?> Deserializar(string json)
    {
        // Al deserializar de jsonb los valores llegan como JsonElement. Los
        // aplanamos a tipos .NET simples (string/int/decimal/bool/null) para
        // que el consumidor de la fila no dependa de System.Text.Json.
        var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = ExtraerValor(prop.Value);
        }
        return dict;
    }

    private static object? ExtraerValor(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) || s!.Length <= max ? s : s[..max];

    // ---- Export ----

    public async Task<ArchivoExportado?> ExportarExcelAsync(Guid id, CancellationToken ct = default)
    {
        var ctx = await CargarParaExportAsync(id, ct);
        if (ctx is null) { return null; }

        using var wb = new XLWorkbook();
        // Nombre de hoja: limpiar chars invalidos + tope 31 chars (limite Excel).
        var hoja = wb.Worksheets.Add(SanitizarNombreHoja(ctx.Snapshot.Nombre));

        // Headers: usa alias del tenant si esta configurado, sino el header canonico
        // del builder. La clave interna (ColumnaOriginal) es la que busca el dict de la fila.
        for (var c = 0; c < ctx.Columnas.Count; c++)
        {
            var cell = hoja.Cell(1, c + 1);
            cell.Value = ctx.Columnas[c].HeaderExport;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            cell.Style.Alignment.WrapText = false;
        }

        // Filas. Iteramos por lotes para no cargar todo en memoria de golpe.
        var row = 2;
        await foreach (var fila in IterarFilasAsync(id, ct))
        {
            for (var c = 0; c < ctx.Columnas.Count; c++)
            {
                var col = ctx.Columnas[c].ColumnaOriginal;
                if (!fila.TryGetValue(col, out var val) || val is null) { continue; }
                var cell = hoja.Cell(row, c + 1);
                switch (val)
                {
                    case long lv: cell.Value = lv; break;
                    case int iv: cell.Value = iv; break;
                    case decimal dv: cell.Value = dv; break;
                    case double db: cell.Value = db; break;
                    case bool bv: cell.Value = bv; break;
                    default: cell.Value = val.ToString(); break;
                }
            }
            row++;
        }

        // Ajuste automatico de ancho — comodo para el usuario que abre el .xlsx.
        hoja.Columns().AdjustToContents(1, Math.Max(1, row - 1));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new ArchivoExportado(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SanitizarNombreArchivo(ctx.Snapshot.Nombre) + ".xlsx");
    }

    public async Task<RipsExportResult> ExportarJsonRipsAsync(Guid id, bool ignorarValidacion = false, CancellationToken ct = default)
    {
        var detalle = await ObtenerAsync(id, ct);
        if (detalle is null) { return new RipsExportResult(null, Array.Empty<string>()); }

        // NIT del obligado: lee Tenant.TaxId del tenant activo. Sin global query filter
        // en Tenants (es entidad global), tid ya vino resuelto por ITenantContext.
        if (tenant.TenantId is not Guid tid) { return new RipsExportResult(null, new[] { "Sin tenant activo." }); }
        var taxId = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tid)
            .Select(t => t.TaxId)
            .FirstOrDefaultAsync(ct);

        // Una sola pagina grande — R1/R2 solo necesitan usuarios+transaccion.
        // Olas siguientes iteraran por bloques cuando emitan servicios[].
        var page = await ListarFilasAsync(id, pagina: 1, tamanoPagina: int.MaxValue, ct: ct);

        // R7: precargar el catalogo de Medicamentos del tenant una sola vez y armar
        // el lookup por 2 llaves (CUM compuesto + expediente base) para que el
        // builder pueda enriquecer cada medicamento con datos oficiales sin tocar el DbContext.
        var meds = await db.Medicamentos.AsNoTracking().ToListAsync(ct);
        var medDict = new Dictionary<string, Visal.Application.Facturacion.Rips.MedicamentoCatalogoInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in meds)
        {
            var cumCompuesto = (!string.IsNullOrWhiteSpace(m.ExpedienteCum) && !string.IsNullOrWhiteSpace(m.ConsecutivoCum))
                ? $"{m.ExpedienteCum}-{m.ConsecutivoCum}"
                : null;
            var info = new Visal.Application.Facturacion.Rips.MedicamentoCatalogoInfo(
                CumInvima: cumCompuesto,
                Nombre: m.Producto ?? m.DescripcionComercial,
                Concentracion: m.Concentracion,
                UnidadMedida: m.UnidadMedida,
                FormaFarmaceutica: m.FormaFarmaceutica,
                // Heuristica POS/PBS: si Modalidad contiene "NO" -> No PBS (02);
                // en cualquier otro caso (incluido null) -> asumimos POS/PBS (01).
                EsPos: !(m.Modalidad?.Contains("NO", StringComparison.OrdinalIgnoreCase) ?? false));
            if (cumCompuesto is not null) { medDict[cumCompuesto] = info; }
            if (!string.IsNullOrWhiteSpace(m.Expediente)) { medDict.TryAdd(m.Expediente, info); }
        }
        // R8: precargar los codigos CIE-10 habilitados para validar los diagnosticos
        // que trae el snapshot. Case-insensitive. Set vacio si el tenant no ha cargado
        // el catalogo (el builder no valida en ese caso).
        var cieCodigos = await db.Diagnosticos.AsNoTracking()
            .Where(d => d.Habilitado)
            .Select(d => d.Codigo)
            .ToListAsync(ct);
        var cieSet = new HashSet<string>(cieCodigos, StringComparer.OrdinalIgnoreCase);

        var catalogos = new Visal.Application.Facturacion.Rips.RipsCatalogos(
            MedicamentosPorCodigo: medDict,
            CodigosCie10Validos: cieSet);

        var payload = ripsBuilder.Build(detalle, page.Items, taxId ?? string.Empty, catalogos);

        // R8: usar el overload ValidateWith que verifica los CIE-10 contra el catalogo.
        // Downcasting seguro: solo la implementacion concreta expone ValidateWith.
        var errores = ripsBuilder is Visal.Application.Facturacion.Rips.RipsJsonBuilder rb
            ? rb.ValidateWith(payload, catalogos)
            : ripsBuilder.Validate(payload);
        // Modo estricto (default): si hay errores, no se genera archivo. Modo tolerante
        // (ignorarValidacion=true, para depuracion): se genera igualmente y se devuelven
        // ambos — el consumidor decide que hacer con los warnings.
        if (errores.Count > 0 && !ignorarValidacion)
        {
            return new RipsExportResult(null, errores);
        }

        // UTF-8 PURO SIN BOM (Res. 2275 seccion 1.1). SerializeToUtf8Bytes ya
        // no incluye preamble; NamingPolicy=CamelCase emite las llaves como
        // "numFactura", "codSexo", etc. IgnoreNull omite opcionales vacios.
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, opts);

        // R5: total neto expuesto en el nombre de archivo para trazabilidad basica
        // (operador puede comparar contra el <PayableAmount> del FEV sin abrir el JSON).
        // El nombre queda: "{snapshot}-neto-{total}.rips.json".
        var (_, _, neto) = Visal.Application.Facturacion.Rips.RipsJsonBuilder.TotalNeto(payload);
        var netoStr = neto.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var archivo = new ArchivoExportado(
            bytes,
            "application/json; charset=utf-8",
            $"{SanitizarNombreArchivo(detalle.Metadata.Nombre)}-neto-{netoStr}.rips.json");
        // En modo tolerante devolvemos el archivo Y los errores (si los hubo) para que
        // el operador tenga trazabilidad de la validacion aunque haya forzado la descarga.
        return new RipsExportResult(archivo, errores);
    }

    public async Task<ArchivoExportado?> ExportarCsvAsync(Guid id, CancellationToken ct = default)
    {
        var ctx = await CargarParaExportAsync(id, ct);
        if (ctx is null) { return null; }

        var sb = new StringBuilder();
        // Cabecera: alias del tenant si esta configurado, sino header canonico del builder.
        sb.AppendLine(string.Join(';', ctx.Columnas.Select(c => EscaparCsv(c.HeaderExport))));

        await foreach (var fila in IterarFilasAsync(id, ct))
        {
            var partes = new string[ctx.Columnas.Count];
            for (var c = 0; c < ctx.Columnas.Count; c++)
            {
                var col = ctx.Columnas[c].ColumnaOriginal;
                var val = fila.TryGetValue(col, out var v) && v is not null ? v.ToString() ?? "" : "";
                partes[c] = EscaparCsv(val);
            }
            sb.AppendLine(string.Join(';', partes));
        }

        // UTF-8 con BOM para que Excel Colombia lo abra bien de un doble-click.
        // Encoding.UTF8.GetBytes NO emite el preamble; hay que anteponerlo a mano.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = utf8.GetPreamble();
        var cuerpo = utf8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + cuerpo.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(cuerpo, 0, bytes, preamble.Length, cuerpo.Length);
        return new ArchivoExportado(
            bytes,
            "text/csv; charset=utf-8",
            SanitizarNombreArchivo(ctx.Snapshot.Nombre) + ".csv");
    }

    /// <summary>Contexto compartido de los dos exportadores. Cada entrada de <see cref="Columnas"/>
    /// lleva la clave interna (para leer del dict de la fila) y el header que se escribe
    /// en el archivo (respetando alias del tenant si existe).</summary>
    private sealed record CtxExport(FacturacionSnapshot Snapshot, IReadOnlyList<ColumnaExportInfo> Columnas);

    private async Task<CtxExport?> CargarParaExportAsync(Guid id, CancellationToken ct)
    {
        var snap = await db.FacturacionSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (snap is null) { return null; }

        // El builder puede haber dejado de estar registrado (upgrade sin rebuild):
        // en ese caso caemos a inferir columnas desde la primera fila. Cubre
        // snapshots historicos con nuevas versiones del builder.
        var builder = builders.FirstOrDefault(b => b.TipoAplicable == snap.Tipo);
        IReadOnlyList<string> canonicas = builder?.Columnas ?? Array.Empty<string>();
        if (canonicas.Count == 0)
        {
            var primera = await db.FacturacionSnapshotFilas.AsNoTracking()
                .Where(x => x.SnapshotId == id)
                .OrderBy(x => x.NumeroFila)
                .Select(x => x.DatosJson).FirstOrDefaultAsync(ct);
            if (primera is not null)
            {
                using var doc = JsonDocument.Parse(primera);
                canonicas = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            }
        }

        // Aplica preferencia del tenant (orden, visibilidad, alias). Si el tenant
        // no configuro nada, obtenemos las mismas columnas del builder tal cual.
        var columnas = await columnaConfig.ObtenerParaExportAsync(snap.Tipo, canonicas, ct);
        return new CtxExport(snap, columnas);
    }

    /// <summary>Streamea las filas del snapshot en orden natural para no cargarlas todas en memoria.</summary>
    private async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> IterarFilasAsync(
        Guid id,
        [EnumeratorCancellation] CancellationToken ct)
    {
        const int Batch = 500;
        var offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var lote = await db.FacturacionSnapshotFilas.AsNoTracking()
                .Where(x => x.SnapshotId == id)
                .OrderBy(x => x.NumeroFila)
                .Skip(offset).Take(Batch)
                .Select(x => x.DatosJson).ToListAsync(ct);
            if (lote.Count == 0) { yield break; }
            foreach (var json in lote) { yield return Deserializar(json); }
            offset += lote.Count;
            if (lote.Count < Batch) { yield break; }
        }
    }

    /// <summary>Escape RFC 4180-ish para el separador ; y encoding CO.</summary>
    private static string EscaparCsv(string s)
    {
        if (s is null) { return string.Empty; }
        var necesita = s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!necesita) { return s; }
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizarNombreArchivo(string s)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new StringBuilder(s.Length);
        foreach (var ch in s) { limpio.Append(invalidos.Contains(ch) ? '_' : ch); }
        var r = limpio.ToString().Trim();
        return string.IsNullOrWhiteSpace(r) ? "snapshot" : r;
    }

    private static string SanitizarNombreHoja(string s)
    {
        // Excel: 31 chars max, sin  : \ / ? * [ ]
        var proh = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) { sb.Append(proh.Contains(ch) ? '_' : ch); }
        var r = sb.ToString().Trim();
        if (string.IsNullOrEmpty(r)) { r = "snapshot"; }
        return r.Length > 31 ? r[..31] : r;
    }
}

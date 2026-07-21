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
    ISnapshotColumnaConfigService columnaConfig) : IFacturacionSnapshotService
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

        var snap = new FacturacionSnapshot
        {
            TenantId = tid,
            Nombre = nombre,
            Tipo = cmd.Tipo,
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
        return await q.OrderByDescending(x => x.CreatedAt).Select(x => Map(x)).ToListAsync(ct);
    }

    public async Task<FacturacionSnapshotDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var snap = await db.FacturacionSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (snap is null) { return null; }
        var builder = builders.FirstOrDefault(b => b.TipoAplicable == snap.Tipo);
        var columnas = builder?.Columnas ?? Array.Empty<string>();
        return new FacturacionSnapshotDetalleDto(Map(snap), columnas, snap.FiltrosJson);
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
            .Select(x => x.DatosJson)
            .ToListAsync(ct);

        var items = filas.Select(Deserializar).ToList();

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

    private static FacturacionSnapshotDto Map(FacturacionSnapshot x) => new(
        x.Id, x.Nombre, x.Tipo, x.Estado,
        x.FechaEjecucionInicio, x.FechaEjecucionFin, x.DuracionMs, x.TotalFilas,
        x.CreatedBy, x.ArchivadoPor, x.MotivoArchivado, x.FechaArchivado, x.ErrorMensaje);

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

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

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

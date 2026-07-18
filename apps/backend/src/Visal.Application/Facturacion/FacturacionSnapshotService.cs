using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    IEnumerable<ISnapshotBuilder> builders) : IFacturacionSnapshotService
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
}

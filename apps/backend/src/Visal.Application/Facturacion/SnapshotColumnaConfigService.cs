using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

public sealed class SnapshotColumnaConfigService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IEnumerable<ISnapshotBuilder> builders) : ISnapshotColumnaConfigService
{
    public async Task<IReadOnlyList<ColumnaConfigItemDto>> ListarAsync(TipoSnapshot tipo, CancellationToken ct = default)
    {
        var canonicas = ColumnasCanonicas(tipo);
        if (canonicas.Count == 0) { return Array.Empty<ColumnaConfigItemDto>(); }

        // Descripciones default publicadas por el builder (p.ej. copiadas de la
        // fila 2 del template EPS). Se usan como sugerencia cuando el tenant no
        // ha guardado una descripcion propia — el override siempre gana.
        var descrDefault = DescripcionesDefault(tipo);

        var overrides = await db.FacturacionSnapshotColumnaConfigs.AsNoTracking()
            .Where(c => c.Tipo == tipo)
            .ToDictionaryAsync(c => c.ColumnaOriginal, ct);

        var conOverride = new List<ColumnaConfigItemDto>();
        var sinOverride = new List<ColumnaConfigItemDto>();
        var siguienteOrdenBase = overrides.Count;
        var i = 0;
        foreach (var col in canonicas)
        {
            if (overrides.TryGetValue(col, out var ov))
            {
                // Si el tenant guardo descripcion, se respeta; si la dejo vacia,
                // caemos al default del builder para que la UI muestre algo util.
                var descr = string.IsNullOrWhiteSpace(ov.Descripcion)
                    ? (descrDefault.TryGetValue(col, out var d) ? d : null)
                    : ov.Descripcion;
                conOverride.Add(new ColumnaConfigItemDto(col, ov.Orden, ov.Visible, ov.Alias, descr, ov.RutaOrigen));
            }
            else
            {
                var descr = descrDefault.TryGetValue(col, out var d) ? d : null;
                sinOverride.Add(new ColumnaConfigItemDto(col, siguienteOrdenBase + i, Visible: true, Alias: null, Descripcion: descr, RutaOrigen: null));
                i++;
            }
        }
        return conOverride
            .OrderBy(x => x.Orden)
            .Concat(sinOverride)
            .ToList();
    }

    public async Task GuardarAsync(TipoSnapshot tipo, IReadOnlyList<ColumnaConfigItemDto> items, Guid actorUserId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) { return; }

        var canonicas = ColumnasCanonicas(tipo).ToHashSet();
        var entrantes = items
            .Where(x => canonicas.Contains(x.ColumnaOriginal))
            .GroupBy(x => x.ColumnaOriginal)
            .Select(g => g.First())
            .ToList();

        var existentes = await db.FacturacionSnapshotColumnaConfigs
            .Where(c => c.Tipo == tipo)
            .ToListAsync(ct);
        var mapExistentes = existentes.ToDictionary(c => c.ColumnaOriginal);

        // Upsert por columna.
        foreach (var item in entrantes)
        {
            if (mapExistentes.TryGetValue(item.ColumnaOriginal, out var e))
            {
                e.Orden = item.Orden;
                e.Visible = item.Visible;
                e.Alias = string.IsNullOrWhiteSpace(item.Alias) ? null : item.Alias.Trim();
                e.Descripcion = string.IsNullOrWhiteSpace(item.Descripcion) ? null : item.Descripcion.Trim();
                e.RutaOrigen = string.IsNullOrWhiteSpace(item.RutaOrigen) ? null : item.RutaOrigen.Trim();
                mapExistentes.Remove(item.ColumnaOriginal);
            }
            else
            {
                db.FacturacionSnapshotColumnaConfigs.Add(new FacturacionSnapshotColumnaConfig
                {
                    TenantId = tenantId,
                    Tipo = tipo,
                    ColumnaOriginal = item.ColumnaOriginal,
                    Orden = item.Orden,
                    Visible = item.Visible,
                    Alias = string.IsNullOrWhiteSpace(item.Alias) ? null : item.Alias.Trim(),
                    Descripcion = string.IsNullOrWhiteSpace(item.Descripcion) ? null : item.Descripcion.Trim(),
                    RutaOrigen = string.IsNullOrWhiteSpace(item.RutaOrigen) ? null : item.RutaOrigen.Trim()
                });
            }
        }

        // Los que quedaron en mapExistentes no vinieron en la lista entrante -> borrar
        // (la UI reemplaza la lista completa; si no aparecen, se sacaron).
        foreach (var huerfano in mapExistentes.Values)
        {
            db.FacturacionSnapshotColumnaConfigs.Remove(huerfano);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(TipoSnapshot tipo, Guid actorUserId, CancellationToken ct = default)
    {
        var existentes = await db.FacturacionSnapshotColumnaConfigs
            .Where(c => c.Tipo == tipo)
            .ToListAsync(ct);
        if (existentes.Count == 0) { return; }
        foreach (var e in existentes) { db.FacturacionSnapshotColumnaConfigs.Remove(e); }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ColumnaExportInfo>> ObtenerParaExportAsync(TipoSnapshot tipo, IReadOnlyList<string> columnasCanonicas, CancellationToken ct = default)
    {
        if (columnasCanonicas.Count == 0) { return Array.Empty<ColumnaExportInfo>(); }

        var overrides = await db.FacturacionSnapshotColumnaConfigs.AsNoTracking()
            .Where(c => c.Tipo == tipo)
            .ToDictionaryAsync(c => c.ColumnaOriginal, ct);

        // Mismo criterio que ListarAsync: overrides ordenan, no-overrides al final.
        var conOverride = new List<(string Col, int Orden, string Header)>();
        var sinOverride = new List<string>();
        foreach (var col in columnasCanonicas)
        {
            if (overrides.TryGetValue(col, out var ov))
            {
                if (!ov.Visible) { continue; } // oculta -> fuera del export
                var header = string.IsNullOrWhiteSpace(ov.Alias) ? col : ov.Alias!;
                conOverride.Add((col, ov.Orden, header));
            }
            else
            {
                sinOverride.Add(col);
            }
        }

        var salida = new List<ColumnaExportInfo>(columnasCanonicas.Count);
        salida.AddRange(conOverride.OrderBy(x => x.Orden).Select(x => new ColumnaExportInfo(x.Col, x.Header)));
        salida.AddRange(sinOverride.Select(x => new ColumnaExportInfo(x, x)));
        return salida;
    }

    private IReadOnlyList<string> ColumnasCanonicas(TipoSnapshot tipo)
    {
        var builder = builders.FirstOrDefault(b => b.TipoAplicable == tipo);
        return builder?.Columnas ?? Array.Empty<string>();
    }

    private IReadOnlyDictionary<string, string?> DescripcionesDefault(TipoSnapshot tipo)
    {
        var builder = builders.FirstOrDefault(b => b.TipoAplicable == tipo);
        return builder?.Descripciones ?? System.Collections.Immutable.ImmutableDictionary<string, string?>.Empty;
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class AtencionColumnaConfigService(IApplicationDbContext db, ITenantContext tenant)
    : IAtencionColumnaConfigService
{
    public async Task<IReadOnlyList<AtencionColumnaConfigDto>> ListarAsync(CancellationToken ct = default)
    {
        return await db.AtencionColumnaConfigs.AsNoTracking()
            .OrderBy(x => x.Orden ?? int.MaxValue).ThenBy(x => x.ColumnaKey)
            .Select(x => new AtencionColumnaConfigDto(x.ColumnaKey, x.Visible, x.Alias, x.Orden))
            .ToListAsync(ct);
    }

    public async Task GuardarLoteAsync(IReadOnlyList<GuardarAtencionColumnaRequest> items, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (items is null || items.Count == 0) { return; }

        // Merge por (tenant, columna_key): si la fila con default puro (Visible=true,
        // Alias=null, Orden=null) llega, borramos la fila para no ensuciar la tabla —
        // la UI cae al comportamiento default sola.
        var existentes = await db.AtencionColumnaConfigs.ToDictionaryAsync(x => x.ColumnaKey, ct);
        foreach (var req in items)
        {
            var key = (req.ColumnaKey ?? "").Trim();
            if (string.IsNullOrEmpty(key)) { continue; }
            var alias = string.IsNullOrWhiteSpace(req.Alias) ? null : req.Alias!.Trim();
            var esDefault = req.Visible && alias is null && req.Orden is null;

            existentes.TryGetValue(key, out var row);
            if (esDefault)
            {
                if (row is not null) { db.AtencionColumnaConfigs.Remove(row); }
                continue;
            }
            if (row is null)
            {
                db.AtencionColumnaConfigs.Add(new AtencionColumnaConfig
                {
                    TenantId = tid,
                    ColumnaKey = key,
                    Visible = req.Visible,
                    Alias = alias,
                    Orden = req.Orden
                });
            }
            else
            {
                row.Visible = req.Visible;
                row.Alias = alias;
                row.Orden = req.Orden;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

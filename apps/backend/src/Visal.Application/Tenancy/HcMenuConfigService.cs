using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class HcMenuConfigService(IApplicationDbContext db, ITenantContext tenant) : IHcMenuConfigService
{
    private static string NormTipo(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private static string NormPestana(string? s) => (s ?? "").Trim();

    public async Task<IReadOnlyList<HcMenuConfigDto>> ListAsync(CancellationToken ct = default)
    {
        return await db.HcMenuConfigs.AsNoTracking()
            .OrderBy(x => x.TipoServicio)
            .ThenBy(x => x.PestanaKey)
            .Select(x => new HcMenuConfigDto(x.TipoServicio, x.PestanaKey, x.Visible))
            .ToListAsync(ct);
    }

    public async Task<HashSet<string>> ObtenerPestanasOcultasAsync(string? tipoServicio, CancellationToken ct = default)
    {
        var t = NormTipo(tipoServicio);
        if (string.IsNullOrEmpty(t)) { return new(); }
        var lista = await db.HcMenuConfigs.AsNoTracking()
            .Where(x => x.TipoServicio == t && !x.Visible)
            .Select(x => x.PestanaKey)
            .ToListAsync(ct);
        return new HashSet<string>(lista, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(string tipoServicio, string pestanaKey, bool visible, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var t = NormTipo(tipoServicio);
        var p = NormPestana(pestanaKey);
        if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(p)) { return; }
        var row = await db.HcMenuConfigs.FirstOrDefaultAsync(
            x => x.TipoServicio == t && x.PestanaKey == p, ct);
        if (visible)
        {
            // Visible=true es el default; el override es solo para ocultar.
            if (row is not null) { db.HcMenuConfigs.Remove(row); await db.SaveChangesAsync(ct); }
            return;
        }
        // Persistir override oculto.
        if (row is null)
        {
            db.HcMenuConfigs.Add(new HcMenuConfig
            {
                TenantId = tid,
                TipoServicio = t,
                PestanaKey = p,
                Visible = false
            });
        }
        else { row.Visible = false; }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HcPestanaAliasDto>> ListAliasesAsync(CancellationToken ct = default)
    {
        return await db.HcPestanaAliases.AsNoTracking()
            .OrderBy(x => x.PestanaKey)
            .Select(x => new HcPestanaAliasDto(x.PestanaKey, x.Alias))
            .ToListAsync(ct);
    }

    public async Task<Dictionary<string, string>> ObtenerAliasesAsync(CancellationToken ct = default)
    {
        var rows = await db.HcPestanaAliases.AsNoTracking()
            .Where(x => x.Alias != null && x.Alias != "")
            .Select(x => new { x.PestanaKey, x.Alias })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.PestanaKey, r => r.Alias!, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAliasAsync(string pestanaKey, string? alias, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        var p = NormPestana(pestanaKey);
        if (string.IsNullOrEmpty(p)) { return; }
        var trimmed = alias?.Trim();
        var row = await db.HcPestanaAliases.FirstOrDefaultAsync(x => x.PestanaKey == p, ct);
        if (string.IsNullOrEmpty(trimmed))
        {
            // Vacio -> vuelve al nombre por defecto: borrar la fila.
            if (row is not null) { db.HcPestanaAliases.Remove(row); await db.SaveChangesAsync(ct); }
            return;
        }
        if (row is null)
        {
            db.HcPestanaAliases.Add(new HcPestanaAlias { TenantId = tid, PestanaKey = p, Alias = trimmed });
        }
        else { row.Alias = trimmed; }
        await db.SaveChangesAsync(ct);
    }
}

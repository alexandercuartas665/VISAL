using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class TenantBrandingPublicoService(IApplicationDbContextFactory dbFactory) : ITenantBrandingPublicoService
{
    public async Task<TenantBrandingPublicoDto?> GetDefaultAsync(CancellationToken ct = default)
    {
        using var db = dbFactory.Create();
        // IgnoreQueryFilters: aqui no hay tenant context. Buscamos tenants Active/Trial.
        var tenants = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TenantBrandingPublicoDto(t.Name, t.LogoUrl))
            .Take(2)
            .ToListAsync(ct);

        // Solo devolvemos branding cuando hay UN unico tenant operativo - asi
        // evitamos elegir arbitrariamente el branding de uno cuando hay varios.
        return tenants.Count == 1 ? tenants[0] : null;
    }
}

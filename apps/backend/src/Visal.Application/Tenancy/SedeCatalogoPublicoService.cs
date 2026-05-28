using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class SedeCatalogoPublicoService(IApplicationDbContext db) : ISedeCatalogoPublicoService
{
    public async Task<IReadOnlyList<SedePublicaDto>> ListAsync(CancellationToken ct = default)
    {
        // Sedes activas de tenants Active/Trial. IgnoreQueryFilters porque aqui no hay tenant context.
        var tenantsValidos = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var sedes = await db.Sucursales.IgnoreQueryFilters()
            .Where(s => s.Activo && tenantsValidos.Contains(s.TenantId))
            .OrderBy(s => s.Nombre)
            .Select(s => new SedePublicaDto(s.Id, s.Nombre, s.Ciudad))
            .ToListAsync(ct);

        return sedes;
    }
}

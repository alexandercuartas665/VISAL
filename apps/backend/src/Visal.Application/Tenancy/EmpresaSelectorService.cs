using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Implementacion: si el usuario es global ve todos los tenants activos del SaaS;
/// si no, solo aquellos donde tiene un TenantUser activo.
/// </summary>
public sealed class EmpresaSelectorService(IApplicationDbContext db) : IEmpresaSelectorService
{
    public async Task<IReadOnlyList<EmpresaOpcionDto>> GetOpcionesAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var user = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (user is null) { return Array.Empty<EmpresaOpcionDto>(); }

        // Memberships activos del usuario (set de tenant ids donde el usuario es miembro real).
        var memberTenantIds = await db.TenantUsers.IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == platformUserId && tu.Status == PlatformUserStatus.Active)
            .Select(tu => tu.TenantId)
            .ToListAsync(ct);

        IQueryable<Domain.Entities.Tenant> q = db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial);

        if (!user.EsGlobal)
        {
            q = q.Where(t => memberTenantIds.Contains(t.Id));
        }

        var tenants = await q.OrderBy(t => t.Name).ToListAsync(ct);
        var memberSet = memberTenantIds.ToHashSet();
        return tenants
            .Select(t => new EmpresaOpcionDto(t.Id, t.Name, t.LegalName, memberSet.Contains(t.Id), user.EsGlobal && !memberSet.Contains(t.Id)))
            .ToList();
    }

    public async Task<EmpresaSeleccionResultado?> ResolverAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default)
    {
        var user = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (user is null) { return null; }

        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) { return null; }
        if (tenant.Status != TenantStatus.Active && tenant.Status != TenantStatus.Trial) { return null; }

        var membership = await db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(tu => tu.PlatformUserId == platformUserId && tu.TenantId == tenantId && tu.Status == PlatformUserStatus.Active, ct);

        if (membership is not null)
        {
            return new EmpresaSeleccionResultado(tenantId, membership.TenantRole.ToString(), false);
        }

        // Sin membership: solo permitido si el usuario es global. Entra como Owner virtual.
        if (user.EsGlobal)
        {
            return new EmpresaSeleccionResultado(tenantId, TenantRole.Owner.ToString(), true);
        }

        return null;
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class SedeSelectorService(IApplicationDbContext db) : ISedeSelectorService
{
    public async Task<IReadOnlyList<SucursalDto>> GetSedesAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default)
    {
        var user = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (user is null) { return Array.Empty<SucursalDto>(); }

        var tu = await db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.PlatformUserId == platformUserId && t.TenantId == tenantId && t.Status == PlatformUserStatus.Active, ct);

        // Sedes activas del tenant.
        var todas = await db.Sucursales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.Activo)
            .OrderBy(s => s.Nombre)
            .ToListAsync(ct);

        // Si el usuario es global y no tiene membership o no tiene sedes asignadas: ve todas.
        if (tu is null)
        {
            if (!user.EsGlobal) { return Array.Empty<SucursalDto>(); }
            return todas.Select(s => new SucursalDto(s.Id, s.Codigo, s.Nombre, s.Direccion, s.Ciudad, s.Telefono, s.Activo, s.MipresObligatorio)).ToList();
        }

        var asignadas = await db.TenantUserSucursales.IgnoreQueryFilters()
            .Where(x => x.TenantUserId == tu.Id)
            .Select(x => x.SucursalId)
            .ToListAsync(ct);

        if (asignadas.Count == 0 && user.EsGlobal)
        {
            return todas.Select(s => new SucursalDto(s.Id, s.Codigo, s.Nombre, s.Direccion, s.Ciudad, s.Telefono, s.Activo, s.MipresObligatorio)).ToList();
        }

        var set = asignadas.ToHashSet();
        return todas.Where(s => set.Contains(s.Id))
            .Select(s => new SucursalDto(s.Id, s.Codigo, s.Nombre, s.Direccion, s.Ciudad, s.Telefono, s.Activo, s.MipresObligatorio))
            .ToList();
    }

    public async Task<bool> PuedeAccederAsync(Guid platformUserId, Guid tenantId, Guid sucursalId, CancellationToken ct = default)
    {
        var sedes = await GetSedesAsync(platformUserId, tenantId, ct);
        return sedes.Any(s => s.Id == sucursalId);
    }
}

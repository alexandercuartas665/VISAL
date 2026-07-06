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

    public async Task<SedesParaUsuarioDto> ListParaUsuarioAsync(string usuario, CancellationToken ct = default)
    {
        // Fallback anti-enumeracion: input vacio o desconocido -> devolvemos lo mismo que
        // ListAsync + MostrarGlobal=true. Un atacante que pruebe correos al azar recibe la
        // misma respuesta que un usuario global, evitando confirmar la existencia.
        var todas = await ListAsync(ct);
        var fallback = new SedesParaUsuarioDto(todas, MostrarGlobal: true);

        var raw = (usuario ?? string.Empty).Trim();
        if (raw.Length == 0) return fallback;

        var lower = raw.ToLowerInvariant();
        var user = await db.PlatformUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u =>
                u.Email == lower ||
                u.Username == raw ||
                u.Documento == raw, ct);

        if (user is null || user.Status != PlatformUserStatus.Active)
        {
            return fallback;
        }

        // Tenants a los que pertenece este usuario (membresias activas).
        var memberships = await db.TenantUsers.IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
            .Select(tu => new { tu.Id, tu.TenantId })
            .ToListAsync(ct);

        if (memberships.Count == 0)
        {
            // Sin membresias activas: si es global, tiene sentido dejarlo elegir GLOBAL +
            // cualquier tenant. Si no, no puede entrar por sede: caemos al fallback silencioso.
            return user.EsGlobal ? fallback : fallback;
        }

        var tenantIds = memberships.Select(m => m.TenantId).Distinct().ToList();
        var tenantUserIds = memberships.Select(m => m.Id).ToList();

        // Sedes explicitamente asignadas al usuario (via TenantUserSucursal).
        var sedesAsignadasIds = await db.TenantUserSucursales.IgnoreQueryFilters()
            .Where(x => tenantUserIds.Contains(x.TenantUserId))
            .Select(x => x.SucursalId)
            .Distinct()
            .ToListAsync(ct);

        // Si el usuario es EsGlobal, mostramos todas las sedes activas de sus tenants
        // (o de todos los tenants activos si no tiene ninguna) para poder cambiar de sede
        // libremente, ademas de la opcion GLOBAL.
        if (user.EsGlobal)
        {
            var sedesGlobal = await db.Sucursales.IgnoreQueryFilters()
                .Where(s => s.Activo)
                .OrderBy(s => s.Nombre)
                .Select(s => new SedePublicaDto(s.Id, s.Nombre, s.Ciudad))
                .ToListAsync(ct);
            return new SedesParaUsuarioDto(sedesGlobal, MostrarGlobal: true);
        }

        // Usuario no global: solo las sedes asignadas. Sin la opcion GLOBAL.
        if (sedesAsignadasIds.Count == 0)
        {
            // Sin asignacion explicita: puede entrar a cualquier sede activa de sus tenants
            // (compat con la logica del handler /auth/login que acepta ese caso).
            var sedesTenant = await db.Sucursales.IgnoreQueryFilters()
                .Where(s => s.Activo && tenantIds.Contains(s.TenantId))
                .OrderBy(s => s.Nombre)
                .Select(s => new SedePublicaDto(s.Id, s.Nombre, s.Ciudad))
                .ToListAsync(ct);
            return new SedesParaUsuarioDto(sedesTenant, MostrarGlobal: false);
        }

        var sedesAsignadas = await db.Sucursales.IgnoreQueryFilters()
            .Where(s => s.Activo && sedesAsignadasIds.Contains(s.Id))
            .OrderBy(s => s.Nombre)
            .Select(s => new SedePublicaDto(s.Id, s.Nombre, s.Ciudad))
            .ToListAsync(ct);

        return new SedesParaUsuarioDto(sedesAsignadas, MostrarGlobal: false);
    }
}

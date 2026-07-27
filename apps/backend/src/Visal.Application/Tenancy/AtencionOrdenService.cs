using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed class AtencionOrdenService(IApplicationDbContext db) : IAtencionOrdenService
{
    // Nombre del permiso que exceptua del bloqueo por orden secuencial. Es el
    // mismo que aparece en ModuloCatalogo.Todos y se lee del claim "perms" en
    // el frontend.
    private const string PermisoSaltarOrden = "atencion.saltar-orden";

    public async Task<AtencionOrdenBloqueo?> ValidarAperturaAsync(Guid sesionId, Guid actorUserId, CancellationToken ct = default)
    {
        if (await UsuarioPuedeSaltarOrdenAsync(actorUserId, ct))
        {
            return null;
        }

        var sesion = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => s.Id == sesionId)
            .Select(s => new { s.Id, s.AsignacionTurnoId, s.SessionNo })
            .FirstOrDefaultAsync(ct);
        if (sesion is null) { return null; }

        var pendiente = await db.AsignacionTurnoSesiones.AsNoTracking()
            .Where(s => s.AsignacionTurnoId == sesion.AsignacionTurnoId
                     && s.SessionNo < sesion.SessionNo
                     && !s.Completado)
            .OrderBy(s => s.SessionNo)
            .Select(s => new { s.Id, s.SessionNo })
            .FirstOrDefaultAsync(ct);
        if (pendiente is null) { return null; }

        return new AtencionOrdenBloqueo(
            $"Debes cerrar la sesion {pendiente.SessionNo} antes de abrir la sesion {sesion.SessionNo}.",
            pendiente.SessionNo,
            sesion.AsignacionTurnoId,
            pendiente.Id);
    }

    /// <summary>
    /// Owner/Admin del tenant pasan libres (regla estandar del sistema, alineado
    /// con historias.reabrir). Los demas roles solo pasan si su rol_id tiene
    /// marcado el permiso <c>atencion.saltar-orden</c> en <c>rol_permisos</c>.
    /// Si el usuario no tiene fila en tenant_users (super admin puro o servicio
    /// sistema) fail-open: no se bloquea; ese caso no proviene del flujo UI.
    /// </summary>
    private async Task<bool> UsuarioPuedeSaltarOrdenAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) { return true; }

        var tenantUser = await db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == userId)
            .Select(u => new { u.TenantRole, u.RolId })
            .FirstOrDefaultAsync(ct);
        if (tenantUser is null) { return true; }

        if (tenantUser.TenantRole == TenantRole.Owner || tenantUser.TenantRole == TenantRole.Admin)
        {
            return true;
        }

        if (tenantUser.RolId is not Guid rolId) { return false; }

        return await db.RolPermisos.AsNoTracking()
            .AnyAsync(p => p.RolId == rolId && p.Modulo == PermisoSaltarOrden && p.Ver, ct);
    }
}

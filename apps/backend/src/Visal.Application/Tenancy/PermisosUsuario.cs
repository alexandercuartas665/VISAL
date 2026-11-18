using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Consulta server-side de permisos del usuario (rol_permisos con Ver=true), con Owner/Admin
/// del tenant como implicitos. Usar en reglas de negocio que no deben depender solo de la UI.
/// </summary>
public static class PermisosUsuario
{
    /// <summary>Permiso: el usuario (p. ej. coordinador) cierra HC sin el control del patron de
    /// cierre del tipo de servicio (no se le pide autorizacion).</summary>
    public const string SinControlCierre = "historias.sin-control-cierre";

    /// <summary>Permiso del modulo /autorizaciones (generar tokens, aprobar solicitudes).</summary>
    public const string Autorizaciones = "autorizaciones";

    /// <summary>
    /// True si el usuario tiene el permiso <paramref name="modulo"/> (Ver) en su rol, o es
    /// Owner/Admin del tenant. Usuario sin fila en tenant_users (super admin de plataforma o
    /// proceso del sistema) = true (fail-open, igual que atencion.saltar-orden). Guid.Empty = false.
    /// </summary>
    public static async Task<bool> TieneAsync(IApplicationDbContext db, Guid userId, string modulo, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) { return false; }
        var tu = await db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == userId)
            .Select(u => new { u.TenantRole, u.RolId })
            .FirstOrDefaultAsync(ct);
        if (tu is null) { return true; }
        if (tu.TenantRole is TenantRole.Owner or TenantRole.Admin) { return true; }
        if (tu.RolId is not Guid rolId) { return false; }
        return await db.RolPermisos.AsNoTracking()
            .AnyAsync(p => p.RolId == rolId && p.Modulo == modulo && p.Ver, ct);
    }
}

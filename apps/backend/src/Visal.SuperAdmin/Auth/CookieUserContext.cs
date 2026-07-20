using System.Security.Claims;
using Visal.Application.Common;

namespace Visal.SuperAdmin.Auth;

/// <summary>
/// ITenantContext para la consola unificada (cookie auth). Resuelve:
/// - UserId: del claim NameIdentifier del usuario autenticado.
/// - TenantId: del claim "tenant_id" si el usuario es miembro de un tenant; null para
///   operadores de plataforma (Super Admin), que no pertenecen a ningun tenant.
/// Asi las consultas tenant-scoped quedan aisladas automaticamente para usuarios de agencia.
///
/// Ola 8 RC8e — fallback a <see cref="TenantAmbient"/> cuando no hay HttpContext
/// (workers, IHostedService). Esto permite que el <c>PreRevisionIaWorker</c>
/// establezca el tenant activo por AsyncLocal antes de procesar un item, sin
/// tener que cambiar la firma de los servicios que resuelve.
/// </summary>
public sealed class CookieUserContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? TenantId
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is not null && Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var id))
            {
                return id;
            }
            return TenantAmbient.TenantId;
        }
    }

    public Guid? UserId
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is not null && Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            {
                return id;
            }
            return TenantAmbient.UserId;
        }
    }

    /// <summary>Sede sobre la que el usuario eligio operar en esta sesion. Null si el tenant
    /// solo tiene una sede o el usuario aun no la eligio.</summary>
    public Guid? SucursalId
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is not null && Guid.TryParse(http.User.FindFirst("sucursal_id")?.Value, out var id))
            {
                return id;
            }
            return TenantAmbient.SucursalId;
        }
    }
}

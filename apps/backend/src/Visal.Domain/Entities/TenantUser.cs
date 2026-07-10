using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Vinculo entre un PlatformUser y un tenant, con rol interno. Entidad TENANT-SCOPED:
/// recibe filtro global de consulta por TenantId.
/// </summary>
public class TenantUser : TenantEntity
{
    public Guid PlatformUserId { get; set; }
    public PlatformUser? PlatformUser { get; set; }

    public string Email { get; set; } = null!;
    public TenantRole TenantRole { get; set; } = TenantRole.Advisor;
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Active;

    /// <summary>Alcance de leads del asesor (los admin/owner/supervisor ven todo por rol).</summary>
    public LeadVisibility LeadVisibility { get; set; } = LeadVisibility.OwnOnly;

    /// <summary>Token de invitacion para que el asesor complete su registro (clave + foto). Null si ya activo.</summary>
    public string? InvitationToken { get; set; }
    public DateTimeOffset? InvitationExpiresAt { get; set; }

    /// <summary>Rol de permisos del usuario en esta entidad (modulo Roles y Permisos). Opcional.</summary>
    public Guid? RolId { get; set; }
    public Rol? Rol { get; set; }

    /// <summary>Sucursal/sede principal asignada al usuario (opcional). La lista completa
    /// de sedes a las que el usuario tiene acceso vive en TenantUserSucursal.</summary>
    public Guid? SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }

    /// <summary>Sedes (sucursales) adicionales que maneja el usuario. Soporte multi-sede.</summary>
    public List<TenantUserSucursal> Sucursales { get; set; } = new();

    // ---------- Permisos de COORDINACION (modulo Coordinacion) ----------
    // Antes los tipos coordinados vivian aqui como 4 booleans hardcodeados
    // (CoordinaTerapias/Consultas/Enfermeria/Equipos). Migraron a la tabla
    // tenant_user_tipos_coordinados para soportar tipos dinamicos del
    // catalogo (ver CatalogoTipoServicio). Ya no hay campos en esta entidad;
    // consultar via IApplicationDbContext.TenantUserTiposCoordinados.

    /// <summary>
    /// Si este usuario representa a un profesional clinico (terapeuta, medico, etc.),
    /// vinculo al catalogo de Profesionales para que el modulo de Atencion pueda
    /// listar sus turnos asignados. Null para usuarios administrativos.
    /// </summary>
    public Guid? ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }
}

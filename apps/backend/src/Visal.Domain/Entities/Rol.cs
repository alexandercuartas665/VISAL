using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Rol de usuario dentro de la entidad (define permisos por modulo). Tenant-scoped.</summary>
public class Rol : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Permiso de un rol sobre un modulo (ver/crear/editar/eliminar). Tenant-scoped.</summary>
public class RolPermiso : TenantEntity
{
    public Guid RolId { get; set; }
    public Rol? Rol { get; set; }

    /// <summary>Clave del modulo (admision, pacientes, cfg-aseguradoras, ...).</summary>
    public string Modulo { get; set; } = null!;
    public bool Ver { get; set; }
    public bool Crear { get; set; }
    public bool Editar { get; set; }
    public bool Eliminar { get; set; }
}

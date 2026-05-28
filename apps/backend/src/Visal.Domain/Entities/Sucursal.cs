using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Sucursal / sede que maneja la entidad. Tenant-scoped.</summary>
public class Sucursal : TenantEntity
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Tabla de union TenantUser <-> Sucursal. Permite que un usuario maneje varias sedes
/// dentro de su tenant. Entidad TENANT-SCOPED (filtro global por TenantId).
/// </summary>
public class TenantUserSucursal : TenantEntity
{
    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }

    public Guid SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
}

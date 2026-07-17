using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Vinculo N:N entre TurnoProgramacion y Sucursal. Una programacion aplica a
/// una o mas sedes del tenant; el coordinador de esa sede puede elegirla al
/// aplicar programacion en /coordinacion. La regla actual exige al menos una
/// sede vinculada (no hay "aplica a todas por comodin"): si quieres que una
/// programacion aplique a todo el tenant, se marcan las N sedes explicitamente.
///
/// PK compuesta (TurnoProgramacionId, SucursalId). Tenant scope se hereda
/// implicitamente porque ambas FKs son tenant-scoped.
/// </summary>
public class TurnoProgramacionSucursal : TenantEntity
{
    public Guid TurnoProgramacionId { get; set; }
    public TurnoProgramacion? TurnoProgramacion { get; set; }

    public Guid SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
}

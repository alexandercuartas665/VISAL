using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>N:M contrato_aseguradora <-> sucursal. Marca en que sedes esta activo un
/// contrato. Por ahora solo informativo (no se filtra operativamente); a futuro
/// alimenta filtros de asignacion/facturacion por sede. Tenant-scoped.</summary>
public class ContratoSucursal : TenantEntity
{
    public Guid ContratoAseguradoraId { get; set; }
    public ContratoAseguradora? ContratoAseguradora { get; set; }

    public Guid SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }
}

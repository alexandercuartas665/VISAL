using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Paquete comercial (ej. "ATENCION INTEGRAL DE PACIENTE AGUDO BAJA COMPLEJIDAD
/// PROGRAMA EXTENSION HOSPITALARIA (POR DIA). CODIGO E890167"). Se usa para
/// agrupar servicios de un contrato de aseguradora bajo un mismo paquete.
/// Es opcional: no todos los servicios estan asociados a un paquete.
/// Tenant-scoped. Codigo unico por tenant.
/// </summary>
public class Paquete : TenantEntity
{
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
}

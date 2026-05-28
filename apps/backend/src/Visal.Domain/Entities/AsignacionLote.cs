using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Lote de asignacion: agrupa los servicios que el usuario guarda en una sola operacion.
/// Reemplaza el TOKEN_AGRUPADO del sistema legacy VISAL_ASIGNACIONES con una FK explicita.
/// Tenant-scoped.
/// </summary>
public class AsignacionLote : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>Codigo de la sucursal/sede donde se origino el lote (snapshot).</summary>
    public string Sucursal { get; set; } = null!;

    /// <summary>Codigo del contrato comercial (snapshot por si cambia despues).</summary>
    public string ContratoCodigo { get; set; } = null!;

    /// <summary>Items del lote (1 fila por servicio asignado).</summary>
    public List<Asignacion> Items { get; set; } = new();
}

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

    /// <summary>Precio pactado del paquete completo. Al aplicar el paquete en /asignacion,
    /// este valor se copia al primer servicio del lote con cantidad > 0 (PaqueteValorPactado
    /// en Asignacion). Los demas servicios del mismo lote quedan con valor null porque uno
    /// solo debe llevar el monto del paquete para no duplicar facturacion. Editable por el
    /// coordinador antes de guardar. numeric.</summary>
    public decimal? Precio { get; set; }

    /// <summary>Servicios que componen el paquete. Al elegir un ServicioContrato con
    /// PaqueteId != null en /asignacion, estos servicios se expanden al carrito para que
    /// el coordinador los revise y guarde en bloque.</summary>
    public List<PaqueteServicio> Servicios { get; set; } = new();

    /// <summary>
    /// FK opcional al <see cref="PaqueteServicio"/> "representativo" para facturacion.
    /// Cuando el paquete se factura, el snapshot de Relacion de Facturas emite UNA sola
    /// fila con el CUPS + descripcion + Cantidad del servicio marcado aqui, y
    /// <c>Valor Unitario = Paquete.Precio</c>. Los demas servicios del paquete no
    /// generan fila propia. Nullable — si el paquete aun no tiene representativo,
    /// el builder cae a "primer PaqueteServicio.OrderBy(Codigo)" como fallback.
    /// <c>ON DELETE SET NULL</c>.
    /// </summary>
    public Guid? CupsRepresentativoServicioId { get; set; }
    public PaqueteServicio? CupsRepresentativoServicio { get; set; }
}

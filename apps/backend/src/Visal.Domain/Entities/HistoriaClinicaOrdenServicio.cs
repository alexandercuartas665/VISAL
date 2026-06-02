using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la "Orden a Servicios" de una Historia Clinica. Cada fila corresponde
/// a un servicio (CUPS, terapia, etc.) prescrito durante la atencion. La cabecera
/// "orden" es virtual: agrupamos todos los items que comparten HistoriaClinicaId.
/// El catalogo origen es la tabla de servicios de los contratos de Aseguradoras
/// (ServicioContrato). Si el contrato/servicio cambia despues, conservamos un
/// snapshot del Codigo y la Descripcion del servicio en el item.
/// </summary>
public class HistoriaClinicaOrdenServicio : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>FK al catalogo de servicios de contratos de aseguradoras.
    /// Null si el profesional escribio un servicio que no estaba en el catalogo.</summary>
    public Guid? ServicioContratoId { get; set; }
    public ServicioContrato? ServicioContrato { get; set; }

    /// <summary>Snapshot del codigo del servicio al momento de agregarlo (campo
    /// que el legacy llama "COD. SERVICIO"). Es lo que viaja a la orden impresa.</summary>
    public string? CodigoServicio { get; set; }

    /// <summary>Snapshot de la descripcion del servicio (lo visible para el usuario).</summary>
    public string Descripcion { get; set; } = null!;

    /// <summary>Cantidad solicitada (texto libre: "1", "10 sesiones", etc.).</summary>
    public string? Cantidad { get; set; }

    public string? Observaciones { get; set; }

    public int Orden { get; set; }
}

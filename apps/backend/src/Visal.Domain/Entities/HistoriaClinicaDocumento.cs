using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Instancia de un documento secundario atado a una HC: una nota de EVOLUCION o
/// un CONSENTIMIENTO informado. Comparte la misma mecanica que
/// HistoriaClinicaEscala (un FormDefinition + valores JSON + estado), pero los
/// formatos disponibles NO salen del catalogo por tipo, sino de la tabla
/// relaciones_formulario filtrada por el FormDefinitionId de la HC padre y el
/// TipoRelacion correspondiente (EVOLUCION / CONSENTIMIENTO).
///
/// El campo <see cref="Tipo"/> permite reutilizar la misma tabla para ambos
/// usos sin duplicar entidades. Valores soportados por la UI: "EVOLUCION" y
/// "CONSENTIMIENTO".
///
/// Tenant-scoped. Estado sigue el mismo ciclo de vida que HistoriaClinica
/// (Abierta -> Cerrada / Inactiva).
/// </summary>
public class HistoriaClinicaDocumento : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    public Guid FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>Categoria del documento: "EVOLUCION" o "CONSENTIMIENTO".</summary>
    public string Tipo { get; set; } = "EVOLUCION";

    /// <summary>Diccionario clave→valor diligenciado, guardado como jsonb.</summary>
    public string ValoresJson { get; set; } = "{}";

    public HistoriaClinicaEstado Estado { get; set; } = HistoriaClinicaEstado.Abierta;

    public DateTimeOffset FechaApertura { get; set; }
    public DateTimeOffset? FechaCierre { get; set; }

    /// <summary>Profesional que lo diligencio (cache de nombre para mostrar sin join).</summary>
    public string? EspecialistaNombre { get; set; }
}

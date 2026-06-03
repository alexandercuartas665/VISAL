using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Instancia de una "escala" diligenciada para una HC. Es esencialmente una
/// historia secundaria atada a una HC padre: usa un FormDefinition de tipo
/// ESCALAS y guarda sus valores como JSON. El doctor solo puede iniciar una
/// escala si la HC padre esta abierta o cerrada (no inactiva).
///
/// Tenant-scoped. El estado sigue el mismo ciclo de vida que HistoriaClinica
/// (Abierta -> Cerrada / Inactiva).
/// </summary>
public class HistoriaClinicaEscala : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    public Guid FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>Diccionario clave→valor diligenciado, guardado como jsonb.</summary>
    public string ValoresJson { get; set; } = "{}";

    public HistoriaClinicaEstado Estado { get; set; } = HistoriaClinicaEstado.Abierta;

    public DateTimeOffset FechaApertura { get; set; }
    public DateTimeOffset? FechaCierre { get; set; }

    /// <summary>Profesional que la diligencio (cache de nombre para mostrar sin join).</summary>
    public string? EspecialistaNombre { get; set; }
}

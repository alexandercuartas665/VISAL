using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la "Orden de Incapacidad" de una Historia Clinica. No depende de
/// catalogo — los datos los digita el profesional durante la atencion.
/// </summary>
public class HistoriaClinicaIncapacidad : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    public string Motivo { get; set; } = null!;
    public DateOnly? FechaDesde { get; set; }
    public DateOnly? FechaHasta { get; set; }
    /// <summary>Snapshot de los dias calculados (FechaHasta - FechaDesde + 1).
    /// Se guarda persistido para sobrevivir cambios futuros de fechas.</summary>
    public int? Dias { get; set; }
    /// <summary>"ENFERMEDAD GENERAL" | "ACCIDENTE DE TRABAJO" (texto libre, no enum).</summary>
    public string? Tipo { get; set; }

    public int Orden { get; set; }
}

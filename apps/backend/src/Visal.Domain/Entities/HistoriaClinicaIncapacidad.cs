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
    /// <summary>Tipo de incapacidad. Se alimenta del catalogo configurable
    /// <see cref="Visal.Domain.Enums.CatalogoPacienteTipo.TipoIncapacidad"/> (/cfg-pacientes).
    /// Se guarda el Nombre del catalogo como snapshot (texto, no FK) para sobrevivir cambios
    /// posteriores del catalogo. Valores sembrados por defecto: "ENFERMEDAD GENERAL" /
    /// "ACCIDENTE DE TRABAJO".</summary>
    public string? Tipo { get; set; }

    /// <summary>"INICIAL" | "PRORROGA". Identifica si la incapacidad es la primera o una
    /// prorroga de una previa. Select fijo en la captura (no catalogo).</summary>
    public string? InicialOProrroga { get; set; }

    /// <summary>Marca si la incapacidad es retroactiva (su fecha de inicio es anterior a la
    /// fecha de expedicion). Lo decide el profesional con un Si/No en la captura.</summary>
    public bool Retroactiva { get; set; }

    public int Orden { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la "Orden de Medicamentos" de una Historia Clinica. Cada fila
/// corresponde a un medicamento recetado durante esa atencion.
/// La cabecera "orden" es virtual: agrupamos todos los items que comparten
/// HistoriaClinicaId. El profesional puede agregar y editar items hasta que
/// la historia se cierre.
/// </summary>
public class HistoriaClinicaMedicamento : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>FK al catalogo de medicamentos (CUM). Null si el profesional
    /// agrego un medicamento que no esta en el catalogo.</summary>
    public Guid? MedicamentoId { get; set; }
    public Medicamento? Medicamento { get; set; }

    /// <summary>Snapshot del nombre del medicamento al momento de agregarlo.
    /// Sobrevive a cambios del catalogo posteriores.</summary>
    public string NombreMedicamento { get; set; } = null!;

    /// <summary>Snapshot del codigo del medicamento al momento de agregarlo.
    /// Tipicamente el RegistroSanitario INVIMA o el IUM. Visible en la orden
    /// impresa.</summary>
    public string? CodigoMedicamento { get; set; }

    /// <summary>Cantidad por toma (texto libre, ej "1", "0.5", "2 tabletas").</summary>
    public string? Cantidad { get; set; }

    /// <summary>Frecuencia (texto libre, ej "cada 8 horas").</summary>
    public string? Frecuencia { get; set; }

    /// <summary>Dias de tratamiento (texto libre, ej "7", "indefinido").</summary>
    public string? Dias { get; set; }

    /// <summary>Texto humano de la posologia ya armada: "1 cada 8 horas durante 7 dias".</summary>
    public string? Posologia { get; set; }

    /// <summary>Observacion clinica del medicamento (libre).</summary>
    public string? Observacion { get; set; }

    /// <summary>
    /// URL del formato MIPRES radicado ante la EPS (opcional en general,
    /// obligatorio cuando la sede tiene MipresObligatorio=true). Guarda el link
    /// completo del formato tal como lo copie el profesional desde
    /// mipres.sispro.gov.co para trazabilidad de la entrega.
    /// </summary>
    public string? MipresUrl { get; set; }

    public int Orden { get; set; }
}

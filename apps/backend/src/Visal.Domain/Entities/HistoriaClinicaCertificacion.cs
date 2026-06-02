using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la "Orden de Certificacion" de una Historia Clinica. Cada item
/// es un certificado (asistencia, incapacidad, etc.) con un titulo corto y
/// un cuerpo libre. No hay catalogo — el profesional escribe el texto.
/// </summary>
public class HistoriaClinicaCertificacion : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>Etiqueta corta del certificado (ej: "Certificado de asistencia").</summary>
    public string Titulo { get; set; } = null!;

    /// <summary>Cuerpo libre del certificado.</summary>
    public string Contenido { get; set; } = null!;

    public int Orden { get; set; }
}

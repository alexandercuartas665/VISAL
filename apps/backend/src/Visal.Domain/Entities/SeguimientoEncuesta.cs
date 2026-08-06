using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Encuesta SIAU de satisfaccion: un registro por paciente y mes.
/// Se materializa on-demand para pacientes con actividad clinica en el mes
/// (asignacion / HC creada en ese periodo). El kanban de /seguimiento
/// enruta los registros por Estado.
/// </summary>
public class SeguimientoEncuesta : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>Periodo de la encuesta: entero YYYYMM (p. ej. 202608).</summary>
    public int Mes { get; set; }

    /// <summary>Pendiente | Realizada | NoContactado</summary>
    public string Estado { get; set; } = "Pendiente";

    public DateTime? FechaLlamada { get; set; }
    public Guid? ResponsableLlamadaId { get; set; }
    public string? ResponsableLlamadaNombre { get; set; }

    /// <summary>Preguntas de calidad SIAU (escala 1-5). Null = sin responder.</summary>
    public int? Pregunta1 { get; set; }
    public int? Pregunta2 { get; set; }
    public int? Pregunta3 { get; set; }
    public int? Pregunta4 { get; set; }
    public int? Pregunta5 { get; set; }

    public string? PersonaAtiende { get; set; }
    public string? Observaciones { get; set; }
}

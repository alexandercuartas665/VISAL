using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Bitacora de ingreso/admision de un paciente (Ola 4b - CENSO "Ingreso de Pacientes").
/// Captura los hitos con hora real (DateTimeOffset) para poder medir el SLA en minutos
/// entre que la IPS comenta/remite y la respuesta. Tenant-scoped. Es una entidad aparte
/// de <see cref="Paciente"/> (que solo tenia fechas sin hora) para no tocar el flujo de
/// Admision existente.
/// </summary>
public class PacienteIngreso : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>Fecha-hora en que la IPS comenta/remite al paciente.</summary>
    public DateTimeOffset? FechaComentan { get; set; }

    /// <summary>Fecha-hora de la respuesta (de nuestra IPS) al comentario.</summary>
    public DateTimeOffset? FechaRespuesta { get; set; }

    /// <summary>SLA en minutos entre comentan y respuesta. Se calcula al guardar cuando
    /// ambas fechas estan presentes; null si falta alguna.</summary>
    public int? SlaMinutos { get; set; }

    /// <summary>Nombre de la IPS que comenta/remite (texto; catalogo formal es opcional).</summary>
    public string? IpsComenta { get; set; }

    /// <summary>Nombre del responsable del ingreso.</summary>
    public string? ResponsableIngreso { get; set; }

    /// <summary>Si el paciente efectivamente ingreso (SI/NO explicito del censo).</summary>
    public bool Ingreso { get; set; } = true;

    /// <summary>Motivo de no ingreso (cuando <see cref="Ingreso"/> es false).</summary>
    public string? MotivoNoIngreso { get; set; }

    public string? Observaciones { get; set; }
}

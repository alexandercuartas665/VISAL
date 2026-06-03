using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Regla de automatizacion del tenant (modulo 2.5). Entidad TENANT-SCOPED. Define un disparador
/// (sin respuesta N horas / entra a etapa / lead nuevo) y una accion (crear seguimiento / alertar).
/// Se puede encender o apagar.
/// </summary>
public class AutomationRule : TenantEntity
{
    public string Name { get; set; } = null!;

    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.NoReply;

    /// <summary>Umbral de minutos sin respuesta para el disparador NoReply.</summary>
    public int ThresholdMinutes { get; set; } = 30;

    /// <summary>Etapa objetivo para el disparador StageEntered.</summary>
    public Guid? StageId { get; set; }

    /// <summary>Ventana horaria (HH:mm) para el disparador ChatInTimeWindow.</summary>
    public string? TimeWindowStart { get; set; }
    public string? TimeWindowEnd { get; set; }

    public AutomationAction Action { get; set; } = AutomationAction.CreateFollowUp;

    /// <summary>Titulo de la tarea de seguimiento generada (accion CreateFollowUp).</summary>
    public string? FollowUpTitle { get; set; }

    /// <summary>Categoria de pregrabado para responder (accion CreateLeadAndReply).</summary>
    public string? TemplateCategory { get; set; }

    /// <summary>Nombre del turno destino (accion AssignToShift).</summary>
    public string? ShiftName { get; set; }

    /// <summary>
    /// Agente IA que debe ejecutar la accion cuando esta requiere IA
    /// (ej: AutomationAction.ReviewMedicalNotesWithAi). Null si la accion no usa IA.
    /// </summary>
    public Guid? AiAgentId { get; set; }
    public AiAgent? AiAgent { get; set; }

    /// <summary>
    /// Solo aplica a la accion ReviewMedicalNotesWithAi. Si esta encendido,
    /// el chat IA del modal de notas dispara automaticamente una revision
    /// cuando el doctor hace "Guardado Parcial".
    /// </summary>
    public bool RevisarAlGuardarParcial { get; set; }

    /// <summary>
    /// Solo aplica a la accion ReviewMedicalNotesWithAi. Si esta encendido,
    /// el chat IA del modal de notas dispara automaticamente una revision
    /// cuando el doctor hace "Guardado Definitivo".
    /// </summary>
    public bool RevisarAlGuardarDefinitivo { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Conteo de ejecuciones acumuladas (estadistica de la tarjeta).</summary>
    public int ExecutionCount { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
}

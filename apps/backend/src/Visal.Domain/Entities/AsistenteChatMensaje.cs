using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Mensaje del historial de conversacion entre un asistente IA y el equipo
/// medico, atado a un paciente. El historial es PROPIO DEL PACIENTE: al
/// abrir cualquier nota de ese paciente se ve la misma conversacion. Si la
/// regla de automatizacion cambia el agente, los mensajes nuevos se generan
/// con el agente activo, pero el historial previo se conserva intacto.
/// </summary>
public class AsistenteChatMensaje : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>"user" | "assistant" | "system" (divider).</summary>
    public string Rol { get; set; } = "user";

    public string Texto { get; set; } = "";

    /// <summary>Momento en que se produjo el mensaje (no necesariamente igual a CreatedAt si se sembro retroactivo).</summary>
    public DateTimeOffset Cuando { get; set; }

    // ---- Auditoria opcional ----

    /// <summary>HC abierta en el modal en el momento del mensaje (snapshot, sin FK estricto).</summary>
    public Guid? HistoriaClinicaId { get; set; }

    /// <summary>Nota en redaccion en el momento del mensaje (snapshot).</summary>
    public Guid? NotaMedicaId { get; set; }

    /// <summary>Agente que respondio (cuando aplica). Snapshot del id.</summary>
    public Guid? AgenteId { get; set; }

    /// <summary>Cache del nombre del agente para historicos donde el agente ya fue borrado.</summary>
    public string? AgenteNombreSnapshot { get; set; }
}

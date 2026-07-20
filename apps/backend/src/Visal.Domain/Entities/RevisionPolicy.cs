using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Politica singleton por tenant que controla el comportamiento automatico del
/// ciclo de revision (Capa 08 Ola 4). Una fila por tenant (UNIQUE index sobre
/// <c>TenantId</c>); cuando no existe fila se aplican los defaults del enum.
///
/// Este singleton es leido por:
///   - `HistoriaClinicaService.CerrarAsync` — para saber si dispara el ciclo automatico.
///   - `IRevisionClinicaService.InactivarAsync` — para validar largo minimo del motivo.
///   - `RevisionKanbanService` — para pedir confirmacion antes de aprobar (Ola 6 backlog).
///   - `IPreRevisionIAService` — para el gate del agente IA (Ola 5).
///
/// La politica NO es sensible: puede quedar en logs de auditoria sin revelar
/// secretos. La modifica el Owner del tenant desde `/config/revision-policy`.
/// </summary>
public class RevisionPolicy : TenantEntity
{
    /// <summary>
    /// Si es true, al pulsar "Guardar definitivamente" una HC (cerrarla) se crea
    /// automaticamente su revision (evento `SolicitudCreada`), quedando en la
    /// columna Cerradas del Kanban sin intervencion manual. False = el profesional
    /// debe pulsar "Enviar a revision" desde el tab Revision del modal HC.
    /// </summary>
    public bool AutoTriggerCierre { get; set; }

    /// <summary>
    /// Reservado Ola 5: si es true, disparar el agente IA para pre-revision
    /// apenas se crea la <see cref="RevisionClinica"/>. Por ahora no se lee.
    /// </summary>
    public bool PreRevisionIAAutoTrigger { get; set; }

    /// <summary>
    /// Reservado Ola 6: cuando <c>UmbralConfianza</c> se cumple, el agente
    /// mueve la revision a Aprobada / Rechazada sin pedir humano. Por ahora
    /// no se lee.
    /// </summary>
    public bool AdopcionAutomaticaAgente { get; set; }

    /// <summary>Umbral 0..1 para adopcion automatica del veredicto del agente. Default 0.95.</summary>
    public decimal UmbralConfianza { get; set; } = 0.95m;

    /// <summary>
    /// Ventana en dias hacia atras que el agente considera al buscar
    /// asignaciones relacionadas al paciente (contexto para la pre-revision).
    /// Default 30 dias.
    /// </summary>
    public int VentanaAsignacionesRelacionadasDias { get; set; } = 30;

    /// <summary>Si es true, la UI abre un modal de confirmacion antes de mover una card a Aprobadas.</summary>
    public bool ConfirmarAprobado { get; set; }

    /// <summary>
    /// Minimo de caracteres exigido en el motivo de <c>Inactivar</c>. Default 10.
    /// Solo aplica a inactivacion (rechazo exige texto no vacio en cualquier caso).
    /// </summary>
    public int MotivoInactivacionMinChars { get; set; } = 10;

    /// <summary>
    /// Ola 8 RC8c — si es true, cuando el revisor mueve una card a Rechazadas se
    /// dispara un mensaje WhatsApp al profesional autor con el motivo y un link
    /// deep al modal HC. Requiere que el profesional tenga telefono y linea WA
    /// activa; si algo falla el rechazo NO se aborta (solo se loguea el error).
    /// Default false para no sorprender a tenants que aun no configuraron HSM
    /// para este proceso.
    /// </summary>
    public bool NotificarRechazoWhatsApp { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Evento en la bitacora append-only del ciclo de revision. Nunca se UPDATE ni
/// DELETE — es la fuente de verdad del historial. La cabecera
/// <see cref="RevisionClinica"/> solo cachea el ultimo estado derivado; si
/// alguna vez hay drift, la bitacora manda.
///
/// El agente IA y el sistema tambien escriben aqui (ver <see cref="ActorTipo"/>).
/// Todo evento carga <c>TenantId</c> y el <c>RevisionClinicaId</c> al que pertenece.
/// </summary>
public class RevisionClinicaEvento : TenantEntity
{
    public Guid RevisionClinicaId { get; set; }
    public RevisionClinica? RevisionClinica { get; set; }

    /// <summary>Tipo de evento. Ver <see cref="RevisionTipoEvento"/>.</summary>
    public RevisionTipoEvento Tipo { get; set; }

    /// <summary>Veredicto asociado al evento. Neutral cuando el evento no expresa juicio.</summary>
    public RevisionResultado Resultado { get; set; } = RevisionResultado.Neutral;

    /// <summary>Quien genero el evento: humano, agente IA o el sistema por trigger.</summary>
    public RevisionActorTipo ActorTipo { get; set; }

    /// <summary>PlatformUserId cuando <see cref="ActorTipo"/> = Humano. Null para agente/sistema.</summary>
    public Guid? ActorUsuarioId { get; set; }

    /// <summary>Identificador del agente IA cuando <see cref="ActorTipo"/> = Agente (AgenteIA.Id o codigo interno).</summary>
    public string? ActorAgenteCodigo { get; set; }

    /// <summary>
    /// Numero de iteracion del ciclo al que pertenece el evento. Copia
    /// del <see cref="RevisionClinica.IteracionActual"/> vigente al momento del append.
    /// Permite reconstruir historial de una iteracion especifica sin recorrer toda la bitacora.
    /// </summary>
    public int Iteracion { get; set; }

    /// <summary>Motivo textual. OBLIGATORIO en eventos con Resultado = <c>Rechazado</c>.</summary>
    public string? Motivo { get; set; }

    /// <summary>Nota libre del revisor humano (o texto sintetizado por el agente).</summary>
    public string? Nota { get; set; }

    /// <summary>
    /// Payload estructurado opcional en jsonb. Usado sobre todo por el agente IA para dejar
    /// checklist detallada, tokens consumidos, correlation-id, y por eventos automaticos que
    /// referencian datos externos. Mantener &lt; ~4 KB por evento.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>Timestamp de creacion del evento. Duplica <c>CreatedAt</c> para orden explicito en la UI.</summary>
    public DateTimeOffset OcurridoEn { get; set; }
}

/// <summary>
/// Tipos de evento posibles en la bitacora. Ampliar aqui rompe el enum en BD, asi que
/// cada nuevo valor requiere migracion + prueba de compatibilidad con las cards del Kanban.
/// </summary>
public enum RevisionTipoEvento
{
    /// <summary>Se abrio el ciclo. Puede ser trigger automatico al cerrar HC o solicitud manual.</summary>
    SolicitudCreada = 0,

    /// <summary>El agente IA emitio veredicto de pre-revision (Aprobado/Rechazado/Neutral).</summary>
    PreRevisionAgente = 1,

    /// <summary>Un revisor humano tomo la revision (o el sistema le asigno via round-robin).</summary>
    AsignacionRevisor = 2,

    /// <summary>El revisor humano aprobo. <c>Resultado</c> = Aprobado. Motivo opcional.</summary>
    Aprobado = 3,

    /// <summary>El revisor humano rechazo. <c>Resultado</c> = Rechazado. Motivo OBLIGATORIO.</summary>
    Rechazado = 4,

    /// <summary>El profesional autor cerro y reenvio la HC tras un rechazo. Incrementa <see cref="RevisionClinica.IteracionActual"/>.</summary>
    Reenvio = 5,

    /// <summary>Se rectifico un aprobado previo (mecanismo de auditoria). Regresa a EnRevision.</summary>
    Rectificacion = 6,

    /// <summary>Un revisor con permiso final archivo. Estado terminal ArchivadaOk.</summary>
    ArchivadoOk = 7,

    /// <summary>Baja logica de la HC del ciclo (duplicada / error de captura). Estado terminal Inactivada.</summary>
    Inactivacion = 8,

    /// <summary>El sistema asigno automaticamente un revisor (Ola 6 - round robin). Sin veredicto.</summary>
    AsignacionAutomatica = 9,
}

/// <summary>
/// Veredicto asociado a un evento. La revision del agente y del humano usan el mismo enum
/// pero el agente nunca cambia <see cref="RevisionClinica.EstadoAgregado"/> — solo
/// <see cref="RevisionClinica.EstadoAgente"/>.
/// </summary>
public enum RevisionResultado
{
    /// <summary>Sin juicio (eventos de flujo como AsignacionRevisor, SolicitudCreada, Reenvio).</summary>
    Neutral = 0,

    /// <summary>Aprobacion (humana o agente).</summary>
    Aprobado = 1,

    /// <summary>Rechazo (humano o agente). Requiere motivo.</summary>
    Rechazado = 2,
}

/// <summary>
/// Origen del evento. Determina que campo actor esta poblado.
/// </summary>
public enum RevisionActorTipo
{
    /// <summary>Un usuario humano — <c>ActorUsuarioId</c> obligatorio.</summary>
    Humano = 0,

    /// <summary>Un agente IA — <c>ActorAgenteCodigo</c> obligatorio.</summary>
    Agente = 1,

    /// <summary>El sistema — trigger automatico, asignacion round-robin, etc. Sin actor.</summary>
    Sistema = 2,
}

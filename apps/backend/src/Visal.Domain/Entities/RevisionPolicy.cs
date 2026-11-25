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

    /// <summary>
    /// Si es true, la pre-revision IA recibe ademas el HISTORIAL del paciente: las historias
    /// ANTERIORES (por fecha de atencion) dentro de la ventana <see cref="HistorialMeses"/>, como
    /// contexto separado del documento a revisar. Default true (la migracion lo enciende).
    /// </summary>
    public bool EnviarHistorialPaciente { get; set; } = true;

    /// <summary>Meses hacia atras (desde la fecha de atencion de la HC revisada) que cubre el historial. Default 8.</summary>
    public int HistorialMeses { get; set; } = 8;

    /// <summary>Maximo de historias anteriores que van COMPLETAS (las mas recientes, priorizando el mismo
    /// tipo de servicio); el resto de la ventana va solo como indice. Default 30.</summary>
    public int HistorialMaxCompletas { get; set; } = 30;

    /// <summary>Presupuesto aproximado de tokens del bloque de historial (chars/4). Default 60000.</summary>
    public int HistorialMaxTokens { get; set; } = 60000;

    /// <summary>Auditoria IA AL CERRAR (Atencion -> "Guardar definitivamente"): el agente revisa la
    /// HC antes de cerrarla y muestra el resultado al profesional. Informativo, no bloquea el cierre.
    /// Apagado por defecto (consume tokens por cada cierre).</summary>
    public bool AuditoriaIaAlCerrar { get; set; }

    /// <summary>Codigos de formato (FormDefinition.Codigo) que se auditan al cerrar, como array JSON.
    /// Null o vacio = todos los formatos.</summary>
    public string? AuditoriaIaFormatosJson { get; set; }

    /// <summary>Segundos maximos que el profesional espera la auditoria al cerrar; si se pasa, la HC
    /// se cierra normal y la auditoria sigue en segundo plano.</summary>
    public int AuditoriaIaTimeoutSeg { get; set; } = 180;

    /// <summary>JSON con los tipos de servicio de la asignacion (TERAPIA, ENFERMERIA, CONSULTA, EQUIPOS,
    /// INSUMOS) cuyas HC se auditan al cerrar. Null/vacio = todos. Con lista, una HC sin asignacion
    /// (creada por fuera de Coordinacion) NO se audita. Se combina (Y) con la lista de formatos.</summary>
    public string? AuditoriaIaTiposServicioJson { get; set; }

    /// <summary>JSON objeto {TIPO: maxIntentos} con los tipos de servicio cuya auditoria al cerrar es
    /// BLOQUEANTE (IMP-68): si el agente rechaza, el profesional debe corregir; al agotar maxIntentos
    /// rechazos debe pedir autorizacion en /autorizaciones. Tipos ausentes = modo informativo.
    /// Null/vacio = todo informativo (comportamiento historico).</summary>
    public string? AuditoriaIaBloqueoJson { get; set; }
}

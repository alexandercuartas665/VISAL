using Visal.Domain.Entities;

namespace Visal.Application.Revision.Ia;

/// <summary>
/// Resultado de una ejecucion de pre-revision IA. Cuando <see cref="Ok"/> es false
/// hay un motivo humano en <see cref="Error"/>. Cuando es true, el veredicto quedo
/// registrado en la bitacora como evento <c>PreRevisionAgente</c>.
/// </summary>
public sealed record PreRevisionIaResult(
    bool Ok,
    RevisionResultado? Resultado,
    decimal Confianza,
    string? Nota,
    IReadOnlyList<string>? Hallazgos,
    string? Error,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<SugerenciaTexto>? Sugerencias = null);

/// <summary>Redaccion mejorada que propone el agente para un campo con hallazgo. Solo lectura:
/// nunca se aplica sola a la HC. Usa solo hechos ya escritos; lo faltante va como [COMPLETAR: ...].</summary>
public sealed record SugerenciaTexto(string Campo, string TextoSugerido);

/// <summary>Datos de la corrida del agente necesarios para registrar el log SIN volver a llamar al
/// proveedor (auditoria al cerrar: se audita con la HC abierta y se registra tras el cierre).</summary>
public sealed record AuditoriaIaTraza(
    string AgenteNombre, string Proveedor, string Modelo,
    string SystemPrompt, string ContextoJson, HistoriaClinicaContextoIa? Contexto,
    string? RespuestaCruda, string HashValores);

/// <summary>Resultado de AUDITAR una HC con el agente (llamada al LLM + parseo), todavia sin
/// registrarlo en el ciclo de revision.</summary>
public sealed record AuditoriaIaResultado(
    bool Ok,
    string? Error,
    RevisionResultado? Resultado,
    decimal Confianza,
    string? Nota,
    IReadOnlyList<string> Hallazgos,
    IReadOnlyList<SugerenciaTexto> Sugerencias,
    int InputTokens,
    int OutputTokens)
{
    public Guid HistoriaClinicaId { get; init; }
    public AuditoriaIaTraza? Traza { get; init; }
    /// <summary>Log ya guardado para esta corrida (intento de cierre). Al registrar el cierre se
    /// vincula ese mismo log al ciclo en vez de crear otro.</summary>
    public Guid? LogId { get; init; }

    public static AuditoriaIaResultado Fallo(Guid hcId, string error, int inTok = 0, int outTok = 0, AuditoriaIaTraza? traza = null)
        => new(false, error, null, 0m, null, Array.Empty<string>(), Array.Empty<SugerenciaTexto>(), inTok, outTok)
        {
            HistoriaClinicaId = hcId,
            Traza = traza,
        };
}

/// <summary>Configuracion efectiva de la auditoria al cerrar para una HC (null = no aplica).</summary>
public sealed record AuditoriaCierreConfig(int TimeoutSeg);

/// <summary>Estado del modo BLOQUEANTE de la auditoria al cerrar para una HC y un usuario (IMP-68).
/// <see cref="Rechazos"/> cuenta los rechazos del agente desde el ultimo reinicio (reapertura de la HC
/// o solicitud rechazada por el coordinador). <see cref="SolicitudEstado"/>: Pendiente / Aprobada /
/// Rechazada (la ultima solicitud de cierre por auditoria IA) o null.</summary>
public sealed record BloqueoIaEstado(
    int MaxIntentos,
    int Rechazos,
    string? SolicitudEstado = null,
    string? SolicitudRespuesta = null,
    DateTimeOffset? SolicitudEn = null)
{
    public bool Agotado => Rechazos >= MaxIntentos;
    public int Restantes => Math.Max(0, MaxIntentos - Rechazos);
    public bool Autorizada => SolicitudEstado == "Aprobada";
    public bool Pendiente => SolicitudEstado == "Pendiente";
}

/// <summary>
/// Orquestador del agente REVISOR CLINICO IA. Carga contexto via
/// <see cref="IRevisionMcpToolsService"/>, arma el prompt, llama al AI Provider
/// Gateway, parsea el veredicto y lo persiste como evento
/// <c>PreRevisionAgente</c> en la bitacora. En esta version NO hay tool-calling
/// nativo: las 9 tools se cargan up-front antes de invocar al LLM.
///
/// Reglas duras (capa IA):
///   - Multi-tenant via global query filter.
///   - No ejecuta si el tenant esta sobre cupo con limite duro.
///   - Todo consumo se registra en <c>IAiUsageService</c> con source = "revision".
///   - Nunca cambia <c>RevisionClinica.EstadoAgregado</c> — solo <c>EstadoAgente</c>.
/// </summary>
public interface IPreRevisionIaService
{
    /// <summary>Nombre canonico del agente que ejecuta la pre-revision cuando
    /// el llamador no elige uno especifico. Fallback usado por triggers
    /// automaticos (auto-revision al cerrar HC) y por el boton legacy del Kanban.</summary>
    public const string AgenteNombre = "REVISOR CLINICO IA";

    /// <summary>
    /// Ejecuta la pre-revision para el ciclo indicado.
    /// <paramref name="agenteId"/> permite forzar un agente distinto al canonico
    /// (por ejemplo, cuando el usuario elige otro desde el selector de /ordenes).
    /// Si es null, se resuelve por <see cref="AgenteNombre"/> — comportamiento
    /// historico usado por triggers automaticos.
    /// </summary>
    Task<PreRevisionIaResult> EjecutarAsync(Guid revisionClinicaId, Guid? agenteId = null, CancellationToken ct = default);

    /// <summary>Audita la HC (contexto + LLM + parseo) SIN registrar nada en el ciclo de revision.
    /// Solo deja el consumo de tokens. Funciona con la HC abierta (auditoria al cerrar).</summary>
    Task<AuditoriaIaResultado> AuditarHcAsync(Guid historiaClinicaId, Guid? agenteId = null, CancellationToken ct = default);

    /// <summary>Si la auditoria al cerrar aplica a esta HC (politica encendida + formato incluido),
    /// devuelve su configuracion; si no, null.</summary>
    Task<AuditoriaCierreConfig?> ConfigAuditoriaAlCerrarAsync(Guid historiaClinicaId, CancellationToken ct = default);

    /// <summary>Modo bloqueante (IMP-68): null = la auditoria al cerrar de esta HC es informativa
    /// (no aplica, su tipo de servicio no es bloqueante, o el usuario esta exento con el permiso
    /// "historias.sin-control-cierre"). Si no es null, un rechazo del agente impide cerrar.</summary>
    Task<BloqueoIaEstado?> EstadoBloqueoIaAsync(Guid historiaClinicaId, Guid actor, CancellationToken ct = default);

    /// <summary>Agotados los intentos, el profesional pide en /autorizaciones permiso para cerrar la
    /// HC pese al rechazo del auditor IA. Devuelve (ok, mensaje): mensaje informativo si ok.</summary>
    Task<(bool Ok, string? Mensaje)> SolicitarAutorizacionCierreIaAsync(
        Guid historiaClinicaId, string motivo, string? conceptoNombre, string? actorNombre, Guid actor, CancellationToken ct = default);

    /// <summary>Tras cerrar la HC: abre/reusa su ciclo de revision y registra el MISMO resultado
    /// auditado (veredicto + log + sugerencias + adopcion automatica), sin llamar al proveedor.</summary>
    Task<PreRevisionIaResult> RegistrarAuditoriaCierreAsync(
        Guid historiaClinicaId, AuditoriaIaResultado auditoria, bool cerradaPeseARechazo, Guid actor, CancellationToken ct = default);

    /// <summary>Guarda el log de la corrida apenas termina la auditoria al cerrar (sin ciclo de
    /// revision todavia), para que el usuario pueda calificarla aunque decida corregir. Devuelve el
    /// Id del log o null si no aplica / no se pudo.</summary>
    Task<Guid?> GuardarLogIntentoCierreAsync(AuditoriaIaResultado auditoria, CancellationToken ct = default);

    /// <summary>Calificacion del usuario sobre una corrida del agente (pulgar arriba/abajo + que
    /// salio mal). Queda en el log de esa corrida.</summary>
    Task<bool> RegistrarFeedbackAsync(Guid logId, bool util, string? comentario, Guid actor, CancellationToken ct = default);

    /// <summary>Intento de cierre que el profesional decidio corregir: solo queda en auditoria (metricas).</summary>
    Task RegistrarIntentoCierreAsync(Guid historiaClinicaId, AuditoriaIaResultado auditoria, Guid actor, CancellationToken ct = default);
}

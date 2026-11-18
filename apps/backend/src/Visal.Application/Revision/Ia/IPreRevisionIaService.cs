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

    public static AuditoriaIaResultado Fallo(Guid hcId, string error, int inTok = 0, int outTok = 0, AuditoriaIaTraza? traza = null)
        => new(false, error, null, 0m, null, Array.Empty<string>(), Array.Empty<SugerenciaTexto>(), inTok, outTok)
        {
            HistoriaClinicaId = hcId,
            Traza = traza,
        };
}

/// <summary>Configuracion efectiva de la auditoria al cerrar para una HC (null = no aplica).</summary>
public sealed record AuditoriaCierreConfig(int TimeoutSeg);

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

    /// <summary>Tras cerrar la HC: abre/reusa su ciclo de revision y registra el MISMO resultado
    /// auditado (veredicto + log + sugerencias + adopcion automatica), sin llamar al proveedor.</summary>
    Task<PreRevisionIaResult> RegistrarAuditoriaCierreAsync(
        Guid historiaClinicaId, AuditoriaIaResultado auditoria, bool cerradaPeseARechazo, Guid actor, CancellationToken ct = default);

    /// <summary>Intento de cierre que el profesional decidio corregir: solo queda en auditoria (metricas).</summary>
    Task RegistrarIntentoCierreAsync(Guid historiaClinicaId, AuditoriaIaResultado auditoria, Guid actor, CancellationToken ct = default);
}

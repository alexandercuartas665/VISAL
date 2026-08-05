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
    int OutputTokens);

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
}

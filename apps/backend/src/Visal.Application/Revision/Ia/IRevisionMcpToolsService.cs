namespace Visal.Application.Revision.Ia;

/// <summary>
/// Catalogo de tools MCP que el agente de revision puede invocar. Cada tool es
/// una funcion de solo lectura que devuelve una porcion del contexto clinico
/// (HC, paciente, ordenes, notas, etc.) filtrada por el tenant activo.
///
/// El orquestador consulta el allow-list del agente antes de llamar cada tool;
/// las que no esten en la lista devuelven un error. El resultado se serializa
/// como JSON y se concatena en el prompt que va al AI Provider.
///
/// En esta primera version NO hay tool-calling nativo (el agente no puede
/// pedir tools de forma iterativa). Los tools se ejecutan up-front: el
/// orquestador arma el paquete completo del contexto antes de invocar al LLM.
/// Backlog: cablear tool-calling nativo cuando el `IAiProviderClient` lo soporte.
/// </summary>
public static class RevisionMcpToolNames
{
    public const string GetHistoriaClinica = "get_historia_clinica";
    public const string GetPaciente = "get_paciente";
    public const string ListOrdenesHc = "list_ordenes_hc";
    public const string ListNotasHc = "list_notas_hc";
    public const string ListEscalasHc = "list_escalas_hc";
    public const string ListEvolucionesHc = "list_evoluciones_hc";
    public const string ListConsentimientosHc = "list_consentimientos_hc";
    public const string ListAsignacionesRelacionadas = "list_asignaciones_relacionadas";
    public const string GetFormDefinition = "get_form_definition";

    /// <summary>Todas las tools del catalogo. Utilizada por defecto en el seeder del agente.</summary>
    public static readonly IReadOnlyList<string> Todas = new[]
    {
        GetHistoriaClinica, GetPaciente, ListOrdenesHc, ListNotasHc,
        ListEscalasHc, ListEvolucionesHc, ListConsentimientosHc,
        ListAsignacionesRelacionadas, GetFormDefinition,
    };
}

/// <summary>Resultado de invocar un tool: JSON serializado + estado + tokens estimados.</summary>
public sealed record ToolInvocationResult(
    string ToolName,
    bool Ok,
    string? JsonPayload,
    string? Error,
    int EstimatedTokens);

/// <summary>Contexto compacto de la HC que se envia al LLM.</summary>
public sealed record HistoriaClinicaContextoIa(
    Guid HistoriaClinicaId,
    IReadOnlyList<ToolInvocationResult> InvocacionesExitosas,
    IReadOnlyList<ToolInvocationResult> InvocacionesFallidas);

/// <summary>
/// Servicio de tools MCP para el agente REVISOR CLINICO IA. Aplica allow-list
/// y auditoria de invocacion. Todas las tools respetan el global tenant filter.
/// </summary>
public interface IRevisionMcpToolsService
{
    /// <summary>Ejecuta el conjunto <paramref name="toolNames"/> filtrando por <paramref name="allowedTools"/>.
    /// Los nombres no permitidos devuelven un <see cref="ToolInvocationResult"/> con Ok=false.</summary>
    Task<HistoriaClinicaContextoIa> ArmarContextoAsync(
        Guid historiaClinicaId,
        IReadOnlyCollection<string> toolNames,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default);

    /// <summary>Ejecuta una tool suelta (util para tests). Aplica el mismo allow-list.</summary>
    Task<ToolInvocationResult> EjecutarToolAsync(
        string toolName,
        Guid historiaClinicaId,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default);
}

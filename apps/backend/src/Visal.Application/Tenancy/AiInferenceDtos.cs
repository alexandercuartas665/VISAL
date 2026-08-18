using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Un turno de la conversacion de prueba. Role: "user" (cliente) o "model" (agente).</summary>
public sealed record AiChatTurn(string Role, string Text);

/// <summary>Recurso que el agente decidio entregar en el chat (imagen, video, pdf, ubicacion o texto).</summary>
public sealed record AiChatAttachment(string Name, AgentResourceType ResourceType, string? FileUrl, string? FileName, string? Detail);

/// <summary>Resultado de una llamada de inferencia, con el consumo de tokens y los recursos a adjuntar.</summary>
public sealed record AiChatResult(bool Ok, string? Text, string? Error, int InputTokens = 0, int OutputTokens = 0,
    IReadOnlyList<AiChatAttachment>? Attachments = null);

/// <summary>Modelos disponibles reportados por el proveedor (consulta a su endpoint /models).</summary>
public sealed record AiModelsResult(bool Ok, IReadOnlyList<string> Models, string? Error);

/// <summary>
/// Cliente HTTP que habla con cada proveedor de IA (Gemini, OpenAI/ChatGPT, DeepSeek, Claude).
/// Recibe la API key ya descifrada; no persiste ni loggea secretos.
/// </summary>
public interface IAiProviderClient
{
    Task<AiChatResult> CompleteAsync(
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        string model,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta al proveedor los modelos disponibles para esta cuenta (endpoint /models). La key
    /// llega descifrada; no se persiste ni se loggea. Devuelve la lista ordenada o el error del proveedor.
    /// </summary>
    Task<AiModelsResult> ListModelsAsync(
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken = default);
}

/// <summary>Inferencia de agentes del tenant: arma el prompt con la config del agente y llama al proveedor.</summary>
public interface IAiInferenceService
{
    /// <summary>
    /// Ejecuta una conversacion de prueba contra el agente indicado. Usa la API key/proveedor/modelo
    /// configurados por la plataforma. systemPromptOverride permite probar un prompt aun sin guardar.
    /// </summary>
    Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta el agente indicado igual que <see cref="TestChatAsync"/> pero permite etiquetar el
    /// origen del consumo de tokens (ej. "email-pqr" para la ingesta de correos). Todo consumo se
    /// registra en el modulo de tokens con ese <paramref name="source"/>.
    /// </summary>
    Task<AiChatResult> RunAgentAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride, string source, CancellationToken cancellationToken = default);
}

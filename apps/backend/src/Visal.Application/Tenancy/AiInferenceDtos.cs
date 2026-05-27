using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Un turno de la conversacion de prueba. Role: "user" (cliente) o "model" (agente).</summary>
public sealed record AiChatTurn(string Role, string Text);

/// <summary>Recurso que el agente decidio entregar en el chat (imagen, video, pdf, ubicacion o texto).</summary>
public sealed record AiChatAttachment(string Name, AgentResourceType ResourceType, string? FileUrl, string? FileName, string? Detail);

/// <summary>Resultado de una llamada de inferencia, con el consumo de tokens y los recursos a adjuntar.</summary>
public sealed record AiChatResult(bool Ok, string? Text, string? Error, int InputTokens = 0, int OutputTokens = 0,
    IReadOnlyList<AiChatAttachment>? Attachments = null);

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
}

/// <summary>Inferencia de agentes del tenant: arma el prompt con la config del agente y llama al proveedor.</summary>
public interface IAiInferenceService
{
    /// <summary>
    /// Ejecuta una conversacion de prueba contra el agente indicado. Usa la API key/proveedor/modelo
    /// configurados por la plataforma. systemPromptOverride permite probar un prompt aun sin guardar.
    /// </summary>
    Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default);
}

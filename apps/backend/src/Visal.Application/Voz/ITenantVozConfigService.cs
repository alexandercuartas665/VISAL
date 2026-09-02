namespace Visal.Application.Voz;

/// <summary>Config de voz del tenant para la UI. NUNCA expone la API key en claro;
/// solo indica si esta cargada (<paramref name="TieneApiKey"/>).</summary>
public sealed record VozConfigDto(
    string? AgentId, string? FromNumber, string? TelnyxSipUsername,
    string? WebhookToken, bool TieneApiKey, bool Activa);

/// <summary>Payload para guardar. <paramref name="ApiKey"/> null/vacio = conservar la
/// existente (no se sobreescribe con vacio).</summary>
public sealed record VozConfigSaveRequest(
    string? ApiKey, string? AgentId, string? FromNumber, string? TelnyxSipUsername, bool Activa);

/// <summary>CRUD de la configuracion de voz IA por agencia. La API key se cifra con
/// ISecretProtector antes de persistir.</summary>
public interface ITenantVozConfigService
{
    /// <summary>Config del tenant activo (enmascarada). Si no existe, devuelve valores vacios.</summary>
    Task<VozConfigDto> GetAsync(CancellationToken ct = default);

    /// <summary>Crea o actualiza la config del tenant. Cifra la API key si viene. Genera
    /// el WebhookToken si aun no existe. Devuelve la config resultante (enmascarada).</summary>
    Task<VozConfigDto> SaveAsync(VozConfigSaveRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Regenera el token del webhook (invalida el anterior). Devuelve el nuevo token.</summary>
    Task<string> RegenerarTokenAsync(Guid actor, CancellationToken ct = default);
}

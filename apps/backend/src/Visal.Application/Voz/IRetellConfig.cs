namespace Visal.Application.Voz;

/// <summary>
/// Configuracion de Retell/Telnyx del tenant activo (guardada cifrada en BD por
/// agencia). Los valores se cargan de forma diferida con
/// <see cref="EnsureLoadedAsync"/>; llamalo antes de leer las propiedades.
/// </summary>
public interface IRetellConfig
{
    /// <summary>Carga (una vez por scope) la config del tenant activo desde BD.</summary>
    Task EnsureLoadedAsync(CancellationToken ct = default);

    string? ApiKey { get; }
    string? AgentId { get; }
    string? FromNumber { get; }
    /// <summary>Token opaco que Retell debe incluir en la ruta del webhook: /webhooks/retell/{token}.</summary>
    string? WebhookToken { get; }
    /// <summary>Username del trunk Telnyx; si esta, se envia como header X-Telnyx-Username en custom_sip_headers.</summary>
    string? TelnyxSipUsername { get; }

    /// <summary>True si estan los minimos para poder crear llamadas (tras EnsureLoadedAsync).</summary>
    bool EstaConfigurado { get; }
}

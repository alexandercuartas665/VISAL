using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion del webhook publico de formularios web (WordPress) por agencia (tenant).
/// GLOBAL (lleva TenantId pero no es ITenantScoped) para poder resolver el tenant a partir del
/// token que viene EN LA URL del webhook, sin contexto de sesion. El token se guarda hasheado
/// (para buscar) y cifrado (para mostrarlo en Mi cuenta). Es un secreto distinto del API key de
/// leads (TenantApiConfig): rota independiente y no se entrega por header. Espeja el patron de
/// TenantApiConfig y WhatsAppLine.InboundToken. Ver ADR docs/decisiones/0009.
/// </summary>
public class TenantFormWebhookConfig : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 (hex) del token, para resolver el tenant en cada webhook.</summary>
    public string? TokenHash { get; set; }

    /// <summary>Token cifrado (ISecretProtector), para mostrarlo al dueno en Mi cuenta.</summary>
    public string? TokenEncrypted { get; set; }

    /// <summary>Si el webhook de formularios esta activo para este tenant.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion de voz IA (Retell/Telnyx) por agencia (tenant). Una fila por
/// tenant. La API key se guarda CIFRADA (ISecretProtector); nunca en claro ni en
/// logs. El WebhookToken (opaco, unico) identifica al tenant en la ruta del
/// webhook /webhooks/retell/{token}.
/// </summary>
public class TenantRetellConfig : TenantEntity
{
    /// <summary>API key de Retell cifrada. Null si aun no se cargo.</summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Id del agente de voz de Retell.</summary>
    public string? AgentId { get; set; }

    /// <summary>Numero saliente (Telnyx importado en Retell) en E.164.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Token opaco unico para la ruta del webhook de esta agencia.</summary>
    public string? WebhookToken { get; set; }

    /// <summary>Username del trunk Telnyx (opcional); viaja como X-Telnyx-Username.</summary>
    public string? TelnyxSipUsername { get; set; }

    /// <summary>Si esta activa se permiten las llamadas; si no, el modulo queda deshabilitado.</summary>
    public bool Activa { get; set; } = true;
}

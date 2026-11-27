using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// IMP-72: llave de SOLO LECTURA de la API de la bitacora del auditor IA (revision_agente_logs).
/// GLOBAL (lleva TenantId pero no es ITenantScoped) para resolver el tenant a partir de la llave
/// sin contexto de sesion, igual que TenantApiConfig. Es una llave distinta a la de leads: rota y
/// se apaga por separado. La llave se guarda como hash (para buscar) y cifrada (para mostrarla).
/// </summary>
public class BitacoraIaApiConfig : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 (hex) de la llave.</summary>
    public string? ApiKeyHash { get; set; }

    /// <summary>Llave cifrada (ISecretProtector), para mostrarla al admin en el modulo.</summary>
    public string? ApiKeyEncrypted { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

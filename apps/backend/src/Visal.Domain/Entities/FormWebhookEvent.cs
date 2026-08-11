using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro de una recepcion del webhook de formularios web, para idempotencia. GLOBAL (lleva
/// TenantId pero no es ITenantScoped: el webhook es la frontera de confianza y opera sin sesion).
/// Dedup por (TenantId + DedupHash) dentro de una ventana de tiempo corta: un reenvio de Elementor
/// con el mismo payload no crea una tarjeta doble. Espeja WompiWebhookEvent.
/// </summary>
public class FormWebhookEvent : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>SHA-256 (hex) del payload normalizado (tenant + campos mapeados).</summary>
    public string DedupHash { get; set; } = null!;

    /// <summary>Tarjeta (Lead) creada por esta recepcion. Null si no llego a crearse.</summary>
    public Guid? LeadId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

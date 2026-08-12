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

    /// <summary>
    /// Tarjeta del embudo (Lead) creada por esta recepcion. Historico: hoy el webhook crea tarjetas
    /// en el modulo de Tableros (ver <see cref="TaskCardId"/>). Se conserva para dedup de filas viejas.
    /// </summary>
    public Guid? LeadId { get; set; }

    /// <summary>Tarjeta del modulo Tableros (TaskCard) creada por esta recepcion. Null si no llego a crearse.</summary>
    public Guid? TaskCardId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

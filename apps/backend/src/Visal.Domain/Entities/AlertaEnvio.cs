using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Bitacora / outbox de una alerta enviada (o intentada) por una regla sobre una
/// asignacion en un periodo. Da idempotencia: el worker no vuelve a enviar la
/// misma (Regla, Asignacion, Periodo) si ya se envio con exito. Un envio fallido
/// se conserva con Exito=false y puede reintentarse en la siguiente corrida.
/// Tenant-scoped. Indice unico (TenantId, ReglaId, AsignacionId, Periodo).
/// </summary>
public class AlertaEnvio : TenantEntity
{
    public Guid ReglaId { get; set; }
    public Guid AsignacionId { get; set; }
    public Guid PacienteId { get; set; }

    /// <summary>Clave de periodo para dedup (ej. "2026-09"): mes calendario para
    /// disparos por dia del mes; mes objetivo (ancla + N meses) para disparos relativos.</summary>
    public string Periodo { get; set; } = null!;

    public AlertaCanal Canal { get; set; }
    public AlertaDestinatario Destinatario { get; set; }

    /// <summary>Correo o telefono efectivamente usado (para auditoria/soporte).</summary>
    public string? Contacto { get; set; }

    public DateTimeOffset FechaEnvio { get; set; }
    public bool Exito { get; set; }
    public string? Error { get; set; }

    /// <summary>Estado de gestion de la tarjeta en la bandeja de Alertas.</summary>
    public AlertaGestion EstadoGestion { get; set; } = AlertaGestion.Nueva;

    /// <summary>Id externo del proveedor (messageId de Gupshup) cuando aplica.</summary>
    public string? ExternalId { get; set; }
}

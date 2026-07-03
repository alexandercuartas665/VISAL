using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Linea/instancia WhatsApp de un tenant (modulo 1.4). Entidad TENANT-SCOPED. La conexion
/// real (QR, sesion) se gestionara mediante el Evolution Connector en una fase posterior;
/// aqui se modela el ciclo de vida, el estado y la asignacion operativa a un asesor.
///
/// Multi-proveedor: cada linea corre contra un IWhatsAppProvider concreto
/// (Evolution o Gupshup). El campo Provider define cual. Ver
/// IWhatsAppProviderResolver.
/// </summary>
public class WhatsAppLine : TenantEntity
{
    public string InstanceName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public WhatsAppLineStatus Status { get; set; } = WhatsAppLineStatus.Created;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastStatusAt { get; set; }

    // === Multi-proveedor ===

    /// <summary>Proveedor concreto detras de esta linea. Default Evolution
    /// para lineas historicas. Cada envio consulta este valor para elegir
    /// cliente HTTP.</summary>
    public WhatsAppProvider Provider { get; set; } = WhatsAppProvider.Evolution;

    /// <summary>Si Provider = Gupshup, apunta a la fila TenantGupshupConfig
    /// que trae credenciales de la App. Null para lineas Evolution.</summary>
    public Guid? GupshupAppId { get; set; }

    // === Webhook entrante ===

    /// <summary>
    /// Token opaco unico por linea usado como path segment del webhook de
    /// mensajes entrantes: /webhooks/gupshup/{InboundToken}. Sirve como
    /// identificador + secreto: al llegar un POST el sistema busca la
    /// linea por este token y usa su TenantId. Regenerable si el token se
    /// filtra (rota sin tocar la App de Gupshup, solo el path). No se
    /// muestra en logs.
    /// </summary>
    public string? InboundToken { get; set; }
}

namespace Visal.Application.Tenancy.Alertas;

/// <summary>
/// Responde automaticamente con el enlace del informe de terapias pendientes cuando un
/// profesional (o destinatario de una alerta por WhatsApp) contesta afirmativamente
/// ("si, enviar enlace"). Solo aplica si ese telefono recibio una alerta reciente por
/// WhatsApp del tenant. Se envia como mensaje de sesion (dentro de la ventana de 24h que
/// abre la respuesta del profesional).
/// </summary>
public interface IAlertaInformeAutoResponder
{
    /// <summary>Devuelve 1 si respondio con el enlace del informe; 0 si no aplica.</summary>
    Task<int> ResponderInformeSiAplicaAsync(Guid tenantId, string contactPhone, Guid lineId, string baseUri, CancellationToken ct = default);
}

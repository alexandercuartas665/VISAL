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
    /// <param name="exigirAlertaPrevia">Si es true (defecto), solo responde a numeros que
    /// recibieron una alerta por WhatsApp reciente. Si es false, responde igual — pensado
    /// para clics de BOTON de nuestras plantillas (un boton solo lo toca quien recibio la
    /// plantilla), de modo que funciona aunque el envio no haya quedado en el outbox (ej.
    /// "Enviar test" o emision con telefono de prueba). El informe se acota al doctor por
    /// su celular cuando hay coincidencia; si no, queda tenant-wide.</param>
    Task<int> ResponderInformeSiAplicaAsync(Guid tenantId, string contactPhone, Guid lineId, string baseUri, bool exigirAlertaPrevia = true, CancellationToken ct = default);
}

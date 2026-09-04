using Visal.Application.Common;

namespace Visal.Application.Tenancy.Email;

/// <summary>
/// Envia correos de notificacion (ej. alertas) usando la cuenta de correo del PQR
/// de la agencia (TenantEmailIngestConfig: Gmail + App Password ya configurada para
/// leer PQR). Reusa el transporte SMTP de MailKit del PQR. Asi el correo sale del
/// buzon propio de la agencia sin pedir credenciales nuevas.
/// </summary>
public interface INotificacionEmailSender
{
    /// <summary>True si la agencia tiene al menos una cuenta de correo (PQR) con App
    /// Password para poder enviar notificaciones.</summary>
    Task<bool> TieneCuentaAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Envia un correo (texto) desde la cuenta de correo de la agencia. El
    /// cuerpo va como texto plano (los enlaces quedan clicables en el cliente).</summary>
    Task<EmailSendResult> SendAsync(Guid tenantId, string toEmail, string subject, string bodyText, CancellationToken ct = default);
}

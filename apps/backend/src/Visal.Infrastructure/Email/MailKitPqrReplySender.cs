using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Visal.Application.Tenancy.Email;

namespace Visal.Infrastructure.Email;

/// <summary>
/// Envia respuestas de PQR por SMTP con MailKit usando la credencial del MISMO buzon (Gmail App
/// Password). Enhebra con In-Reply-To/References sobre el Message-ID original. No persiste ni
/// loggea la clave. Nunca lanza: devuelve (Ok, Error).
/// </summary>
public sealed class MailKitPqrReplySender : IPqrEmailReplySender
{
    public async Task<(bool Ok, string? Error)> SendAsync(SmtpReplyParams p, CancellationToken ct = default)
    {
        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(p.FromName) ? p.FromEmail : p.FromName, p.FromEmail));
            msg.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(p.ToName) ? p.ToEmail : p.ToName, p.ToEmail));
            msg.Subject = p.Subject;

            // Enhebrado: In-Reply-To + References apuntan al Message-ID original.
            if (!string.IsNullOrWhiteSpace(p.InReplyToMessageId))
            {
                var mid = p.InReplyToMessageId.Trim();
                if (!mid.StartsWith('<')) { mid = "<" + mid + ">"; }
                msg.InReplyTo = mid;
                msg.References.Add(mid);
            }

            var body = new BodyBuilder { TextBody = p.BodyText };
            foreach (var a in p.Attachments ?? Array.Empty<PqrReplyAttachment>())
            {
                if (a.Bytes is null || a.Bytes.Length == 0) { continue; }
                var mime = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType;
                body.Attachments.Add(a.FileName, a.Bytes, ContentType.Parse(mime));
            }
            msg.Body = body.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(p.Host, p.Port,
                p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(p.Username, p.Password, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
            return (true, null);
        }
        catch (AuthenticationException)
        {
            return (false, "No se pudo autenticar en el servidor SMTP. Verifica la App Password del buzon.");
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo enviar el correo: {ex.Message}");
        }
    }
}

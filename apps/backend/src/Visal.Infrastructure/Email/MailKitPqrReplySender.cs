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
            var from = new MailboxAddress(string.IsNullOrWhiteSpace(p.FromName) ? p.FromEmail : p.FromName.Trim(), p.FromEmail);
            msg.From.Add(from);
            msg.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(p.ToName) ? p.ToEmail : p.ToName, p.ToEmail));
            // Reply-To = el mismo buzon: las respuestas del destinatario llegan a la
            // cuenta que envia (y ayuda al puntaje de entregabilidad).
            msg.ReplyTo.Add(from);
            msg.Subject = p.Subject;

            // Cabecera List-Unsubscribe (correo automatizado): Gmail/Outlook la premian.
            if (!string.IsNullOrWhiteSpace(p.ListUnsubscribe))
            {
                msg.Headers.Add("List-Unsubscribe", p.ListUnsubscribe.Trim());
            }

            // Enhebrado: In-Reply-To + References apuntan al Message-ID original.
            if (!string.IsNullOrWhiteSpace(p.InReplyToMessageId))
            {
                var mid = p.InReplyToMessageId.Trim();
                if (!mid.StartsWith('<')) { mid = "<" + mid + ">"; }
                msg.InReplyTo = mid;
                msg.References.Add(mid);
            }

            // Enviar SIEMPRE multipart texto+HTML: un correo con parte HTML bien
            // formada puntua mejor que uno de solo texto plano. Si el caller no da
            // HTML, se genera una version simple a partir del texto.
            var body = new BodyBuilder
            {
                TextBody = p.BodyText,
                HtmlBody = string.IsNullOrWhiteSpace(p.BodyHtml) ? TextoAHtml(p.BodyText) : p.BodyHtml
            };
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

    /// <summary>Genera una parte HTML simple a partir del texto plano: escapa HTML y
    /// convierte saltos de linea en &lt;br&gt;, envuelto en un contenedor legible. No
    /// interpreta markdown; solo asegura que el correo salga multipart texto+HTML.</summary>
    private static string TextoAHtml(string? texto)
    {
        var contenido = System.Net.WebUtility.HtmlEncode(texto ?? "")
            .Replace("\r\n", "\n").Replace("\r", "\n")
            .Replace("\n", "<br>");
        return "<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;"
             + "line-height:1.5;color:#1f2937;\">" + contenido + "</div>";
    }
}

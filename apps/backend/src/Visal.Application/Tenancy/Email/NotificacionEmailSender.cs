using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Email;

public sealed class NotificacionEmailSender : INotificacionEmailSender
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secret;
    private readonly IPqrEmailReplySender _sender;

    public NotificacionEmailSender(IApplicationDbContext db, ISecretProtector secret, IPqrEmailReplySender sender)
    {
        _db = db;
        _secret = secret;
        _sender = sender;
    }

    public async Task<bool> TieneCuentaAsync(Guid tenantId, CancellationToken ct = default)
        => await ResolverCuentaQuery(tenantId).AnyAsync(ct);

    public async Task<EmailSendResult> SendAsync(Guid tenantId, string toEmail, string subject, string bodyText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) { return new EmailSendResult(false, "Destinatario de correo vacio."); }

        var cfg = await ResolverCuentaQuery(tenantId).FirstOrDefaultAsync(ct);
        if (cfg is null)
        {
            return new EmailSendResult(false, "La agencia no tiene una cuenta de correo (PQR) con App Password para enviar notificaciones.");
        }

        string password;
        try { password = _secret.Unprotect(cfg.AppPasswordEncrypted!); }
        catch { return new EmailSendResult(false, "La App Password del buzon esta cifrada con una version anterior. Vuelve a guardarla."); }

        var (host, port, useSsl) = SmtpDe(cfg);
        var pars = new SmtpReplyParams(
            host, port, useSsl,
            cfg.EmailAddress, password,
            cfg.EmailAddress, cfg.Nombre,
            toEmail, null,
            string.IsNullOrWhiteSpace(subject) ? "Notificacion VISAL" : subject,
            bodyText ?? "",
            null,
            Array.Empty<PqrReplyAttachment>());

        var (ok, error) = await _sender.SendAsync(pars, ct);
        return new EmailSendResult(ok, error);
    }

    /// <summary>Cuentas del tenant con App Password, priorizando la habilitada.</summary>
    private IQueryable<TenantEmailIngestConfig> ResolverCuentaQuery(Guid tenantId)
        => _db.TenantEmailIngestConfigs.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                        && c.AppPasswordEncrypted != null && c.AppPasswordEncrypted != "")
            .OrderByDescending(c => c.IsEnabled)
            .ThenBy(c => c.Nombre);

    /// <summary>Deriva el SMTP de envio desde el host IMAP del buzon (imap.gmail.com ->
    /// smtp.gmail.com). Gmail usa 587 + STARTTLS con App Password.</summary>
    private static (string Host, int Port, bool UseSsl) SmtpDe(TenantEmailIngestConfig cfg)
    {
        var imap = (cfg.ImapHost ?? "").Trim();
        var host = imap.StartsWith("imap.", StringComparison.OrdinalIgnoreCase)
            ? "smtp." + imap["imap.".Length..]
            : (imap.Length == 0 ? "smtp.gmail.com" : imap.Replace("imap", "smtp", StringComparison.OrdinalIgnoreCase));
        return (host, 587, false);
    }
}

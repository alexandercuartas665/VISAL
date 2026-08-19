using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Email;

public sealed class PqrEmailReplyService : IPqrEmailReplyService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secret;
    private readonly IPqrEmailReplySender _sender;
    private readonly ITenantContext _tenant;

    public PqrEmailReplyService(IApplicationDbContext db, ISecretProtector secret,
        IPqrEmailReplySender sender, ITenantContext tenant)
    {
        _db = db;
        _secret = secret;
        _sender = sender;
        _tenant = tenant;
    }

    public async Task<PqrReplyContextDto> GetReplyContextAsync(Guid cardId, CancellationToken ct = default)
    {
        var log = await UltimoLogAsync(cardId, ct);
        if (log is null)
        {
            return new PqrReplyContextDto(cardId, false,
                "Esta tarjeta no tiene un correo asociado para responder.", null, null, "", null, null, null);
        }
        if (string.IsNullOrWhiteSpace(log.FromAddress))
        {
            return new PqrReplyContextDto(cardId, false,
                "El correo original no registro un remitente valido.", null, log.FromName, "", null, log.ReceivedAt, log.Subject);
        }

        var cfg = await _db.TenantEmailIngestConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == log.ConfigId, ct);
        if (cfg is null || string.IsNullOrEmpty(cfg.AppPasswordEncrypted))
        {
            return new PqrReplyContextDto(cardId, false,
                "El buzon del correo no tiene credencial para enviar respuestas.", log.FromAddress, log.FromName,
                Reasunto(log.Subject), cfg?.EmailAddress, log.ReceivedAt, log.Subject);
        }

        return new PqrReplyContextDto(cardId, true, null, log.FromAddress, log.FromName,
            Reasunto(log.Subject), cfg.EmailAddress, log.ReceivedAt, log.Subject);
    }

    public async Task<(bool Ok, string? Error)> ResponderAsync(
        Guid cardId, string asunto, string cuerpo, IReadOnlyList<PqrReplyAttachment> adjuntos,
        Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cuerpo)) { return (false, "El mensaje esta vacio."); }

        var log = await UltimoLogAsync(cardId, ct);
        if (log is null || string.IsNullOrWhiteSpace(log.FromAddress))
        {
            return (false, "No se encontro el correo original para responder.");
        }

        var cfg = await _db.TenantEmailIngestConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == log.ConfigId, ct);
        if (cfg is null || string.IsNullOrEmpty(cfg.AppPasswordEncrypted))
        {
            return (false, "El buzon del correo no tiene credencial para enviar respuestas.");
        }

        string password;
        try { password = _secret.Unprotect(cfg.AppPasswordEncrypted); }
        catch { return (false, "La App Password del buzon esta cifrada con una version anterior. Vuelve a guardarla."); }

        var (host, port, useSsl) = SmtpDe(cfg);
        var subject = string.IsNullOrWhiteSpace(asunto) ? Reasunto(log.Subject) : asunto.Trim();

        var pars = new SmtpReplyParams(
            host, port, useSsl,
            cfg.EmailAddress, password,
            cfg.EmailAddress, cfg.Nombre,
            log.FromAddress, log.FromName,
            subject, cuerpo,
            log.MessageId,
            adjuntos ?? Array.Empty<PqrReplyAttachment>());

        var (ok, error) = await _sender.SendAsync(pars, ct);
        if (!ok) { return (false, error ?? "No se pudo enviar el correo."); }

        // Registrar en la tarjeta: una accion + un comentario con el texto enviado.
        var adjTxt = (adjuntos is { Count: > 0 }) ? $" (con {adjuntos.Count} adjunto(s))" : "";
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TaskCardId = cardId,
            Type = TaskActivityType.Action,
            ActorUserId = actor == Guid.Empty ? null : actor,
            ActorName = actorDisplayName,
            Text = $"respondio el correo a {log.FromAddress}{adjTxt}",
        });
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TaskCardId = cardId,
            Type = TaskActivityType.Comment,
            ActorUserId = actor == Guid.Empty ? null : actor,
            ActorName = actorDisplayName,
            Text = $"✉ Respuesta enviada — Asunto: {subject}\n\n{cuerpo}",
        });
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    // ---- Plantillas ----

    public async Task<IReadOnlyList<PqrPlantillaDto>> ListPlantillasAsync(CancellationToken ct = default)
    {
        return await _db.PqrRespuestaPlantillas.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Nombre)
            .Select(p => new PqrPlantillaDto(p.Id, p.Nombre, p.Asunto, p.Cuerpo))
            .ToListAsync(ct);
    }

    public async Task<Guid> GuardarPlantillaAsync(SavePqrPlantillaRequest req, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (string.IsNullOrWhiteSpace(req.Nombre)) { throw new InvalidOperationException("El nombre es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.Cuerpo)) { throw new InvalidOperationException("El cuerpo es obligatorio."); }

        PqrRespuestaPlantilla p;
        if (req.Id is { } id && id != Guid.Empty)
        {
            p = await _db.PqrRespuestaPlantillas.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("La plantilla no existe.");
        }
        else
        {
            p = new PqrRespuestaPlantilla { TenantId = tid };
            var maxOrder = await _db.PqrRespuestaPlantillas.Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1;
            p.SortOrder = maxOrder + 1;
            _db.PqrRespuestaPlantillas.Add(p);
        }
        p.Nombre = req.Nombre.Trim();
        p.Asunto = string.IsNullOrWhiteSpace(req.Asunto) ? null : req.Asunto.Trim();
        p.Cuerpo = req.Cuerpo;
        p.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task EliminarPlantillaAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.PqrRespuestaPlantillas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) { return; }
        _db.PqrRespuestaPlantillas.Remove(p);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Helpers ----

    private Task<EmailIngestLog?> UltimoLogAsync(Guid cardId, CancellationToken ct)
        => _db.EmailIngestLogs.AsNoTracking()
            .Where(l => l.TaskCardId == cardId && l.MessageId != null)
            .OrderByDescending(l => l.ReceivedAt).ThenByDescending(l => l.ProcessedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>Antepone "Re: " al asunto original evitando duplicarlo.</summary>
    private static string Reasunto(string? original)
    {
        var s = (original ?? "").Trim();
        if (s.Length == 0) { return "Re: (sin asunto)"; }
        return s.StartsWith("re:", StringComparison.OrdinalIgnoreCase) ? s : $"Re: {s}";
    }

    /// <summary>Deriva host/puerto SMTP a partir del host IMAP del buzon (imap.gmail.com -> smtp.gmail.com).</summary>
    private static (string Host, int Port, bool UseSsl) SmtpDe(TenantEmailIngestConfig cfg)
    {
        var imap = (cfg.ImapHost ?? "").Trim();
        var host = imap.StartsWith("imap.", StringComparison.OrdinalIgnoreCase)
            ? "smtp." + imap["imap.".Length..]
            : (imap.Length == 0 ? "smtp.gmail.com" : imap.Replace("imap", "smtp", StringComparison.OrdinalIgnoreCase));
        // 587 con STARTTLS es el estandar de Gmail para envio con App Password.
        return (host, 587, false);
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Email;

public sealed class EmailIngestConfigService : IEmailIngestConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secret;
    private readonly IImapEmailReader _reader;
    private readonly IEmailIngestProcessor _processor;

    public EmailIngestConfigService(IApplicationDbContext db, ISecretProtector secret,
        IImapEmailReader reader, IEmailIngestProcessor processor)
    {
        _db = db;
        _secret = secret;
        _reader = reader;
        _processor = processor;
    }

    public async Task<IReadOnlyList<EmailIngestConfigDto>> ListAsync(CancellationToken ct = default)
    {
        var cfgs = await _db.TenantEmailIngestConfigs.AsNoTracking()
            .OrderBy(c => c.Nombre).ToListAsync(ct);
        if (cfgs.Count == 0) { return Array.Empty<EmailIngestConfigDto>(); }

        var agentIds = cfgs.Where(c => c.ClassifierAgentId is not null).Select(c => c.ClassifierAgentId!.Value).Distinct().ToList();
        var boardIds = cfgs.Where(c => c.TargetBoardId is not null).Select(c => c.TargetBoardId!.Value).Distinct().ToList();

        var agentes = await _db.AiAgents.AsNoTracking().Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var tableros = await _db.TaskBoards.AsNoTracking().Where(b => boardIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        return cfgs.Select(c => Map(c, agentes, tableros)).ToList();
    }

    public async Task<EmailIngestConfigDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.TenantEmailIngestConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) { return null; }
        var agentes = new Dictionary<Guid, string>();
        var tableros = new Dictionary<Guid, string>();
        if (c.ClassifierAgentId is not null)
        {
            var n = await _db.AiAgents.AsNoTracking().Where(a => a.Id == c.ClassifierAgentId).Select(a => a.Name).FirstOrDefaultAsync(ct);
            if (n is not null) { agentes[c.ClassifierAgentId.Value] = n; }
        }
        if (c.TargetBoardId is not null)
        {
            var n = await _db.TaskBoards.AsNoTracking().Where(b => b.Id == c.TargetBoardId).Select(b => b.Name).FirstOrDefaultAsync(ct);
            if (n is not null) { tableros[c.TargetBoardId.Value] = n; }
        }
        return Map(c, agentes, tableros);
    }

    public async Task<Guid> SaveAsync(SaveEmailIngestConfigRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Nombre)) { throw new InvalidOperationException("El nombre es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.EmailAddress)) { throw new InvalidOperationException("El correo es obligatorio."); }

        TenantEmailIngestConfig cfg;
        if (req.Id is { } id && id != Guid.Empty)
        {
            cfg = await _db.TenantEmailIngestConfigs.FirstOrDefaultAsync(c => c.Id == id, ct)
                  ?? throw new InvalidOperationException("El buzon no existe.");
        }
        else
        {
            cfg = new TenantEmailIngestConfig();
            _db.TenantEmailIngestConfigs.Add(cfg);
        }

        cfg.Nombre = req.Nombre.Trim();
        cfg.EmailAddress = req.EmailAddress.Trim();
        cfg.ImapHost = string.IsNullOrWhiteSpace(req.ImapHost) ? "imap.gmail.com" : req.ImapHost.Trim();
        cfg.ImapPort = req.ImapPort <= 0 ? 993 : req.ImapPort;
        cfg.ImapUseSsl = req.ImapUseSsl;
        cfg.Folder = string.IsNullOrWhiteSpace(req.Folder) ? "INBOX" : req.Folder.Trim();
        cfg.ClassifierAgentId = req.ClassifierAgentId;
        cfg.TargetBoardId = req.TargetBoardId;
        cfg.TargetColumnId = req.TargetColumnId;
        cfg.PollIntervalMinutes = Math.Clamp(req.PollIntervalMinutes, 1, 1440);
        cfg.LookbackDays = Math.Clamp(req.LookbackDays, 1, 90);
        cfg.MaxPorCorrida = Math.Clamp(req.MaxPorCorrida, 1, 200);
        cfg.OnlyUnread = req.OnlyUnread;
        cfg.MarkAsRead = req.MarkAsRead;
        cfg.ProcessedLabel = string.IsNullOrWhiteSpace(req.ProcessedLabel) ? null : req.ProcessedLabel.Trim();
        cfg.IsEnabled = req.IsEnabled;

        // App Password: solo se reescribe si viene una nueva (cifrada). Vacio = conservar.
        if (!string.IsNullOrWhiteSpace(req.AppPassword))
        {
            // Gmail muestra la App Password con espacios cada 4 chars; se guardan sin espacios.
            cfg.AppPasswordEncrypted = _secret.Protect(req.AppPassword.Replace(" ", "").Trim());
        }

        // No se puede habilitar sin credencial.
        if (cfg.IsEnabled && string.IsNullOrEmpty(cfg.AppPasswordEncrypted))
        {
            throw new InvalidOperationException("Para habilitar el buzon primero guarda la App Password.");
        }

        await _db.SaveChangesAsync(ct);
        return cfg.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var cfg = await _db.TenantEmailIngestConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cfg is null) { return; }
        var logs = await _db.EmailIngestLogs.Where(l => l.ConfigId == id).ToListAsync(ct);
        _db.EmailIngestLogs.RemoveRange(logs);
        _db.TenantEmailIngestConfigs.Remove(cfg);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(bool Ok, string? Error, int Total)> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var cfg = await _db.TenantEmailIngestConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cfg is null) { return (false, "El buzon no existe.", 0); }
        if (string.IsNullOrEmpty(cfg.AppPasswordEncrypted)) { return (false, "Falta la App Password.", 0); }

        string password;
        try { password = _secret.Unprotect(cfg.AppPasswordEncrypted); }
        catch { return (false, "La App Password esta cifrada con una version anterior. Vuelve a guardarla.", 0); }

        var pars = new ImapConnectionParams(cfg.ImapHost, cfg.ImapPort, cfg.ImapUseSsl, cfg.EmailAddress, password,
            string.IsNullOrWhiteSpace(cfg.Folder) ? "INBOX" : cfg.Folder, cfg.OnlyUnread, 1, null);
        return await _reader.TestConnectionAsync(pars, ct);
    }

    public async Task<(bool Ok, string? Error, IReadOnlyList<string> Labels)> ListarEtiquetasAsync(Guid id, CancellationToken ct = default)
    {
        var cfg = await _db.TenantEmailIngestConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cfg is null) { return (false, "El buzon no existe.", Array.Empty<string>()); }
        if (string.IsNullOrEmpty(cfg.AppPasswordEncrypted)) { return (false, "Primero guarda la App Password del buzon.", Array.Empty<string>()); }

        string password;
        try { password = _secret.Unprotect(cfg.AppPasswordEncrypted); }
        catch { return (false, "La App Password esta cifrada con una version anterior. Vuelve a guardarla.", Array.Empty<string>()); }

        var pars = new ImapConnectionParams(cfg.ImapHost, cfg.ImapPort, cfg.ImapUseSsl, cfg.EmailAddress, password,
            string.IsNullOrWhiteSpace(cfg.Folder) ? "INBOX" : cfg.Folder, cfg.OnlyUnread, 1, null);
        try
        {
            var labels = await _reader.ListLabelsAsync(pars, ct);
            return (true, null, labels);
        }
        catch (Exception ex)
        {
            return (false, $"No se pudieron leer las etiquetas: {ex.Message}", Array.Empty<string>());
        }
    }

    public Task<EmailIngestRunResult> ProcessNowAsync(Guid id, CancellationToken ct = default)
        => _processor.ProcessConfigAsync(id, ct);

    public async Task<IReadOnlyList<EmailIngestLogDto>> ListLogsAsync(Guid configId, int take = 100, CancellationToken ct = default)
    {
        return await _db.EmailIngestLogs.AsNoTracking()
            .Where(l => l.ConfigId == configId)
            .OrderByDescending(l => l.ProcessedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(l => new EmailIngestLogDto(l.Id, l.FromAddress, l.Subject, l.ReceivedAt, l.Resultado,
                l.TipoPqrs, l.TaskCardId, l.ErrorMessage, l.InputTokens, l.OutputTokens, l.AttachmentCount, l.ProcessedAt))
            .ToListAsync(ct);
    }

    private static EmailIngestConfigDto Map(TenantEmailIngestConfig c,
        IReadOnlyDictionary<Guid, string> agentes, IReadOnlyDictionary<Guid, string> tableros)
        => new(
            c.Id, c.Nombre, c.EmailAddress, c.ImapHost, c.ImapPort, c.ImapUseSsl, c.Folder,
            c.ClassifierAgentId,
            c.ClassifierAgentId is { } aid && agentes.TryGetValue(aid, out var an) ? an : null,
            c.TargetBoardId,
            c.TargetBoardId is { } bid && tableros.TryGetValue(bid, out var bn) ? bn : null,
            c.PollIntervalMinutes, c.LookbackDays, c.MaxPorCorrida, c.OnlyUnread, c.MarkAsRead, c.ProcessedLabel, c.IsEnabled,
            !string.IsNullOrEmpty(c.AppPasswordEncrypted),
            c.LastPolledAt, c.LastError, c.LastResultSummary);
}

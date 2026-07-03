using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>
/// CRUD tenant-scoped de TenantGupshupConfig + rotacion de InboundToken.
/// La apikey no se persiste en claro nunca: se cifra con Data Protection
/// antes de guardarse y se descifra al vuelo cuando el provider la
/// necesita para enviar (ver GupshupWhatsAppProvider).
/// </summary>
internal sealed class GupshupConfigService : IGupshupConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public GupshupConfigService(
        IApplicationDbContext db, ITenantContext tenantContext,
        ISecretProtector secretProtector, IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<GupshupConfigDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.TenantGupshupConfigs.AsNoTracking().OrderBy(c => c.AppName).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<GupshupConfigDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.TenantGupshupConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<GupshupConfigDto?> UpsertAsync(Guid? id, SaveGupshupConfigRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        TenantGupshupConfig? row = null;
        if (id is Guid gid && gid != Guid.Empty)
        {
            row = await _db.TenantGupshupConfigs.FirstOrDefaultAsync(c => c.Id == gid, ct);
            if (row is null) { return null; }
        }
        else
        {
            row = new TenantGupshupConfig { TenantId = tenantId, ApiKeyEncrypted = string.Empty };
            _db.TenantGupshupConfigs.Add(row);
        }

        row.AppId = req.AppId;
        row.AppName = req.AppName.Trim();
        row.WabaId = string.IsNullOrWhiteSpace(req.WabaId) ? null : req.WabaId.Trim();
        row.PhoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();
        row.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName.Trim();
        row.IsActive = req.IsActive;

        if (!string.IsNullOrWhiteSpace(req.ApiKey))
        {
            row.ApiKeyEncrypted = _secretProtector.Protect(req.ApiKey.Trim());
        }
        else if (id is null || id == Guid.Empty)
        {
            // Creacion sin apikey no es aceptable: sin ella no se puede enviar.
            return null;
        }
        if (!string.IsNullOrWhiteSpace(req.PartnerToken))
        {
            row.PartnerTokenEncrypted = _secretProtector.Protect(req.PartnerToken.Trim());
        }

        _audit.Write(actorUserId, "gupshup-config.upsert", nameof(TenantGupshupConfig), row.Id,
            previousValue: null,
            newValue: new { row.AppId, row.AppName, row.WabaId, row.PhoneNumber, row.IsActive, updatedApiKey = !string.IsNullOrWhiteSpace(req.ApiKey) },
            tenantId: tenantId);

        await _db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var row = await _db.TenantGupshupConfigs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) { return false; }
        // Desatar las lineas que apuntaban a esta App para que no queden con
        // referencia colgando. La linea sigue existiendo (Provider=Gupshup) pero
        // sin credenciales -- la UI deberia forzar reasignarla.
        var lines = await _db.WhatsAppLines.Where(l => l.GupshupAppId == row.Id).ToListAsync(ct);
        foreach (var l in lines) { l.GupshupAppId = null; }
        _db.TenantGupshupConfigs.Remove(row);
        _audit.Write(actorUserId, "gupshup-config.delete", nameof(TenantGupshupConfig), id,
            previousValue: new { row.AppName, row.AppId }, newValue: null, tenantId: row.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> RegenerateInboundTokenAsync(Guid lineId, Guid actorUserId, CancellationToken ct = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) { return null; }
        var previous = line.InboundToken;
        line.InboundToken = GenerateInboundToken();
        _audit.Write(actorUserId, "whatsapp-line.regenerate-inbound-token", nameof(WhatsAppLine), line.Id,
            previousValue: new { hadToken = previous is not null },
            newValue: new { rotated = true },
            tenantId: line.TenantId);
        await _db.SaveChangesAsync(ct);
        return line.InboundToken;
    }

    private static string GenerateInboundToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private GupshupConfigDto Map(TenantGupshupConfig r) => new(
        r.Id, r.AppId, r.AppName, r.WabaId, r.PhoneNumber, r.DisplayName,
        r.IsActive, r.LastValidatedAt,
        MaskApiKey(r.ApiKeyEncrypted),
        HasPartnerToken: !string.IsNullOrWhiteSpace(r.PartnerTokenEncrypted));

    /// <summary>Devuelve "sk_****abcd" con los ultimos 4 chars. Si algo falla al
    /// descifrar, devuelve "(re-ingresar)" para que la UI pida re-tipearla.</summary>
    private string MaskApiKey(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) { return "(sin apikey)"; }
        string clear;
        try { clear = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        if (clear.Length <= 4) { return "****"; }
        return $"{new string('*', Math.Min(clear.Length - 4, 8))}{clear[^4..]}";
    }
}

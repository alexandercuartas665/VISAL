using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Voz;

// Ola 4c (P0): contexto corto por operacion (using var db = _dbFactory.Create();) en vez del IApplicationDbContext scoped.
public sealed class TenantVozConfigService : ITenantVozConfigService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _protector;

    public TenantVozConfigService(IApplicationDbContextFactory dbFactory, ITenantContext tenant, ISecretProtector protector)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _protector = protector;
    }

    public async Task<VozConfigDto> GetAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await db.TenantRetellConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null) { return new VozConfigDto(null, null, null, null, false, true); }
        return ToDto(cfg);
    }

    public async Task<VozConfigDto> SaveAsync(VozConfigSaveRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }

        using var db = _dbFactory.Create();
        var cfg = await db.TenantRetellConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null)
        {
            cfg = new TenantRetellConfig { TenantId = tid };
            db.TenantRetellConfigs.Add(cfg);
        }

        cfg.AgentId = Trim(req.AgentId);
        cfg.FromNumber = Trim(req.FromNumber);
        cfg.TelnyxSipUsername = Trim(req.TelnyxSipUsername);
        cfg.Activa = req.Activa;
        // Solo se re-cifra si viene una key nueva (no borrar la existente con vacio).
        if (!string.IsNullOrWhiteSpace(req.ApiKey))
        {
            cfg.ApiKeyEncrypted = _protector.Protect(req.ApiKey.Trim());
        }
        if (string.IsNullOrWhiteSpace(cfg.WebhookToken))
        {
            cfg.WebhookToken = Guid.NewGuid().ToString("N");
        }

        await db.SaveChangesAsync(ct);
        return ToDto(cfg);
    }

    public async Task<string> RegenerarTokenAsync(Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        using var db = _dbFactory.Create();
        var cfg = await db.TenantRetellConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null)
        {
            cfg = new TenantRetellConfig { TenantId = tid };
            db.TenantRetellConfigs.Add(cfg);
        }
        cfg.WebhookToken = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        return cfg.WebhookToken;
    }

    private static VozConfigDto ToDto(TenantRetellConfig cfg) => new(
        cfg.AgentId, cfg.FromNumber, cfg.TelnyxSipUsername,
        cfg.WebhookToken, !string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted), cfg.Activa);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

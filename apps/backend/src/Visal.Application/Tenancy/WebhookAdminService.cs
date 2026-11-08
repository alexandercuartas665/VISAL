using System.Security.Cryptography;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class WebhookAdminService : IWebhookAdminService
{
    private const int AppPort = 8080;

    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly IDevTunnel _tunnel;
    private readonly IWhatsAppConnectorService _connector;

    public WebhookAdminService(IApplicationDbContextFactory dbFactory, IDevTunnel tunnel, IWhatsAppConnectorService connector)
    {
        _dbFactory = dbFactory;
        _tunnel = tunnel;
        _connector = connector;
    }

    public async Task<WebhookConfigDto> GetAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await GetOrCreateAsync(db, cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> SaveAsync(string mode, string? publicUrl, string? token, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await GetOrCreateAsync(db, cancellationToken);
        cfg.WebhookMode = string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Development";
        cfg.WebhookPublicUrl = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(token))
        {
            cfg.WebhookToken ??= GenerateToken();
        }
        else
        {
            cfg.WebhookToken = token.Trim();
        }
        await db.SaveChangesAsync(cancellationToken);

        // Re-registrar el webhook en las instancias conectadas (igual que al iniciar el tunel en
        // modo desarrollo). Sin esto, cambiar a Produccion solo actualizaba la BD y Evolution seguia
        // entregando los entrantes a la URL anterior; las lineas no recibian en el nuevo destino.
        await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StartTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var cfg = await GetOrCreateAsync(db, cancellationToken);
        cfg.WebhookToken ??= GenerateToken();
        cfg.WebhookMode = "Development";

        var url = await _tunnel.StartAsync(AppPort, cancellationToken);
        cfg.WebhookActiveUrl = url;
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        }
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StopTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        _tunnel.Stop();
        var cfg = await GetOrCreateAsync(db, cancellationToken);
        cfg.WebhookActiveUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    private async Task<EvolutionMasterConfig> GetOrCreateAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var cfg = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
        if (cfg is null)
        {
            cfg = new EvolutionMasterConfig { WebhookMode = "Development" };
            db.EvolutionMasterConfigs.Add(cfg);
            await db.SaveChangesAsync(ct);
        }
        return cfg;
    }

    private WebhookConfigDto Map(EvolutionMasterConfig c)
    {
        var effectiveBase = string.Equals(c.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? c.WebhookPublicUrl
            : c.WebhookActiveUrl;
        var effective = string.IsNullOrWhiteSpace(effectiveBase) ? null : $"{effectiveBase!.TrimEnd('/')}/webhooks/evolution";
        return new WebhookConfigDto(c.WebhookMode, c.WebhookPublicUrl, c.WebhookToken, c.WebhookActiveUrl, _tunnel.IsRunning, effective);
    }

    private static string GenerateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

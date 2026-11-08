using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = _dbFactory.Create();) en vez del IApplicationDbContext scoped.
public sealed class EvolutionConfigService : IEvolutionConfigService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public EvolutionConfigService(
        IApplicationDbContextFactory dbFactory,
        ITenantContext tenantContext,
        ISecretProtector secretProtector,
        IAuditWriter audit)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<EvolutionConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var config = await db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return config is null ? null : Map(config);
    }

    public async Task<EvolutionConfigDto?> UpsertAsync(UpsertEvolutionConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        using var db = _dbFactory.Create();
        var config = await db.TenantEvolutionConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = config is null;

        if (config is null)
        {
            if (string.IsNullOrWhiteSpace(request.ApiToken))
            {
                return null; // un alta requiere token
            }

            config = new TenantEvolutionConfig
            {
                TenantId = tenantId,
                ApiTokenEncrypted = _secretProtector.Protect(request.ApiToken)
            };
            db.TenantEvolutionConfigs.Add(config);
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiToken))
        {
            // Solo re-cifra si se envia un token nuevo; si viene vacio se conserva el actual.
            config.ApiTokenEncrypted = _secretProtector.Protect(request.ApiToken);
        }

        config.BaseUrl = request.BaseUrl.Trim();
        config.InstanceName = request.InstanceName.Trim();
        config.WebhookUrl = request.WebhookUrl?.Trim();
        config.IsActive = true;
        config.UseMasterServer = false; // configurar URL/token propios implica servidor propio

        // Auditoria SIN el token (nunca se loggea el secreto).
        _audit.Write(db, actorUserId, isNew ? "evolution.config.create" : "evolution.config.update",
            nameof(TenantEvolutionConfig), config.Id,
            previousValue: null,
            newValue: new { config.BaseUrl, config.InstanceName, config.WebhookUrl },
            tenantId: tenantId);

        await db.SaveChangesAsync(cancellationToken);
        return Map(config);
    }

    private EvolutionConfigDto Map(TenantEvolutionConfig c) =>
        new(c.BaseUrl ?? "", c.InstanceName ?? "", c.ApiTokenEncrypted is null ? "" : Mask(c.ApiTokenEncrypted), c.WebhookUrl, c.IsActive, c.LastValidatedAt);

    private string Mask(string encryptedToken)
    {
        // Descifra solo para mostrar los ultimos 4 caracteres; nunca se expone completo.
        var token = _secretProtector.Unprotect(encryptedToken);
        return token.Length <= 4 ? "****" : $"{new string('*', Math.Min(token.Length - 4, 8))}{token[^4..]}";
    }
}

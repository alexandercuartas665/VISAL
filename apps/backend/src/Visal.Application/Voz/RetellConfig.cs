using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Voz;

/// <summary>
/// Lee la config de voz del tenant activo desde <c>TenantRetellConfig</c> (una fila
/// por tenant, API key cifrada). Scoped: cachea la carga por scope. La API key se
/// descifra al vuelo y nunca se expone a la UI ni a logs.
/// </summary>
public sealed class RetellConfig : IRetellConfig
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;

    private bool _loaded;
    private string? _apiKey, _agentId, _fromNumber, _webhookToken, _telnyxUser;

    public RetellConfig(IApplicationDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded) { return; }
        _loaded = true;
        // El filtro global de tenant limita a la fila del tenant activo.
        var cfg = await _db.TenantRetellConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.Activa) { return; }
        _agentId = cfg.AgentId;
        _fromNumber = cfg.FromNumber;
        _webhookToken = cfg.WebhookToken;
        _telnyxUser = cfg.TelnyxSipUsername;
        if (!string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            try { _apiKey = _protector.Unprotect(cfg.ApiKeyEncrypted!); }
            catch { /* corrupto: queda sin key y la UI pide re-ingresar */ }
        }
    }

    public string? ApiKey => _apiKey;
    public string? AgentId => _agentId;
    public string? FromNumber => _fromNumber;
    public string? WebhookToken => _webhookToken;
    public string? TelnyxSipUsername => _telnyxUser;

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_apiKey)
        && !string.IsNullOrWhiteSpace(_agentId)
        && !string.IsNullOrWhiteSpace(_fromNumber);
}

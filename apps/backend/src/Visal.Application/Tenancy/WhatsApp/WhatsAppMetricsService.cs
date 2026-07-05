using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>Metricas de bajo costo para el modulo /lineas: contadores de
/// mensajes enviados hoy/mes y saldo Gupshup por linea/App. No persiste
/// nada — todo son queries agregadas contra Messages y llamada HTTP a Gupshup.</summary>
public interface IWhatsAppMetricsService
{
    /// <summary>Numero de mensajes salientes (Outbound) del tenant activo
    /// desde el inicio del dia local (Colombia).</summary>
    Task<int> CountOutboundTodayAsync(CancellationToken ct = default);

    /// <summary>Numero de mensajes salientes del tenant desde el primer dia
    /// del mes actual.</summary>
    Task<int> CountOutboundThisMonthAsync(CancellationToken ct = default);

    /// <summary>Consulta el saldo de la wallet Gupshup usando cualquier linea
    /// Gupshup del tenant. Como el saldo es de cuenta (no por App), la
    /// primera App configurada basta. Devuelve null si no hay linea Gupshup
    /// o Gupshup rechaza la lectura.</summary>
    Task<GupshupBalanceResult?> GetGupshupBalanceAsync(CancellationToken ct = default);
}

internal sealed class WhatsAppMetricsService : IWhatsAppMetricsService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secrets;
    private readonly IGupshupApiClient _gupshup;

    public WhatsAppMetricsService(IApplicationDbContext db, ISecretProtector secrets, IGupshupApiClient gupshup)
    {
        _db = db;
        _secrets = secrets;
        _gupshup = gupshup;
    }

    public async Task<int> CountOutboundTodayAsync(CancellationToken ct = default)
    {
        var startLocal = DateTime.Today; // Kestrel corre en hora local del servidor.
        var startUtc = new DateTimeOffset(startLocal, TimeSpan.Zero).ToUniversalTime();
        return await _db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && m.SentAt >= startUtc)
            .CountAsync(ct);
    }

    public async Task<int> CountOutboundThisMonthAsync(CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var startLocal = new DateTime(today.Year, today.Month, 1);
        var startUtc = new DateTimeOffset(startLocal, TimeSpan.Zero).ToUniversalTime();
        return await _db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && m.SentAt >= startUtc)
            .CountAsync(ct);
    }

    public async Task<GupshupBalanceResult?> GetGupshupBalanceAsync(CancellationToken ct = default)
    {
        // El saldo es de la cuenta Gupshup, no por App. Cualquier App activa
        // sirve para pedirlo. Tomamos la primera IsActive con apikey.
        var cfg = await _db.TenantGupshupConfigs
            .Where(c => c.IsActive && c.ApiKeyEncrypted != null && c.ApiKeyEncrypted != "")
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (cfg is null) { return null; }
        string apikey;
        try { apikey = _secrets.Unprotect(cfg.ApiKeyEncrypted); }
        catch { return new GupshupBalanceResult(false, "Apikey Gupshup corrupta en BD.", null, null, null); }
        // Partner Token es opcional pero necesario para saldo real; si esta
        // guardado lo descifra y lo pasa al cliente.
        string? partnerToken = null;
        if (!string.IsNullOrWhiteSpace(cfg.PartnerTokenEncrypted))
        {
            try { partnerToken = _secrets.Unprotect(cfg.PartnerTokenEncrypted!); }
            catch { /* corrupto: el cliente devolvera un mensaje claro */ }
        }
        return await _gupshup.GetWalletBalanceAsync(apikey, cfg.AppName, cfg.AppId, partnerToken, ct);
    }
}

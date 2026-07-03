using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class WhatsAppConnectorService : IWhatsAppConnectorService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IEvolutionApiClient _client;
    private readonly IWhatsAppProviderResolver _providers;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppConnectorService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISecretProtector secretProtector,
        IEvolutionApiClient client,
        IWhatsAppProviderResolver providers,
        IAuditWriter audit,
        TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _client = client;
        _providers = providers;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<EvolutionServerSettingDto> GetServerAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var masterReady = master is not null && !string.IsNullOrWhiteSpace(master.BaseUrl) && !string.IsNullOrWhiteSpace(master.ApiKeyEncrypted);

        return new EvolutionServerSettingDto(
            UseMasterServer: cfg?.UseMasterServer ?? true,
            MasterReady: masterReady,
            MasterBaseUrl: master?.BaseUrl,
            OwnBaseUrl: cfg?.BaseUrl,
            OwnTokenMasked: cfg?.ApiTokenEncrypted is { } enc ? Mask(enc) : null,
            HasOwnToken: cfg?.ApiTokenEncrypted is not null);
    }

    public async Task<EvolutionServerSettingDto?> SetServerAsync(SetEvolutionServerRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var cfg = await _db.TenantEvolutionConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TenantEvolutionConfig { TenantId = tenantId };
            _db.TenantEvolutionConfigs.Add(cfg);
        }

        cfg.UseMasterServer = request.UseMasterServer;
        if (!request.UseMasterServer)
        {
            cfg.BaseUrl = NormalizeBaseUrl(request.OwnBaseUrl);
            if (!string.IsNullOrWhiteSpace(request.OwnApiToken))
            {
                cfg.ApiTokenEncrypted = _secretProtector.Protect(request.OwnApiToken.Trim());
            }
        }
        cfg.IsActive = true;

        _audit.Write(actorUserId, "evolution.server.set", nameof(TenantEvolutionConfig), cfg.Id,
            previousValue: null, newValue: new { cfg.UseMasterServer, cfg.BaseUrl }, tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return await GetServerAsync(cancellationToken);
    }

    public async Task<LineConnectResult> ConnectLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineConnectResult(false, null, "La linea no existe.");
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return new LineConnectResult(false, null, "No hay servidor Evolution configurado (ni maestro ni propio).");
        }

        var (baseUrl, apiKey) = server.Value;
        var result = await _client.CreateInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (!result.Ok)
        {
            line.Status = WhatsAppLineStatus.Failed;
            line.LastStatusAt = _timeProvider.GetUtcNow();
            await _db.SaveChangesAsync(cancellationToken);
            return new LineConnectResult(false, null, result.Error);
        }

        line.Status = WhatsAppLineStatus.Connecting;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.connect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);

        // Configura el webhook entrante para recibir mensajes en caliente (si esta configurado).
        var (webhookUrl, webhookToken) = await EffectiveWebhookAsync(cancellationToken);
        if (webhookUrl is not null && webhookToken is not null)
        {
            await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
        }

        return new LineConnectResult(true, result.QrBase64, null);
    }

    public async Task<WhatsAppLineDto?> RefreshAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null)
        {
            return Map(line);
        }

        var (baseUrl, apiKey) = server.Value;
        var state = await _client.GetStateAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        if (state.Ok)
        {
            var mapped = state.State?.ToLowerInvariant() switch
            {
                "open" => WhatsAppLineStatus.Connected,
                "connecting" => WhatsAppLineStatus.Connecting,
                "close" => WhatsAppLineStatus.Disconnected,
                _ => line.Status
            };
            if (mapped != line.Status)
            {
                var now = _timeProvider.GetUtcNow();
                line.Status = mapped;
                line.LastStatusAt = now;
                if (mapped == WhatsAppLineStatus.Connected) { line.LastConnectedAt = now; }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        return Map(line);
    }

    public async Task<bool> DisconnectAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is not null)
        {
            var (baseUrl, apiKey) = server.Value;
            await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken);
        }

        line.Status = WhatsAppLineStatus.Disconnected;
        line.LastStatusAt = _timeProvider.GetUtcNow();
        _audit.Write(actorUserId, "whatsapp-line.disconnect", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { instance = EvoInstance(line) }, tenantId: line.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteLineAsync(Guid lineId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return false;
        }

        // Borra la instancia en Evolution (best-effort) antes de quitar la fila.
        var server = await ResolveServerAsync(cancellationToken);
        if (server is not null)
        {
            var (baseUrl, apiKey) = server.Value;
            try { await _client.DeleteInstanceAsync(baseUrl, apiKey, EvoInstance(line), cancellationToken); }
            catch { /* la instancia puede no existir */ }
        }

        _audit.Write(actorUserId, "whatsapp-line.delete", nameof(WhatsAppLine), line.Id,
            previousValue: new { line.InstanceName, line.Status }, newValue: null, tenantId: line.TenantId);

        _db.WhatsAppLines.Remove(line);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<LineSendResult> SendTestAsync(Guid lineId, string phone, string text, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
        {
            return new LineSendResult(false, "Indica el numero y el mensaje.");
        }
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new LineSendResult(false, "La linea no existe.");
        }
        if (line.Status != WhatsAppLineStatus.Connected)
        {
            return new LineSendResult(false, FormatLineNotConnected(line));
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var provider = _providers.ForLine(line);
        var result = await provider.SendTextAsync(line, digits, text.Trim(), cancellationToken);

        _audit.Write(actorUserId, "whatsapp-line.test-send", nameof(WhatsAppLine), line.Id,
            previousValue: null, newValue: new { to = digits, ok = result.Ok, provider = provider.Kind.ToString() }, tenantId: line.TenantId);

        if (!result.Ok)
        {
            await MaybeMarkLineDisconnectedAsync(line, result.Error, cancellationToken);
            return new LineSendResult(false, HumanizeEvolutionError(result.Error, line));
        }
        return new LineSendResult(true, null);
    }

    public async Task<LineSendResult> SendMediaAsync(Guid lineId, string phone, MessageMediaType mediaType, string base64, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var ready = await ReadyLineAsync(lineId, phone, cancellationToken);
        if (ready.Error is not null) { return new LineSendResult(false, ready.Error); }
        var (line, digits) = ready.Value;

        var provider = _providers.ForLine(line);
        var media = new MediaPayload(base64, PublicUrl: null, mimeType, fileName);
        var result = await provider.SendMediaAsync(line, digits, mediaType, media, caption, cancellationToken);
        if (!result.Ok)
        {
            await MaybeMarkLineDisconnectedAsync(line, result.Error, cancellationToken);
            return new LineSendResult(false, HumanizeEvolutionError(result.Error, line));
        }
        return new LineSendResult(true, null);
    }

    public async Task<LineSendResult> SendLocationAsync(Guid lineId, string phone, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var ready = await ReadyLineAsync(lineId, phone, cancellationToken);
        if (ready.Error is not null) { return new LineSendResult(false, ready.Error); }
        var (line, digits) = ready.Value;
        var provider = _providers.ForLine(line);
        var result = await provider.SendLocationAsync(line, digits, latitude, longitude, name, address: null, cancellationToken);
        if (!result.Ok)
        {
            await MaybeMarkLineDisconnectedAsync(line, result.Error, cancellationToken);
            return new LineSendResult(false, HumanizeEvolutionError(result.Error, line));
        }
        return new LineSendResult(true, null);
    }

    // Resuelve linea conectada + numero normalizado. Error no nulo si algo falta.
    // El resolver de servidor ya no vive aca: cada IWhatsAppProvider sabe como
    // levantar sus propias credenciales.
    private async Task<(string Error, (WhatsAppLine line, string digits) Value)> ReadyLineAsync(Guid lineId, string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return ("Indica el numero.", default); }
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) { return ("La linea no existe.", default); }
        if (line.Status != WhatsAppLineStatus.Connected) { return (FormatLineNotConnected(line), default); }
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return (null!, (line, digits));
    }

    // Convierte el error crudo del cliente Evolution (HTTP 500 + JSON) en un mensaje
    // legible para el operador. Reconoce las causas mas comunes que vimos en
    // produccion: instancia caida ("Connection Closed"), instancia inexistente,
    // numero no valido, no autorizado. Cualquier otra cosa pasa intacta pero
    // recortada para no inundar el toast.
    private static string HumanizeEvolutionError(string? raw, WhatsAppLine line)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return "No se pudo enviar el mensaje. Intenta de nuevo."; }
        var alias = !string.IsNullOrWhiteSpace(line.PhoneNumber) ? $"la linea {line.PhoneNumber}" : (!string.IsNullOrWhiteSpace(line.InstanceName) ? $"la linea '{line.InstanceName}'" : "la linea");
        var s = raw;
        if (s.Contains("Connection Closed", StringComparison.OrdinalIgnoreCase))
        {
            return $"La sesion de WhatsApp de {alias} se cerro. Ve a 'Lineas WhatsApp' y vuelve a escanear el QR para reconectar.";
        }
        if (s.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || s.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"La instancia WhatsApp de {alias} no existe en el servidor Evolution. Recrea la linea o contacta soporte.";
        }
        if (s.Contains("not a valid", StringComparison.OrdinalIgnoreCase) || s.Contains("invalid number", StringComparison.OrdinalIgnoreCase) || s.Contains("number is not a valid WhatsApp number", StringComparison.OrdinalIgnoreCase))
        {
            return "El numero del destinatario no esta registrado en WhatsApp. Verifica el numero del paciente.";
        }
        if (s.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || s.StartsWith("HTTP 401") || s.StartsWith("HTTP 403"))
        {
            return "Las credenciales del servidor Evolution no son validas. Avisa al administrador.";
        }
        // Fallback: recortar a 180 chars para no mostrar el JSON entero.
        return s.Length > 180 ? s[..180] + "..." : s;
    }

    // Cuando Evolution responde "Connection Closed" la instancia ya no esta
    // operativa: marcamos la linea como Disconnected para que los siguientes
    // envios fallen rapido con el mensaje claro arriba en vez de seguir
    // golpeando al servidor remoto.
    private async Task MaybeMarkLineDisconnectedAsync(WhatsAppLine line, string? error, CancellationToken ct)
    {
        if (error is null) { return; }
        if (!error.Contains("Connection Closed", StringComparison.OrdinalIgnoreCase)) { return; }
        if (line.Status == WhatsAppLineStatus.Disconnected) { return; }
        line.Status = WhatsAppLineStatus.Disconnected;
        line.UpdatedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

    private static string FormatLineNotConnected(WhatsAppLine line)
    {
        var nombre = !string.IsNullOrWhiteSpace(line.PhoneNumber)
            ? $"La linea {line.PhoneNumber}"
            : (!string.IsNullOrWhiteSpace(line.InstanceName) ? $"La linea '{line.InstanceName}'" : "La linea");
        return $"{nombre} no esta conectada. Ve a 'Lineas WhatsApp' y escanea el QR para activarla.";
    }

    public async Task<int> ApplyWebhookToConnectedLinesAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (webhookUrl, webhookToken) = await EffectiveWebhookAsync(cancellationToken);
        if (webhookUrl is null || webhookToken is null) { return 0; }
        var server = await ResolveServerAsync(cancellationToken);
        if (server is null) { return 0; }
        var (baseUrl, apiKey) = server.Value;

        var lines = await _db.WhatsAppLines.Where(l => l.Status == WhatsAppLineStatus.Connected).ToListAsync(cancellationToken);
        var applied = 0;
        foreach (var line in lines)
        {
            var res = await _client.SetWebhookAsync(baseUrl, apiKey, EvoInstance(line), webhookUrl, webhookToken, cancellationToken);
            if (res.Ok) { applied++; }
        }
        return applied;
    }

    // URL + token efectivos del webhook segun el modo configurado (dev usa la URL activa del tunel).
    private async Task<(string? Url, string? Token)> EffectiveWebhookAsync(CancellationToken ct)
    {
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.WebhookToken)) { return (null, null); }
        var baseUrl = string.Equals(master.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? master.WebhookPublicUrl
            : master.WebhookActiveUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) { return (null, null); }
        return ($"{baseUrl!.TrimEnd('/')}/webhooks/evolution", master.WebhookToken);
    }

    // Servidor efectivo (URL + API key descifrada) segun la eleccion del tenant.
    private async Task<(string baseUrl, string apiKey)?> ResolveServerAsync(CancellationToken ct)
    {
        var cfg = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is not null && !cfg.UseMasterServer)
        {
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.ApiTokenEncrypted)) { return null; }
            return (cfg.BaseUrl!, _secretProtector.Unprotect(cfg.ApiTokenEncrypted!));
        }
        var master = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (master is null || string.IsNullOrWhiteSpace(master.BaseUrl) || string.IsNullOrWhiteSpace(master.ApiKeyEncrypted)) { return null; }
        return (master.BaseUrl!, _secretProtector.Unprotect(master.ApiKeyEncrypted!));
    }

    // Nombre de instancia unico en el servidor compartido: visal_<tenant>_<linea>.
    private static string EvoInstance(WhatsAppLine line) => $"visal_{line.TenantId:N}_{line.Id:N}";

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var url = raw.Trim().TrimEnd('/');
        if (url.EndsWith("/manager", StringComparison.OrdinalIgnoreCase)) { url = url[..^"/manager".Length]; }
        return url.TrimEnd('/');
    }

    private string Mask(string encrypted)
    {
        string value;
        try { value = _secretProtector.Unprotect(encrypted); }
        catch { return "(re-ingresar)"; }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt, l.Provider, l.GupshupAppId, l.InboundToken);
}

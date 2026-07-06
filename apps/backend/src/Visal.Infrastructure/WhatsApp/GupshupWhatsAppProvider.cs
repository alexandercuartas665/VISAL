using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Infrastructure.WhatsApp;

/// <summary>
/// Provider WhatsApp para lineas Gupshup. Envolvia el GupshupApiClient con
/// la resolucion de credenciales (TenantGupshupConfig referenciado por
/// WhatsAppLine.GupshupAppId). La apikey vive cifrada en BD y se descifra
/// al vuelo por cada envio.
///
/// SendMedia exige URL publica: Gupshup no acepta base64. Si el orquestador
/// solo trae Base64, retornamos error claro para que la UI lo muestre; el
/// caller debera subir el archivo al almacenamiento publico primero.
/// </summary>
internal sealed class GupshupWhatsAppProvider : IWhatsAppProvider
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IGupshupApiClient _client;

    public GupshupWhatsAppProvider(
        IApplicationDbContext db,
        ISecretProtector secretProtector,
        IGupshupApiClient client)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
    }

    public WhatsAppProvider Kind => WhatsAppProvider.Gupshup;

    public async Task<ProviderSendResult> SendTextAsync(WhatsAppLine line, string phone, string text, CancellationToken ct)
    {
        var creds = await ResolveCredentialsAsync(line, ct);
        if (creds is null) { return CredentialsMissing(); }
        var (apiKey, source) = creds.Value;
        var r = await _client.SendTextAsync(apiKey, source, phone, text.Trim(), ct);
        return new ProviderSendResult(r.Ok, r.Error, r.MessageId);
    }

    public async Task<ProviderSendResult> SendMediaAsync(WhatsAppLine line, string phone, MessageMediaType mediaType, MediaPayload media, string? caption, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(media.PublicUrl))
        {
            // Gupshup no soporta base64: el orquestador debe pasar URL publica.
            return new ProviderSendResult(false, "Gupshup requiere URL publica del archivo. El sistema subira el archivo antes de enviarlo (G6).");
        }
        var creds = await ResolveCredentialsAsync(line, ct);
        if (creds is null) { return CredentialsMissing(); }
        var (apiKey, source) = creds.Value;

        var gupshupType = mediaType switch
        {
            MessageMediaType.Image => "image",
            MessageMediaType.Video => "video",
            MessageMediaType.Audio => "audio",
            MessageMediaType.Document => "file",
            _ => null,
        };
        if (gupshupType is null) { return new ProviderSendResult(false, "Gupshup: tipo de adjunto no soportado."); }

        var r = await _client.SendMediaAsync(apiKey, source, phone, gupshupType, media.PublicUrl!, caption, media.FileName, ct);
        return new ProviderSendResult(r.Ok, r.Error, r.MessageId);
    }

    public Task<ProviderSendResult> SendLocationAsync(WhatsAppLine line, string phone, double latitude, double longitude, string? name, string? address, CancellationToken ct)
    {
        // Gupshup no expone send-location en la API publica v1 (solo v2 con
        // templates de tipo location). Lo dejamos fuera hasta que aparezca un
        // caso real; el chat panel actual no lo usa.
        return Task.FromResult(new ProviderSendResult(false, "Gupshup: envio de ubicacion no habilitado."));
    }

    /// <summary>Trae apikey descifrada + numero source de la App vinculada a
    /// la linea. Null si algo falta (app no seleccionada, config inactiva o
    /// sin telefono).</summary>
    private async Task<(string ApiKey, string Source)?> ResolveCredentialsAsync(WhatsAppLine line, CancellationToken ct)
    {
        if (line.GupshupAppId is not Guid appPk) { return null; }
        // IgnoreQueryFilters: este provider tambien se llama desde flujos sin
        // tenant scope (webhook auto-respuesta al Quick Reply). El AppId ya
        // vino de la linea, cuyo TenantId es autoritativo — no hay riesgo de
        // fuga entre tenants.
        var cfg = await _db.TenantGupshupConfigs.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == appPk, ct);
        if (cfg is null || !cfg.IsActive || string.IsNullOrWhiteSpace(cfg.PhoneNumber)) { return null; }
        var apikey = _secretProtector.Unprotect(cfg.ApiKeyEncrypted);
        return (apikey, NormalizeDigits(cfg.PhoneNumber!));
    }

    private static ProviderSendResult CredentialsMissing() =>
        new(false, "Gupshup: falta App configurada (AppId + apikey + telefono) para esta linea.");

    private static string NormalizeDigits(string phone) => new(phone.Where(char.IsDigit).ToArray());
}

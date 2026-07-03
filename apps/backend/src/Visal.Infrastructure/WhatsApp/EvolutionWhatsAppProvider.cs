using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Tenancy.WhatsApp;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Evolution;

namespace Visal.Infrastructure.WhatsApp;

/// <summary>
/// Provider WhatsApp para lineas Evolution API. Wrappea el
/// EvolutionApiClient y aisla la resolucion de credenciales (master vs
/// tenant) que hasta ahora vivia en WhatsAppConnectorService. La logica es
/// la misma que ya estaba probada; solo se movio para que el connector se
/// vuelva agnostico al proveedor.
/// </summary>
internal sealed class EvolutionWhatsAppProvider : IWhatsAppProvider
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IEvolutionApiClient _client;

    public EvolutionWhatsAppProvider(
        IApplicationDbContext db,
        ISecretProtector secretProtector,
        IEvolutionApiClient client)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
    }

    public WhatsAppProvider Kind => WhatsAppProvider.Evolution;

    public async Task<ProviderSendResult> SendTextAsync(WhatsAppLine line, string phone, string text, CancellationToken ct)
    {
        var server = await ResolveServerAsync(ct);
        if (server is null) { return new ProviderSendResult(false, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var digits = NormalizeDigits(phone);
        var r = await _client.SendTextAsync(baseUrl, apiKey, EvoInstance(line), digits, text.Trim(), ct);
        return new ProviderSendResult(r.Ok, r.Error);
    }

    public async Task<ProviderSendResult> SendMediaAsync(WhatsAppLine line, string phone, MessageMediaType mediaType, MediaPayload media, string? caption, CancellationToken ct)
    {
        var server = await ResolveServerAsync(ct);
        if (server is null) { return new ProviderSendResult(false, "No hay servidor Evolution configurado."); }
        if (string.IsNullOrWhiteSpace(media.Base64))
        {
            // Evolution requiere base64. Si solo tenemos URL sin descarga previa
            // no podemos enviar; el orquestador deberia haberlo descargado.
            return new ProviderSendResult(false, "Evolution requiere el archivo en base64.");
        }
        var (baseUrl, apiKey) = server.Value;
        var instance = EvoInstance(line);
        var digits = NormalizeDigits(phone);
        var r = mediaType switch
        {
            MessageMediaType.Audio => await _client.SendAudioAsync(baseUrl, apiKey, instance, digits, media.Base64!, ct),
            MessageMediaType.Image => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "image", media.Base64!, media.MimeType, media.FileName, caption, ct),
            MessageMediaType.Video => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "video", media.Base64!, media.MimeType, media.FileName, caption, ct),
            MessageMediaType.Document => await _client.SendMediaAsync(baseUrl, apiKey, instance, digits, "document", media.Base64!, media.MimeType, media.FileName, caption, ct),
            _ => new EvolutionSendResult(false, "Tipo de adjunto no soportado."),
        };
        return new ProviderSendResult(r.Ok, r.Error);
    }

    public async Task<ProviderSendResult> SendLocationAsync(WhatsAppLine line, string phone, double latitude, double longitude, string? name, string? address, CancellationToken ct)
    {
        var server = await ResolveServerAsync(ct);
        if (server is null) { return new ProviderSendResult(false, "No hay servidor Evolution configurado."); }
        var (baseUrl, apiKey) = server.Value;
        var r = await _client.SendLocationAsync(baseUrl, apiKey, EvoInstance(line), NormalizeDigits(phone), latitude, longitude, name, address, ct);
        return new ProviderSendResult(r.Ok, r.Error);
    }

    // Servidor efectivo (URL + API key descifrada) segun la eleccion del tenant.
    // Copia identica de WhatsAppConnectorService.ResolveServerAsync: mismo
    // comportamiento y mismos edge cases.
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
    // Debe coincidir exactamente con la version del connector para no romper
    // sesiones ya creadas.
    private static string EvoInstance(WhatsAppLine line) => $"visal_{line.TenantId:N}_{line.Id:N}";

    private static string NormalizeDigits(string phone) => new(phone.Where(char.IsDigit).ToArray());
}

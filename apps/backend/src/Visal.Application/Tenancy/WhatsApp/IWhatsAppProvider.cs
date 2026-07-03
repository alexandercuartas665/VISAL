using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>Resultado uniforme de un envio saliente. ExternalId es el id que
/// devuelve el proveedor (Evolution: waMessageId; Gupshup: messageId). Sirve
/// para correlacionar acuses de recibo y logs.</summary>
public sealed record ProviderSendResult(bool Ok, string? Error, string? ExternalId = null);

/// <summary>
/// Payload de media. Evolution acepta base64; Gupshup solo acepta URL publica.
/// El orquestador (WhatsAppConnectorService) pasa ambos si tiene, y el
/// provider elige el que soporta. Si el provider no puede materializar el
/// envio con lo recibido, devuelve un error claro.
/// </summary>
/// <param name="Base64">Contenido en base64 (sin prefijo data:...). Null si solo se dispone de URL.</param>
/// <param name="PublicUrl">URL absoluta accesible desde internet (imagen/pdf servida por Visal). Null si solo base64.</param>
public sealed record MediaPayload(string? Base64, string? PublicUrl, string? MimeType, string? FileName);

/// <summary>
/// Abstraccion del proveedor real de WhatsApp detras de una linea. Multi-BSP:
/// cada linea tiene un WhatsAppProvider (Evolution|Gupshup) y el sistema
/// selecciona la implementacion via IWhatsAppProviderResolver.
///
/// Metodos incluidos hoy: SendText, SendMedia, SendLocation (los tres flujos
/// que el WhatsAppChatPanel usa hoy). QR/Connect/Webhook siguen viviendo en
/// el connector porque son operaciones que solo Evolution ejerce (Gupshup se
/// conecta por Meta directamente, sin QR).
/// </summary>
public interface IWhatsAppProvider
{
    /// <summary>Discriminante para logs/telemetria.</summary>
    WhatsAppProvider Kind { get; }

    /// <summary>Envia un mensaje de texto. phone en formato E.164 sin '+'
    /// (ej: 573001234567). El provider aplica su normalizacion interna.</summary>
    Task<ProviderSendResult> SendTextAsync(
        WhatsAppLine line, string phone, string text, CancellationToken ct);

    /// <summary>Envia imagen/video/audio/documento. mediaType define la
    /// naturaleza; el provider elige la codificacion (base64 vs URL). caption
    /// puede acompañar image/video/document; audio la ignora.</summary>
    Task<ProviderSendResult> SendMediaAsync(
        WhatsAppLine line, string phone, MessageMediaType mediaType,
        MediaPayload media, string? caption, CancellationToken ct);

    /// <summary>Envia una ubicacion (lat/lng) con nombre/direccion opcionales.</summary>
    Task<ProviderSendResult> SendLocationAsync(
        WhatsAppLine line, string phone, double latitude, double longitude,
        string? name, string? address, CancellationToken ct);
}

/// <summary>Factory que resuelve la implementacion IWhatsAppProvider adecuada
/// para una linea dada. Uso: var p = resolver.ForLine(line);</summary>
public interface IWhatsAppProviderResolver
{
    IWhatsAppProvider ForLine(WhatsAppLine line);
}

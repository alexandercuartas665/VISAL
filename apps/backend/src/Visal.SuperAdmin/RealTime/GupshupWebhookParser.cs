using System.Text.Json;
using Visal.Application.Tenancy;

namespace Visal.SuperAdmin.RealTime;

/// <summary>
/// Traduce el payload crudo del webhook de Gupshup a nuestro formato de
/// ingesta. Estructura Gupshup (relevante):
///
///   {
///     "app": "AppName", "timestamp": 12345, "version": 2,
///     "type": "message",              // solo procesamos type=message
///     "payload": {
///       "id": "gupshup-msg-uuid",
///       "source": "573001234567",      // phone del remitente
///       "type": "text|image|video|audio|file|location|contacts",
///       "payload": {
///         "text": "...",               // si type=text
///         "url": "...", "caption": "..." // si type=image|video|file
///         "url": "..."                 // si type=audio
///       },
///       "sender": { "phone": "...", "name": "Juan", "country_code": "57", "dial_code": "..." }
///     }
///   }
///
/// Eventos "message-event" (sent/delivered/read/failed), "user-event" y
/// "template-event" se descartan: son acuses/estados, no mensajes nuevos.
///
/// A diferencia de Evolution, el tenant NO viene en el payload -- se saca
/// de la linea que corresponde al token del path del webhook. Por eso este
/// parser devuelve solo IngestMessageRequest; el endpoint provee el TenantId.
/// </summary>
public static class GupshupWebhookParser
{
    public static IngestMessageRequest? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { return null; }

        // Solo procesamos type=message. El resto (message-event, user-event,
        // template-event) son cambios de estado y no producen mensaje nuevo.
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) { return null; }
        if (!string.Equals(typeEl.GetString(), "message", StringComparison.OrdinalIgnoreCase)) { return null; }

        if (!root.TryGetProperty("payload", out var outer) || outer.ValueKind != JsonValueKind.Object) { return null; }

        // Phone del remitente: preferir sender.phone; fallback a payload.source.
        string? phone = null;
        if (outer.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object &&
            sender.TryGetProperty("phone", out var pEl) && pEl.ValueKind == JsonValueKind.String)
        {
            phone = pEl.GetString();
        }
        if (string.IsNullOrWhiteSpace(phone) &&
            outer.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String)
        {
            phone = src.GetString();
        }
        phone = phone is null ? null : new string(phone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(phone)) { return null; }

        // Id externo idempotente -- Gupshup lo devuelve en payload.id.
        var externalId = outer.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()!
            : Guid.NewGuid().ToString("N");

        // Nombre del remitente si viene.
        string? name = null;
        if (outer.TryGetProperty("sender", out var s2) && s2.ValueKind == JsonValueKind.Object &&
            s2.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
        {
            name = nEl.GetString();
        }

        // Tipo + cuerpo. Gupshup pone el contenido real en payload.payload.
        var innerType = outer.TryGetProperty("type", out var itEl) && itEl.ValueKind == JsonValueKind.String
            ? itEl.GetString()!.ToLowerInvariant()
            : "text";
        var body = ExtractBody(outer, innerType);
        if (string.IsNullOrWhiteSpace(body)) { body = "(mensaje no soportado)"; }

        // messageType para downstream: normalizamos a lo mismo que ya usamos
        // en Evolution ("text" | "media") + un tipo nuevo "button_reply" para
        // que la ingesta detecte los Quick Reply de plantillas HSM sin
        // depender del texto (algunas plantillas usan otro idioma o wording).
        var messageType = innerType switch
        {
            "text" => "text",
            // iOS suele entregar los quick-reply de plantilla como "interactive"
            // (formato Meta), Android como "button". Cubrimos ambos + variantes.
            "button" or "button_reply" or "quick_reply" or "interactive" or "reply" => "button_reply",
            _ => "media",
        };

        DateTimeOffset? sentAt = null;
        if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number &&
            ts.TryGetInt64(out var ms))
        {
            // Gupshup manda milisegundos.
            sentAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return new IngestMessageRequest(phone, name, externalId, body!, messageType, sentAt);
    }

    private static string? ExtractBody(JsonElement outer, string innerType)
    {
        if (!outer.TryGetProperty("payload", out var inner) || inner.ValueKind != JsonValueKind.Object) { return null; }
        return innerType switch
        {
            "text" => inner.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            "image" or "video" or "file" => Caption(inner, innerType),
            "audio" => "(audio)",
            "location" => Location(inner),
            "contacts" => "(contacto)",
            "button" or "button_reply" or "quick_reply" or "interactive" or "reply" => ButtonLabel(inner),
            _ => $"({innerType})",
        };
    }

    /// <summary>Extrae el texto visible del boton respondido. Gupshup/Meta usan
    /// distintos campos y anidamientos segun la version del payload y el SO del
    /// cliente (iOS suele mandar "interactive" con button_reply/list_reply
    /// anidado; Android manda "button" con title/text plano). Cubrimos las formas
    /// comunes de forma tolerante.</summary>
    private static string ButtonLabel(JsonElement inner)
    {
        // 1) title/text/payload planos.
        var direct = FirstString(inner, "title", "text", "payload");
        if (direct is not null) { return direct; }

        // 2) reply.title / button_reply.title / list_reply.title (un nivel).
        foreach (var wrap in new[] { "reply", "button_reply", "list_reply", "interactive" })
        {
            if (inner.TryGetProperty(wrap, out var w) && w.ValueKind == JsonValueKind.Object)
            {
                var nested = FirstString(w, "title", "text", "payload");
                if (nested is not null) { return nested; }
                // interactive puede envolver button_reply/list_reply un nivel mas.
                foreach (var wrap2 in new[] { "button_reply", "list_reply" })
                {
                    if (w.TryGetProperty(wrap2, out var w2) && w2.ValueKind == JsonValueKind.Object)
                    {
                        var deep = FirstString(w2, "title", "text");
                        if (deep is not null) { return deep; }
                    }
                }
            }
        }
        return "(boton)";
    }

    /// <summary>Primer campo string no vacio de <paramref name="el"/> entre <paramref name="names"/>.</summary>
    private static string? FirstString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s)) { return s!; }
            }
        }
        return null;
    }

    private static string Caption(JsonElement inner, string kind)
    {
        var label = kind switch { "image" => "imagen", "video" => "video", _ => "archivo" };
        if (inner.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String)
        {
            var c = cap.GetString();
            return string.IsNullOrWhiteSpace(c) ? $"({label})" : c!;
        }
        return $"({label})";
    }

    private static string Location(JsonElement inner)
    {
        double? lat = null, lng = null;
        if (inner.TryGetProperty("latitude", out var latEl) && latEl.ValueKind == JsonValueKind.Number) { lat = latEl.GetDouble(); }
        if (inner.TryGetProperty("longitude", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number) { lng = lngEl.GetDouble(); }
        return (lat, lng) switch
        {
            (double la, double lo) => $"(ubicacion: {la:F5}, {lo:F5})",
            _ => "(ubicacion)",
        };
    }
}

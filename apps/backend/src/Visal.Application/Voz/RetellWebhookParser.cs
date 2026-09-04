using System.Text.Json;

namespace Visal.Application.Voz;

/// <summary>
/// Parseo defensivo del cuerpo JSON del webhook de Retell: { "event": "...",
/// "call": { ... } }. No lanza: si el cuerpo es invalido devuelve null y el
/// endpoint responde 200 igual (para no provocar reintentos infinitos del proveedor).
/// </summary>
public static class RetellWebhookParser
{
    public static RetellWebhookEvento? Parse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }

            var evento = Str(root, "event");
            if (string.IsNullOrWhiteSpace(evento)) { return null; }

            if (!root.TryGetProperty("call", out var call) || call.ValueKind != JsonValueKind.Object)
            {
                return new RetellWebhookEvento(evento!, null, null, null, null, null, null, null, null, null, null);
            }

            var callId = Str(call, "call_id");
            var callStatus = Str(call, "call_status");
            var transcript = Str(call, "transcript");
            var recordingUrl = Str(call, "recording_url");
            var start = Long(call, "start_timestamp");
            var end = Long(call, "end_timestamp");
            var duracion = (start is long s && end is long e && e >= s) ? (int?)((e - s) / 1000) : null;
            var disc = Str(call, "disconnection_reason");
            decimal? costo = null;
            if (call.TryGetProperty("call_cost", out var cc) && cc.ValueKind == JsonValueKind.Object)
            {
                costo = Dec(cc, "combined_cost");
            }
            string? analisis = null;
            if (call.TryGetProperty("call_analysis", out var ca) && ca.ValueKind != JsonValueKind.Null)
            {
                analisis = ca.GetRawText();
            }

            return new RetellWebhookEvento(evento!, callId, callStatus, transcript, duracion, costo, start, end, disc, analisis, recordingUrl);
        }
        catch { return null; }
    }

    private static string? Str(JsonElement o, string p)
        => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement o, string p)
        => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static decimal? Dec(JsonElement o, string p)
    {
        if (!o.TryGetProperty(p, out var v)) { return null; }
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
    }
}

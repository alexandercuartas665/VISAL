using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Visal.Application.Voz;

namespace Visal.Infrastructure.Voz;

/// <summary>
/// Cliente HTTP contra api.retellai.com. Auth: header Authorization: Bearer &lt;apikey&gt;
/// (nunca se loguea). create-phone-call NO se reintenta (evita doble llamada);
/// get-call si se reintenta con backoff porque es idempotente.
/// </summary>
public sealed class RetellHttpClient : IRetellClient
{
    private const string BaseUrl = "https://api.retellai.com";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;
    private readonly IRetellConfig _config;
    private readonly ILogger<RetellHttpClient> _log;

    public RetellHttpClient(HttpClient http, IRetellConfig config, ILogger<RetellHttpClient> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task<CrearLlamadaResult> CrearLlamadaAsync(CrearLlamadaRequest req, CancellationToken ct = default)
    {
        await _config.EnsureLoadedAsync(ct);
        if (!_config.EstaConfigurado)
        {
            return new CrearLlamadaResult(false, "Retell no esta configurado.", null, null);
        }

        var body = new Dictionary<string, object?>
        {
            ["from_number"] = req.FromNumber,
            ["to_number"] = req.ToNumber,
        };
        if (!string.IsNullOrWhiteSpace(req.OverrideAgentId)) { body["override_agent_id"] = req.OverrideAgentId; }
        if (req.VariablesDinamicas is { Count: > 0 }) { body["retell_llm_dynamic_variables"] = req.VariablesDinamicas; }
        if (req.Metadata is { Count: > 0 }) { body["metadata"] = req.Metadata; }
        if (req.CustomSipHeaders is { Count: > 0 }) { body["custom_sip_headers"] = req.CustomSipHeaders; }

        var json = JsonSerializer.Serialize(body);
        // SIN retry: reintentar podria disparar dos llamadas telefonicas a la misma persona.
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v2/create-phone-call")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(msg, cts.Token);
            var respBody = await resp.Content.ReadAsStringAsync(cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var (callId, status) = ParseCall(respBody);
                _log.LogInformation("Retell create-phone-call OK call_id={CallId} status={Status}", callId, status);
                return new CrearLlamadaResult(true, null, callId, status);
            }
            var transitorio = (int)resp.StatusCode >= 500;
            return new CrearLlamadaResult(false, Humanize((int)resp.StatusCode, respBody), null, null, transitorio);
        }
        catch (TaskCanceledException)
        {
            return new CrearLlamadaResult(false, "Timeout al crear la llamada.", null, null, Transitorio: true);
        }
        catch (Exception ex)
        {
            return new CrearLlamadaResult(false, ex.Message, null, null, Transitorio: true);
        }
    }

    public async Task<LlamadaSnapshot?> ConsultarLlamadaAsync(string callId, CancellationToken ct = default)
    {
        await _config.EnsureLoadedAsync(ct);
        if (!_config.EstaConfigurado || string.IsNullOrWhiteSpace(callId)) { return null; }
        var url = $"{BaseUrl}/v2/get-call/{Uri.EscapeDataString(callId)}";
        // get-call es idempotente: hasta 3 intentos con backoff en 5xx/red.
        for (var intento = 0; intento < 3; intento++)
        {
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);
                using var resp = await _http.SendAsync(msg, cts.Token);
                if ((int)resp.StatusCode >= 500)
                {
                    await BackoffAsync(intento, ct);
                    continue;
                }
                if (!resp.IsSuccessStatusCode) { return null; }
                var respBody = await resp.Content.ReadAsStringAsync(cts.Token);
                return ParseSnapshot(respBody);
            }
            catch (TaskCanceledException) { await BackoffAsync(intento, ct); }
            catch (HttpRequestException) { await BackoffAsync(intento, ct); }
        }
        return null;
    }

    private static async Task BackoffAsync(int intento, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMilliseconds(300 * Math.Pow(2, intento)), ct); } catch { }
    }

    private static (string? CallId, string? Status) ParseCall(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            return (Str(r, "call_id"), Str(r, "call_status"));
        }
        catch { return (null, null); }
    }

    private static LlamadaSnapshot? ParseSnapshot(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var callId = Str(r, "call_id");
            if (string.IsNullOrEmpty(callId)) { return null; }
            var start = Long(r, "start_timestamp");
            var end = Long(r, "end_timestamp");
            decimal? costo = null;
            if (r.TryGetProperty("call_cost", out var cc) && cc.ValueKind == JsonValueKind.Object)
            {
                if (cc.TryGetProperty("combined_cost", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) { costo = d; }
            }
            var dur = (start is long s && end is long e && e >= s) ? (int?)((e - s) / 1000) : null;
            return new LlamadaSnapshot(callId!, Str(r, "call_status"), Str(r, "transcript"), dur, costo, start, end);
        }
        catch { return null; }
    }

    private static string Humanize(int status, string body) => status switch
    {
        401 or 403 => "Retell rechazo la API key. Revisa RETELL_API_KEY.",
        404 => "Retell 404: recurso no encontrado (revisa agent id / numero).",
        422 => "Retell 422: datos invalidos (revisa numeros E.164 / agente). " + Snippet(body),
        429 => "Retell rate limit. Reintenta en unos segundos.",
        _ => $"Retell HTTP {status}. " + Snippet(body),
    };

    private static string Snippet(string body)
        => string.IsNullOrWhiteSpace(body) ? "" : (body.Length > 200 ? body[..200] : body);

    private static string? Str(JsonElement o, string p)
        => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement o, string p)
        => o.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;
}

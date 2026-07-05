using System.Net.Http.Headers;
using System.Text.Json;
using Visal.Application.Admin;

namespace Visal.Infrastructure.Gupshup;

/// <summary>
/// Cliente HTTP contra la API publica de Gupshup (api.gupshup.io).
///
/// Convenciones Gupshup relevantes:
/// - Header de auth: 'apikey' (no 'Authorization'). NO logueamos su valor.
/// - Content-Type: application/x-www-form-urlencoded para /wa/api/v1/msg y
///   /wa/api/v2/template/msg. Los objetos JSON van serializados como valor
///   de campos ('message' y 'template' respectivamente).
/// - El body de exito trae {"status":"submitted","messageId":"..."}. En error
///   trae {"status":"error","message":"..."} con HTTP 4xx/5xx.
/// - No hay QR ni conexion: la App vive en Meta directamente. Aca solo
///   despachamos.
/// </summary>
public sealed class GupshupApiClient : IGupshupApiClient
{
    private const string BaseUrl = "https://api.gupshup.io";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;

    public GupshupApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<GupshupSendResult> SendTextAsync(
        string apiKey, string source, string destination, string text,
        CancellationToken cancellationToken = default)
    {
        // message = {"type":"text","text":"..."}
        var message = JsonSerializer.Serialize(new { type = "text", text });
        var form = new Dictionary<string, string>
        {
            ["channel"] = "whatsapp",
            ["source"] = source,
            ["destination"] = destination,
            ["message"] = message,
        };
        return PostFormAsync("/wa/api/v1/msg", apiKey, form, cancellationToken);
    }

    public Task<GupshupSendResult> SendMediaAsync(
        string apiKey, string source, string destination,
        string mediaType, string publicUrl, string? caption, string? fileName,
        CancellationToken cancellationToken = default)
    {
        // Segun tipo, Gupshup exige propiedades distintas:
        // image/video: originalUrl (+ previewUrl opcional) + caption
        // file:        url + filename (+ caption opcional)
        // audio:       url (sin caption)
        var normalized = NormalizeMediaType(mediaType);
        object payload = normalized switch
        {
            "image" or "video" => new { type = normalized, originalUrl = publicUrl, previewUrl = publicUrl, caption = caption ?? "" },
            "file" => new { type = "file", url = publicUrl, filename = fileName ?? "archivo", caption = caption ?? "" },
            "audio" => new { type = "audio", url = publicUrl },
            _ => throw new ArgumentException($"Gupshup: media type '{mediaType}' no soportado.", nameof(mediaType)),
        };
        var message = JsonSerializer.Serialize(payload);
        var form = new Dictionary<string, string>
        {
            ["channel"] = "whatsapp",
            ["source"] = source,
            ["destination"] = destination,
            ["message"] = message,
        };
        return PostFormAsync("/wa/api/v1/msg", apiKey, form, cancellationToken);
    }

    public Task<GupshupSendResult> SendTemplateAsync(
        string apiKey, string source, string destination,
        string templateId, IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default)
    {
        // template = {"id":"<uuid>","params":["a","b"]}. Endpoint /wa/api/v1/template/msg;
        // el legacy v2 fue retirado y devuelve 404.
        var template = JsonSerializer.Serialize(new { id = templateId, @params = parameters });
        var form = new Dictionary<string, string>
        {
            ["channel"] = "whatsapp",
            ["source"] = source,
            ["destination"] = destination,
            ["template"] = template,
        };
        return PostFormAsync("/wa/api/v1/template/msg", apiKey, form, cancellationToken);
    }

    public async Task<GupshupTemplateListResult> ListTemplatesAsync(
        string apiKey, string appName, Guid appId, string? partnerToken,
        CancellationToken cancellationToken = default)
    {
        // 1) User API con solo apikey: si esta App tiene habilitado el endpoint
        //    legacy /wa/app/{appName}/template, devuelve la lista sin requerir
        //    Partner Token. Es el caso comun para Apps recientes.
        var userApiList = await TryListWithUserApiAsync(apiKey, appName, appId, cancellationToken);
        if (userApiList is not null)
        {
            return new GupshupTemplateListResult(true, null, userApiList);
        }

        // 2) Fallback a Partner API cuando hay token cargado.
        if (!string.IsNullOrWhiteSpace(partnerToken))
        {
            return await ListWithPartnerApiAsync(appId, partnerToken!, cancellationToken);
        }

        // 3) Sin Partner Token y User API sin plantillas visibles: devolvemos
        //    lista vacia silenciosa. Mejor UX que un banner rojo: el envio
        //    sigue funcionando si el caller conoce el id de la plantilla,
        //    y esta pagina muestra "sin plantillas aun" sin alarmar.
        return new GupshupTemplateListResult(true, null, Array.Empty<GupshupTemplateInfo>());
    }

    /// <summary>Intento oportunista contra la User API con solo apikey.
    /// Devuelve null si el endpoint no esta disponible (401/403/404) para que
    /// el caller pueda decidir fallback. Devuelve lista (aun vacia) si la
    /// respuesta fue 200 valida.</summary>
    private async Task<IReadOnlyList<GupshupTemplateInfo>?> TryListWithUserApiAsync(
        string apiKey, string appName, Guid appId, CancellationToken ct)
    {
        // Gupshup expone la lista de plantillas de la App en dos rutas segun
        // antiguedad de la cuenta. Probamos ambas antes de rendirnos.
        var candidates = new[]
        {
            $"{BaseUrl}/wa/app/{Uri.EscapeDataString(appName)}/template",
            $"{BaseUrl}/wa/app/{appId:D}/template",
        };
        foreach (var url in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("apikey", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);
                using var resp = await _http.SendAsync(request, cts.Token);
                if (!resp.IsSuccessStatusCode) { continue; }
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                return ParseTemplates(body);
            }
            catch { /* red o timeout: probar siguiente candidato */ }
        }
        return null;
    }

    private async Task<GupshupTemplateListResult> ListWithPartnerApiAsync(
        Guid appId, string partnerToken, CancellationToken cancellationToken)
    {
        // Partner API: GET partner.gupshup.io/partner/app/{appId}/templates
        // Header Authorization: <token> (sin "Bearer ", asi lo pide Gupshup).
        var url = $"https://partner.gupshup.io/partner/app/{appId:D}/templates";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", partnerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(request, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return new GupshupTemplateListResult(false, HumanizeError((int)resp.StatusCode, body), Array.Empty<GupshupTemplateInfo>());
            }
            var list = ParseTemplates(body);
            return new GupshupTemplateListResult(true, null, list);
        }
        catch (Exception ex)
        {
            return new GupshupTemplateListResult(false, ex.Message, Array.Empty<GupshupTemplateInfo>());
        }
    }

    public async Task<GupshupCreateTemplateResult> CreateTemplateAsync(
        string apiKey, string appName, Guid appId, string? partnerToken,
        GupshupCreateTemplateRequest req,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(partnerToken))
        {
            return new GupshupCreateTemplateResult(false,
                "Falta Partner Token de Gupshup. Cargalo en 'Editar App' para poder crear plantillas HSM.",
                null, null);
        }
        // Partner API: POST partner.gupshup.io/partner/app/{appId}/templates
        // form-urlencoded como el user endpoint.
        var url = $"https://partner.gupshup.io/partner/app/{appId:D}/templates";
        var form = new Dictionary<string, string>
        {
            ["elementName"] = req.ElementName,
            ["languageCode"] = req.LanguageCode,
            ["category"] = req.Category,
            ["templateType"] = req.TemplateType,
            ["content"] = req.Content,
            ["example"] = req.Example,
            ["enableSample"] = "true",
        };
        if (!string.IsNullOrWhiteSpace(req.Header)) { form["header"] = req.Header!; }
        if (!string.IsNullOrWhiteSpace(req.ExampleHeader)) { form["exampleHeader"] = req.ExampleHeader!; }
        if (!string.IsNullOrWhiteSpace(req.Footer)) { form["footer"] = req.Footer!; }
        if (req.Buttons is { Count: > 0 }) { form["buttons"] = JsonSerializer.Serialize(req.Buttons); }

        try
        {
            using var content = new FormUrlEncodedContent(form!);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };
            request.Headers.TryAddWithoutValidation("Authorization", partnerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(request, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return new GupshupCreateTemplateResult(false, HumanizeError((int)resp.StatusCode, body), null, null);
            }
            var (id, status) = ExtractCreatedTemplate(body);
            return new GupshupCreateTemplateResult(true, null, id, status);
        }
        catch (Exception ex)
        {
            return new GupshupCreateTemplateResult(false, ex.Message, null, null);
        }
    }

    public async Task<GupshupBalanceResult> GetWalletBalanceAsync(
        string apiKey, string appName, Guid appId, string? partnerToken,
        CancellationToken cancellationToken = default)
    {
        // Ruta canonica: Partner API con Partner Token. Es la unica que Gupshup
        // documenta y garantiza para saldo (endpoint global de la cuenta).
        if (!string.IsNullOrWhiteSpace(partnerToken))
        {
            var partnerResult = await TryPartnerBalanceAsync(partnerToken!, cancellationToken);
            if (partnerResult is not null) { return partnerResult; }
        }

        // Rutas legacy con solo apikey. La mayoria de cuentas modernas devuelven
        // 401/404; se dejan como fallback por si alguna cuenta enterprise vieja
        // aun responde.
        var candidates = new[]
        {
            $"{BaseUrl}/wa/api/v1/users/{Uri.EscapeDataString(apiKey)}/wallet/balance",
            $"{BaseUrl}/wa/api/v1/wallet/balance",
            $"{BaseUrl}/wa/app/{Uri.EscapeDataString(appName)}/wallet",
        };
        foreach (var url in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("apikey", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Timeout);
                using var resp = await _http.SendAsync(request, cts.Token);
                if (!resp.IsSuccessStatusCode) { continue; }
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                var parsed = ParseBalance(body);
                if (parsed.Balance is null) { continue; }
                return new GupshupBalanceResult(true, null, parsed.Balance, parsed.Overdraft, parsed.Currency);
            }
            catch { /* siguiente candidato */ }
        }
        return new GupshupBalanceResult(false,
            "Gupshup solo expone saldo via Partner API. Carga el Partner Token en la App para verlo.",
            null, null, null);
    }

    private async Task<GupshupBalanceResult?> TryPartnerBalanceAsync(string partnerToken, CancellationToken ct)
    {
        var url = "https://partner.gupshup.io/partner/account/api/balance";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", partnerToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(request, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode) { return null; }
            var parsed = ParseBalance(body);
            if (parsed.Balance is null) { return null; }
            return new GupshupBalanceResult(true, null, parsed.Balance, parsed.Overdraft, parsed.Currency);
        }
        catch { return null; }
    }

    private static (decimal? Balance, decimal? Overdraft, string? Currency) ParseBalance(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return (null, null, null); }
            var root = doc.RootElement;
            // Formatos observados:
            //   { "status":"success", "balance": 9.998, "overdraftLimit": 0, "currency":"USD" }
            //   { "walletResponse": { "currentBalance": 9.998, "overDraftLimit": 0 } }
            if (root.TryGetProperty("walletResponse", out var w) && w.ValueKind == JsonValueKind.Object)
            {
                root = w;
            }
            decimal? bal = TryDec(root, "balance") ?? TryDec(root, "currentBalance");
            decimal? od = TryDec(root, "overdraftLimit") ?? TryDec(root, "overDraftLimit");
            var cur = Str(root, "currency") ?? "USD";
            return (bal, od, cur);
        }
        catch { return (null, null, null); }
    }

    private static decimal? TryDec(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) { return null; }
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String when decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    // ============================================================
    //                        Infraestructura
    // ============================================================

    private async Task<GupshupSendResult> PostFormAsync(
        string path, string apiKey, IDictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form!);
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path)
            {
                Content = content,
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("apikey", apiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var resp = await _http.SendAsync(request, cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var id = TryExtractMessageId(body);
                return new GupshupSendResult(true, null, id);
            }
            return new GupshupSendResult(false, HumanizeError((int)resp.StatusCode, body));
        }
        catch (Exception ex)
        {
            return new GupshupSendResult(false, ex.Message);
        }
    }

    private static string NormalizeMediaType(string raw) => raw.ToLowerInvariant() switch
    {
        "document" => "file",
        "pdf" => "file",
        var s => s,
    };

    private static string? TryExtractMessageId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("messageId", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch { /* body no JSON, no es fatal */ }
        return null;
    }

    /// <summary>Devuelve un mensaje legible para el operador sin filtrar el
    /// body completo (puede llegar HTML de proxies intermedios). Si Gupshup
    /// mando error estructurado {"message":"..."} lo usamos.</summary>
    private static string HumanizeError(int status, string body)
    {
        var apiMessage = TryExtractApiMessage(body);
        return status switch
        {
            401 or 403 => "Gupshup rechazo la apikey. Revisa credenciales.",
            404 => "Gupshup 404: endpoint no encontrado (revisa AppId/AppName).",
            429 => "Gupshup rate limit alcanzado. Reintenta en unos segundos.",
            _ when apiMessage is not null => $"Gupshup: {apiMessage}",
            _ => $"Gupshup HTTP {status}",
        };
    }

    private static IReadOnlyList<GupshupTemplateInfo> ParseTemplates(string body)
    {
        var list = new List<GupshupTemplateInfo>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return list; }
            if (!doc.RootElement.TryGetProperty("templates", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return list;
            }
            foreach (var t in arr.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) { continue; }
                var id = Str(t, "id") ?? "";
                var name = Str(t, "elementName") ?? Str(t, "templateName") ?? "";
                var lang = Str(t, "languageCode") ?? Str(t, "language") ?? "";
                var cat = Str(t, "category") ?? "";
                var st = (Str(t, "status") ?? "").ToUpperInvariant();
                var content = Str(t, "data") ?? Str(t, "content") ?? Str(t, "body") ?? "";
                var placeholders = CountPlaceholders(content);
                if (string.IsNullOrEmpty(id)) { continue; }
                list.Add(new GupshupTemplateInfo(id, name, lang, cat, st, content, placeholders));
            }
        }
        catch { /* body no JSON o shape inesperada: devolvemos lo que llevamos */ }
        return list;
    }

    private static (string? Id, string? Status) ExtractCreatedTemplate(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return (null, null); }
            if (doc.RootElement.TryGetProperty("template", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                return (Str(t, "id"), Str(t, "status")?.ToUpperInvariant());
            }
        }
        catch { }
        return (null, null);
    }

    private static string? Str(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Cuenta {{1}}{{2}}... para saber cuantos parametros pide la plantilla.
    // La UI valida que el envio traiga N valores exactos.
    private static int CountPlaceholders(string body)
    {
        var max = 0;
        for (var i = 0; i < body.Length - 3; i++)
        {
            if (body[i] == '{' && body[i + 1] == '{' && char.IsDigit(body[i + 2]))
            {
                var end = i + 3;
                while (end < body.Length && char.IsDigit(body[end])) { end++; }
                if (end + 1 < body.Length && body[end] == '}' && body[end + 1] == '}')
                {
                    if (int.TryParse(body[(i + 2)..end], out var n) && n > max) { max = n; }
                }
            }
        }
        return max;
    }

    private static string? TryExtractApiMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var m) &&
                m.ValueKind == JsonValueKind.String)
            {
                return m.GetString();
            }
        }
        catch { }
        return null;
    }
}

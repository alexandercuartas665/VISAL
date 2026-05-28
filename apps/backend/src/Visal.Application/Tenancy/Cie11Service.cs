using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Cliente del WHO ICD-11 API. Maneja:
/// - CRUD de la configuracion por tenant.
/// - OAuth2 client_credentials con scope `icdapi_access` + cache del Bearer hasta expirar.
/// - Busqueda por termino con Flexisearch y limpieza de HTML del title.
/// - Detalle por entityId (extrae Code + Title.@value).
/// Headers requeridos por WHO: API-Version=v2, Accept-Language=es, Accept=application/json.
/// </summary>
public sealed class Cie11Service(IApplicationDbContext db, ITenantContext tenant, IHttpClientFactory http, ILogger<Cie11Service> log) : ICie11Service
{
    private static readonly Dictionary<Guid, (string token, DateTime exp)> _tokenCache = new();
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async Task<Cie11ConfigDto?> GetConfigAsync(CancellationToken ct = default)
    {
        var c = await db.Cie11Configs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c is null) { return null; }
        return new Cie11ConfigDto(c.TokenUrl, c.ClientId, c.ClientSecret, c.SearchUrl, c.MmsUrlBase, c.Activo);
    }

    public async Task<Cie11ConfigDto?> SaveConfigAsync(Cie11ConfigDto req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { return null; }
        var c = await db.Cie11Configs.FirstOrDefaultAsync(ct);
        if (c is null)
        {
            c = new Cie11Config { TenantId = tid };
            db.Cie11Configs.Add(c);
        }
        c.TokenUrl = req.TokenUrl?.Trim();
        c.ClientId = req.ClientId?.Trim();
        c.ClientSecret = req.ClientSecret?.Trim();
        c.SearchUrl = req.SearchUrl?.Trim();
        c.MmsUrlBase = req.MmsUrlBase?.Trim();
        c.Activo = req.Activo;
        await db.SaveChangesAsync(ct);
        // Invalidar cache de token al guardar.
        if (tenant.TenantId is Guid t2) { _tokenCache.Remove(t2); }
        return new Cie11ConfigDto(c.TokenUrl, c.ClientId, c.ClientSecret, c.SearchUrl, c.MmsUrlBase, c.Activo);
    }

    public async Task<IReadOnlyList<Cie11SearchItem>> SearchAsync(string query, CancellationToken ct = default)
    {
        var cfg = await db.Cie11Configs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.Activo || string.IsNullOrWhiteSpace(cfg.SearchUrl))
        {
            throw new InvalidOperationException("CIE-11 no esta configurado para este tenant.");
        }
        var token = await GetTokenAsync(cfg, ct);

        var url = $"{cfg.SearchUrl}?q={Uri.EscapeDataString(query)}&useFlexisearch=true";
        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("API-Version", "v2");
        req.Headers.Add("Accept-Language", "es");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("CIE-11 search fallo {Code}: {Body}", resp.StatusCode, body);
            throw new InvalidOperationException($"Error en busqueda CIE-11 ({(int)resp.StatusCode}).");
        }

        var json = await resp.Content.ReadFromJsonAsync<SearchRoot>(cancellationToken: ct);
        if (json?.DestinationEntities is null) { return Array.Empty<Cie11SearchItem>(); }

        return json.DestinationEntities
            .Select(e => new Cie11SearchItem(e.EntityId ?? "", StripHtml(e.Title ?? "")))
            .Where(i => !string.IsNullOrEmpty(i.EntityId))
            .ToList();
    }

    public async Task<Cie11Detail?> GetDetailAsync(string entityIdOrUrl, CancellationToken ct = default)
    {
        var cfg = await db.Cie11Configs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.Activo || string.IsNullOrWhiteSpace(cfg.MmsUrlBase))
        {
            throw new InvalidOperationException("CIE-11 no esta configurado para este tenant.");
        }
        var token = await GetTokenAsync(cfg, ct);

        // Si recibimos URL completa, extraer solo el id final.
        var id = entityIdOrUrl;
        if (id.Contains('/'))
        {
            id = new Uri(id).Segments.Last().TrimEnd('/');
        }

        var baseUrl = cfg.MmsUrlBase!.TrimEnd('/');
        var url = $"{baseUrl}/{id}";
        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("API-Version", "v2");
        req.Headers.Add("Accept-Language", "es");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("CIE-11 detail fallo {Code}: {Body}", resp.StatusCode, body);
            return null;
        }
        var json = await resp.Content.ReadFromJsonAsync<DetailRoot>(cancellationToken: ct);
        if (json is null) { return null; }
        var title = StripHtml(json.Title?.Value ?? "");
        return new Cie11Detail(json.Code ?? "", title);
    }

    /// <summary>OAuth2 client_credentials con cache por tenant hasta expirar el token.</summary>
    private async Task<string> GetTokenAsync(Cie11Config cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.TokenUrl) || string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret))
        {
            throw new InvalidOperationException("CIE-11 sin TOKEN_URL/CLIENT_ID/CLIENT_SECRET configurado.");
        }
        if (_tokenCache.TryGetValue(cfg.TenantId, out var hit) && hit.exp > DateTime.UtcNow.AddSeconds(30))
        {
            return hit.token;
        }
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_tokenCache.TryGetValue(cfg.TenantId, out hit) && hit.exp > DateTime.UtcNow.AddSeconds(30))
            {
                return hit.token;
            }
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", cfg.ClientId!),
                new KeyValuePair<string, string>("client_secret", cfg.ClientSecret!),
                new KeyValuePair<string, string>("scope", "icdapi_access")
            });
            using var resp = await client.PostAsync(cfg.TokenUrl, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                log.LogWarning("CIE-11 token fallo {Code}: {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"No se pudo autenticar contra WHO ICD-11 ({(int)resp.StatusCode}).");
            }
            var token = await resp.Content.ReadFromJsonAsync<TokenResp>(cancellationToken: ct);
            if (token?.AccessToken is null) { throw new InvalidOperationException("WHO devolvio token vacio."); }
            var exp = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
            _tokenCache[cfg.TenantId] = (token.AccessToken, exp);
            return token.AccessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private static string StripHtml(string s)
        => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty);

    private sealed class TokenResp
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
    private sealed class SearchRoot
    {
        [JsonPropertyName("destinationEntities")] public List<SearchEntity>? DestinationEntities { get; set; }
    }
    private sealed class SearchEntity
    {
        [JsonPropertyName("id")] public string? EntityId { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
    }
    private sealed class DetailRoot
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("title")] public TitleObj? Title { get; set; }
    }
    private sealed class TitleObj
    {
        [JsonPropertyName("@value")] public string? Value { get; set; }
    }
}

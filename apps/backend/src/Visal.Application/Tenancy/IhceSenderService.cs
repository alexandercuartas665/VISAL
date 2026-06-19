using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Cliente HTTP de los servicios FHIR del API IHCE de MinSalud.
/// Headers usados en sandbox (confirmados en la coleccion Postman de junio 2026):
///   Ocp-Apim-Subscription-Key: {APIMsubskey}
///   Content-Type: application/json
/// No requiere Authorization Bearer en sandbox; en produccion probablemente si.
/// </summary>
public sealed class IhceSenderService(
    IApplicationDbContext db,
    ISecretProtector secrets,
    IHttpClientFactory http,
    ILogger<IhceSenderService> log) : IIhceSenderService
{
    // Cache de token Bearer Azure AD por (TenantId Azure + ClientId), con ttl segun expires_in.
    private static readonly Dictionary<string, (string token, DateTime exp)> _tokenCache = new();
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public async Task<EnvioRdaResultado> EnviarRdaAsync(Guid rdaEventoId, Guid actor, CancellationToken ct = default)
    {
        var ev = await db.RdaEventos.FirstOrDefaultAsync(x => x.Id == rdaEventoId, ct)
            ?? throw new InvalidOperationException($"RdaEvento {rdaEventoId} no existe.");

        // Cargar config y armar la URL completa.
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(ev.Ambiente, ct);
        var url = JoinUrl(urlBase, cfg.PathEnvioRda);

        // El POST al IHCE requiere ADEMAS de la APIM key un Bearer token Azure AD
        // obtenido de la credencial de la sede que emite el RDA.
        var bearer = await ObtenerBearerAsync(ev.SucursalId, ev.Ambiente, cfg, ct);

        log.LogInformation("Enviando RDA {Id} ({Ambiente}) a {Url}", ev.Id, ev.Ambiente, url);

        var call = await PostJsonAsync(url, apimSubskey, ev.BundleJson, bearer, ct);

        // Actualizar estado del evento segun resultado.
        ev.UltimoIntento = DateTimeOffset.UtcNow;
        ev.Intentos += 1;
        string? referencia = null;
        EstadoRdaEvento nuevo;
        if (call.Exito)
        {
            nuevo = EstadoRdaEvento.Aceptado;
            ev.FechaEnvio = DateTimeOffset.UtcNow;
            referencia = ExtraerReferencia(call.ResponseBody);
            ev.ReferenciaMinsalud = referencia;
            ev.ErroresJson = null;
        }
        else
        {
            nuevo = call.HttpStatus switch
            {
                >= 400 and < 500 => EstadoRdaEvento.Rechazado,
                _ => EstadoRdaEvento.Error // 5xx, timeout, red
            };
            ev.ErroresJson = SerializeError(call);
        }
        ev.Estado = nuevo;
        await db.SaveChangesAsync(ct);

        log.LogInformation("RDA {Id} -> HTTP {Status} -> Estado {Estado} ({Ms} ms) por {Actor}",
            ev.Id, call.HttpStatus, nuevo, call.ElapsedMs, actor);

        return new EnvioRdaResultado(call, ev.Id, nuevo, referencia);
    }

    public async Task<IhceCallResult> ConsultarPacienteAsync(ConsultaPacienteRequest req, CancellationToken ct = default)
    {
        // Para consulta no atribuimos a un RdaEvento; usamos el ambiente activo de la config.
        var cfgEntity = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Interoperabilidad no configurada.");
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(cfgEntity.AmbienteActivo, ct);
        var url = JoinUrl(urlBase, cfg.PathConsultarPaciente);

        // Para consultar paciente tambien necesitamos Bearer; tomamos la primera
        // credencial de sede disponible para el ambiente activo.
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Ambiente == cfgEntity.AmbienteActivo
                && !string.IsNullOrEmpty(c.ClientSecretCifrado), ct)
            ?? throw new InvalidOperationException(
                $"No hay credencial de sede configurada para el ambiente {cfgEntity.AmbienteActivo}.");
        var bearer = await ObtenerBearerAsync(credencial.SucursalId, cfgEntity.AmbienteActivo, cfg, ct);

        // Cuerpo: Parameters resource segun la especificacion del MinSalud.
        var payload = new
        {
            resourceType = "Parameters",
            parameter = new object[]
            {
                new
                {
                    name = "identifier",
                    part = new object[]
                    {
                        new { name = "type",  valueString = req.TipoDocumento },
                        new { name = "value", valueString = req.NumeroDocumento }
                    }
                },
                new { name = "humanuser", valueString = req.HumanUserCcCedula ?? $"CC-{req.NumeroDocumento}" }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        return await PostJsonAsync(url, apimSubskey, json, bearer, ct);
    }

    /// <summary>
    /// Obtiene un token Bearer de Azure AD (login.microsoftonline.com) usando las
    /// credenciales OAuth2 client_credentials de la sede + el TenantID Azure y Scope
    /// globales de la config. Cachea por (azureTid+clientId) hasta cerca de expirar.
    /// </summary>
    private async Task<string> ObtenerBearerAsync(Guid sucursalId, AmbienteIhce ambiente, InteroperabilidadConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.AzureTenantId) || string.IsNullOrWhiteSpace(cfg.Scope))
        {
            throw new InvalidOperationException("Falta TenantID Azure o Scope en la config de interoperabilidad.");
        }
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == sucursalId && c.Ambiente == ambiente, ct)
            ?? throw new InvalidOperationException(
                $"La sede {sucursalId} no tiene credenciales configuradas para el ambiente {ambiente}.");
        if (string.IsNullOrEmpty(credencial.ClientSecretCifrado) || string.IsNullOrWhiteSpace(credencial.ClientId))
        {
            throw new InvalidOperationException("La credencial de la sede no tiene ClientID o ClientSecret.");
        }

        var cacheKey = $"{cfg.AzureTenantId}|{credencial.ClientId}";
        if (_tokenCache.TryGetValue(cacheKey, out var hit) && hit.exp > DateTime.UtcNow.AddSeconds(30))
        {
            return hit.token;
        }
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_tokenCache.TryGetValue(cacheKey, out hit) && hit.exp > DateTime.UtcNow.AddSeconds(30))
            {
                return hit.token;
            }
            var clientSecret = secrets.Unprotect(credencial.ClientSecretCifrado);
            var tokenUrl = $"https://login.microsoftonline.com/{cfg.AzureTenantId}/oauth2/v2.0/token";
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", credencial.ClientId!),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("scope", cfg.Scope!)
            });
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var resp = await client.PostAsync(tokenUrl, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"No se pudo obtener token Azure AD ({(int)resp.StatusCode}): {body}");
            }
            var token = JsonSerializer.Deserialize<AzureTokenOk>(body)
                ?? throw new InvalidOperationException("Azure AD devolvio respuesta no parseable.");
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new InvalidOperationException("Azure AD devolvio access_token vacio.");
            }
            var exp = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600);
            _tokenCache[cacheKey] = (token.AccessToken, exp);
            return token.AccessToken;
        }
        finally { _tokenLock.Release(); }
    }

    private sealed class AzureTokenOk
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    // ===================== Helpers =====================

    /// <summary>
    /// Carga la config IHCE, escoge endpoint base + APIM key segun ambiente,
    /// descifra los secretos. Lanza si falta config o APIM key.
    /// </summary>
    private async Task<(InteroperabilidadConfig cfg, string urlBase, string apim)> CargarContextoAsync(AmbienteIhce ambiente, CancellationToken ct)
    {
        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No hay configuracion de interoperabilidad para este tenant.");
        var urlBase = ambiente == AmbienteIhce.Sandbox ? cfg.EndpointSandbox : cfg.EndpointProduccion;
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            throw new InvalidOperationException($"El endpoint {ambiente} no esta configurado.");
        }
        var apimCifrada = ambiente == AmbienteIhce.Sandbox ? cfg.ApimSubskeySandboxCifrada : cfg.ApimSubskeyProduccionCifrada;
        if (string.IsNullOrEmpty(apimCifrada))
        {
            throw new InvalidOperationException($"La APIM Subscription Key {ambiente} no esta configurada.");
        }
        var apim = secrets.Unprotect(apimCifrada);
        return (cfg, urlBase!.TrimEnd('/'), apim);
    }

    private async Task<IhceCallResult> PostJsonAsync(string url, string apimSubskey, string jsonBody, string bearer, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", apimSubskey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, ct);
            sw.Stop();
            var body = await resp.Content.ReadAsStringAsync(ct);
            var ct2 = resp.Content.Headers.ContentType?.ToString();
            return new IhceCallResult(
                Exito: resp.IsSuccessStatusCode,
                HttpStatus: (int)resp.StatusCode,
                ResponseBody: body,
                ResponseContentType: ct2,
                Mensaje: resp.IsSuccessStatusCode
                    ? $"OK ({(int)resp.StatusCode})"
                    : $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}",
                ElapsedMs: (int)sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException tcex) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            return new IhceCallResult(false, 0, null, null, "Cancelado", (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            log.LogWarning(ex, "Error de red en POST {Url}", url);
            return new IhceCallResult(false, 0, null, null, $"Error de red: {ex.Message}", (int)sw.ElapsedMilliseconds);
        }
    }

    private static string JoinUrl(string baseUrl, string path)
    {
        var b = baseUrl.TrimEnd('/');
        var p = path.StartsWith('/') ? path : "/" + path;
        return b + p;
    }

    private static string? ExtraerReferencia(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        // Heuristica: si la respuesta es JSON, intentamos sacar campos comunes
        // (id, referenceId, transactionId). Como no conocemos la estructura exacta de
        // la respuesta de exito, guardamos el body completo en cualquier caso y solo
        // intentamos extraer un identificador legible para la columna referencia_minsalud.
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var name in new[] { "id", "referenceId", "transactionId", "documentId", "rdaId" })
            {
                if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString();
                }
            }
        }
        catch { /* no es JSON o estructura distinta */ }
        return null;
    }

    private static string SerializeError(IhceCallResult call)
        => JsonSerializer.Serialize(new
        {
            httpStatus = call.HttpStatus,
            mensaje = call.Mensaje,
            contentType = call.ResponseContentType,
            body = call.ResponseBody,
            elapsedMs = call.ElapsedMs
        }, new JsonSerializerOptions { WriteIndented = true });
}

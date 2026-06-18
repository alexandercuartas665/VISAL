using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Gestion de la configuracion de interoperabilidad IHCE / RDA por tenant.
/// Cifra ApimSubskey + ClientSecret con <see cref="ISecretProtector"/> y nunca devuelve
/// los plaintext al frontend — solo flags "tiene valor" para que la UI muestre el estado
/// sin exponer el secreto.
/// </summary>
public sealed class InteroperabilidadConfigService(
    IApplicationDbContext db,
    ITenantContext tenant,
    ISecretProtector secrets,
    IHttpClientFactory http,
    ILogger<InteroperabilidadConfigService> log) : IInteroperabilidadConfigService
{
    public async Task<InteroperabilidadConfigDto?> GetConfigAsync(CancellationToken ct = default)
    {
        var c = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c is null) { return null; }
        return Map(c);
    }

    public async Task<InteroperabilidadConfigDto> SaveConfigAsync(InteroperabilidadConfigSaveRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("No hay tenant activo para guardar la config de interoperabilidad.");
        }
        var c = await db.InteroperabilidadConfigs.FirstOrDefaultAsync(ct);
        if (c is null)
        {
            c = new InteroperabilidadConfig { TenantId = tid };
            db.InteroperabilidadConfigs.Add(c);
        }
        c.EndpointSandbox = NullIfEmpty(req.EndpointSandbox);
        c.EndpointProduccion = NullIfEmpty(req.EndpointProduccion);
        c.AzureTenantId = NullIfEmpty(req.AzureTenantId);
        c.Scope = NullIfEmpty(req.Scope);
        c.AmbienteActivo = req.AmbienteActivo;

        // Secretos: si llega valor nuevo lo ciframos; si viene vacio se mantiene el actual.
        if (!string.IsNullOrWhiteSpace(req.ApimSubskeySandboxNueva))
        {
            c.ApimSubskeySandboxCifrada = secrets.Protect(req.ApimSubskeySandboxNueva.Trim());
        }
        if (!string.IsNullOrWhiteSpace(req.ApimSubskeyProduccionNueva))
        {
            c.ApimSubskeyProduccionCifrada = secrets.Protect(req.ApimSubskeyProduccionNueva.Trim());
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Interoperabilidad config guardada por {Actor} (tenant {Tid})", actor, tid);
        return Map(c);
    }

    public async Task<IReadOnlyList<InteroperabilidadCredencialSedeDto>> ListarCredencialesAsync(CancellationToken ct = default)
    {
        // Join con Sucursales para devolver el nombre legible de la sede sin un segundo round-trip.
        var rows = await (
            from cr in db.InteroperabilidadCredencialesSede.AsNoTracking()
            join su in db.Sucursales.AsNoTracking() on cr.SucursalId equals su.Id
            orderby su.Nombre, cr.Ambiente
            select new
            {
                cr.Id,
                cr.SucursalId,
                SucursalNombre = su.Nombre,
                cr.Ambiente,
                cr.CodigoHabilitacion,
                cr.NombreLlave,
                cr.ClientId,
                TieneClientSecret = !string.IsNullOrEmpty(cr.ClientSecretCifrado),
                cr.FechaExpiracion
            }).ToListAsync(ct);

        return rows.Select(r => new InteroperabilidadCredencialSedeDto(
            r.Id, r.SucursalId, r.SucursalNombre, r.Ambiente,
            r.CodigoHabilitacion, r.NombreLlave, r.ClientId,
            r.TieneClientSecret, r.FechaExpiracion)).ToList();
    }

    public async Task<InteroperabilidadCredencialSedeDto> GuardarCredencialAsync(InteroperabilidadCredencialSedeSaveRequest req, Guid actor, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }
        var existe = await db.InteroperabilidadCredencialesSede
            .FirstOrDefaultAsync(x => x.SucursalId == req.SucursalId && x.Ambiente == req.Ambiente, ct);

        if (existe is null)
        {
            existe = new InteroperabilidadCredencialSede
            {
                TenantId = tid,
                SucursalId = req.SucursalId,
                Ambiente = req.Ambiente
            };
            db.InteroperabilidadCredencialesSede.Add(existe);
        }
        existe.CodigoHabilitacion = NullIfEmpty(req.CodigoHabilitacion);
        existe.NombreLlave = NullIfEmpty(req.NombreLlave);
        existe.ClientId = NullIfEmpty(req.ClientId);
        existe.FechaExpiracion = req.FechaExpiracion;

        if (!string.IsNullOrWhiteSpace(req.ClientSecretNuevo))
        {
            existe.ClientSecretCifrado = secrets.Protect(req.ClientSecretNuevo.Trim());
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Credencial IHCE guardada para sede {Sid} ({Amb}) por {Actor}",
            req.SucursalId, req.Ambiente, actor);

        // Resolver el nombre de la sede para el DTO de respuesta.
        var sucNombre = await db.Sucursales.AsNoTracking()
            .Where(s => s.Id == req.SucursalId).Select(s => s.Nombre).FirstOrDefaultAsync(ct) ?? "";

        return new InteroperabilidadCredencialSedeDto(
            existe.Id, existe.SucursalId, sucNombre, existe.Ambiente,
            existe.CodigoHabilitacion, existe.NombreLlave, existe.ClientId,
            !string.IsNullOrEmpty(existe.ClientSecretCifrado), existe.FechaExpiracion);
    }

    public async Task<bool> EliminarCredencialAsync(Guid credencialId, Guid actor, CancellationToken ct = default)
    {
        var cr = await db.InteroperabilidadCredencialesSede.FirstOrDefaultAsync(x => x.Id == credencialId, ct);
        if (cr is null) { return false; }
        db.InteroperabilidadCredencialesSede.Remove(cr);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Credencial IHCE {Cid} eliminada por {Actor}", credencialId, actor);
        return true;
    }

    public async Task<ProbarConexionResultado> ProbarConexionAsync(Guid credencialId, CancellationToken ct = default)
    {
        var cr = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credencialId, ct);
        if (cr is null)
        {
            return new ProbarConexionResultado(false, "Credencial no encontrada.", null, null, null, null, null);
        }
        var cfg = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.AzureTenantId) || string.IsNullOrWhiteSpace(cfg.Scope))
        {
            return new ProbarConexionResultado(false,
                "Falta configurar TenantID Azure o Scope en los parametros del API IHCE.",
                null, null, null, null, null);
        }
        if (string.IsNullOrWhiteSpace(cr.ClientId) || string.IsNullOrEmpty(cr.ClientSecretCifrado))
        {
            return new ProbarConexionResultado(false,
                "La credencial no tiene Client ID o Client Secret configurados.",
                null, null, null, null, null);
        }

        string clientSecret;
        try
        {
            clientSecret = secrets.Unprotect(cr.ClientSecretCifrado);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo descifrar el ClientSecret de la credencial {Cid}", credencialId);
            return new ProbarConexionResultado(false,
                "No se pudo descifrar el Client Secret guardado (posible cambio de llaves Data Protection).",
                null, null, null, null, null);
        }

        // POST a https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token con grant_type=client_credentials.
        // El APIMsubskey NO va en este request — solo va en las llamadas al API IHCE protegido por APIM.
        var tokenUrl = $"https://login.microsoftonline.com/{cfg.AzureTenantId}/oauth2/v2.0/token";
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", cr.ClientId!),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("scope", cfg.Scope!)
        });

        var client = http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        try
        {
            using var resp = await client.PostAsync(tokenUrl, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                var ok = JsonSerializer.Deserialize<AzureTokenOk>(body);
                if (ok is null || string.IsNullOrEmpty(ok.AccessToken))
                {
                    return new ProbarConexionResultado(false,
                        "Azure AD devolvio 200 pero sin access_token. Revisa la respuesta.",
                        null, null, null, (int)resp.StatusCode, null);
                }
                var frag = ok.AccessToken.Length > 16 ? ok.AccessToken[..16] + "..." : ok.AccessToken;
                log.LogInformation("Token IHCE obtenido para credencial {Cid} (sede {Sid}, {Amb}). Expira en {Exp}s.",
                    credencialId, cr.SucursalId, cr.Ambiente, ok.ExpiresIn);
                return new ProbarConexionResultado(true,
                    "Token Bearer obtenido correctamente.",
                    frag, ok.ExpiresIn, ok.TokenType, (int)resp.StatusCode, null);
            }
            // Error: parsear payload Azure AD para devolver error/error_description.
            string? errCode = null; string? errDesc = null;
            try
            {
                var err = JsonSerializer.Deserialize<AzureTokenError>(body);
                errCode = err?.Error;
                errDesc = err?.ErrorDescription;
            }
            catch { /* body no era JSON valido */ }

            log.LogWarning("Token IHCE fallo {Code} para credencial {Cid}: {Err} {Desc}",
                (int)resp.StatusCode, credencialId, errCode, errDesc);
            var mensaje = !string.IsNullOrWhiteSpace(errDesc)
                ? errDesc.Length > 240 ? errDesc[..240] + "..." : errDesc
                : $"Azure AD respondio HTTP {(int)resp.StatusCode}.";
            return new ProbarConexionResultado(false, mensaje, null, null, null, (int)resp.StatusCode, errCode);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Error de red probando token IHCE para credencial {Cid}", credencialId);
            return new ProbarConexionResultado(false,
                $"Error de red: {ex.Message}", null, null, null, null, null);
        }
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static InteroperabilidadConfigDto Map(InteroperabilidadConfig c) => new(
        c.EndpointSandbox,
        c.EndpointProduccion,
        c.AzureTenantId,
        c.Scope,
        !string.IsNullOrEmpty(c.ApimSubskeySandboxCifrada),
        !string.IsNullOrEmpty(c.ApimSubskeyProduccionCifrada),
        c.AmbienteActivo);

    private sealed class AzureTokenOk
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class AzureTokenError
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }
}

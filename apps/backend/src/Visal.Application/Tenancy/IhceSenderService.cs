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

    // Cache de UUIDs FHIR resueltos contra el directorio IHCE.
    //   Sede: key = "{ambiente}|REPS|{codigoHabilitacion}"      value = uuid FHIR devuelto por MinSalud
    //   EAPB: key = "{ambiente}|EAPB|{nombreNormalizado}"        value = uuid FHIR devuelto por MinSalud
    // Se llenan la primera vez que se envia un RDA y se reutilizan en envios siguientes.
    // TTL implicito: hasta que reinicien el proceso — los UUIDs FHIR de MinSalud son estables.
    private static readonly Dictionary<string, string> _orgUuidCache = new();
    private static readonly SemaphoreSlim _orgUuidLock = new(1, 1);

    public async Task<EnvioRdaResultado> EnviarRdaAsync(Guid rdaEventoId, Guid actor, CancellationToken ct = default)
    {
        var ev = await db.RdaEventos.FirstOrDefaultAsync(x => x.Id == rdaEventoId, ct)
            ?? throw new InvalidOperationException($"RdaEvento {rdaEventoId} no existe.");

        // Cargar config y armar la URL completa. El path depende del TipoRda:
        //  - Paciente => /Composition/$enviar-rda-paciente
        //  - Consulta => /Composition/$enviar-rda-consulta
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(ev.Ambiente, ct);
        var path = ev.TipoRda == TipoRdaIhce.Consulta ? cfg.PathEnvioRdaConsulta : cfg.PathEnvioRda;
        var url = JoinUrl(urlBase, path);

        // El POST al IHCE requiere ADEMAS de la APIM key un Bearer token Azure AD
        // obtenido de la credencial de la sede que emite el RDA.
        var bearer = await ObtenerBearerAsync(ev.SucursalId, ev.Ambiente, cfg, ct);

        // PRE-FLIGHT CHECK: consultar profesional firmante en el directorio IHCE.
        // Si MinSalud no lo tiene (cruzado contra ReTHUS), no tiene sentido enviar el RDA
        // porque devolveria BUNDLE-005 'Practitioner not found'. Reportamos error claro.
        if (ev.ProfesionalId is Guid profId)
        {
            var prof = await db.Profesionales.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == profId, ct);
            if (prof is not null)
            {
                var consultaUrl = JoinUrl(urlBase, cfg.PathConsultarProfesional);
                var consultaPayload = ParametersPayload(prof.TipoDocumento, prof.NumeroDocumento);
                var consultaCall = await PostJsonAsync(consultaUrl, apimSubskey, consultaPayload, bearer, ct);
                if (!consultaCall.Exito)
                {
                    ev.UltimoIntento = DateTimeOffset.UtcNow;
                    ev.Intentos += 1;
                    ev.Estado = EstadoRdaEvento.Rechazado;
                    ev.ErroresJson = JsonSerializer.Serialize(new
                    {
                        preflight = "consultar-profesional-salud",
                        mensaje = $"El profesional CC {prof.NumeroDocumento} ({prof.NombreCompleto}) no esta registrado en el directorio IHCE. RDA no enviado.",
                        consulta = new
                        {
                            httpStatus = consultaCall.HttpStatus,
                            body = consultaCall.ResponseBody,
                            elapsedMs = consultaCall.ElapsedMs
                        }
                    }, new JsonSerializerOptions { WriteIndented = true });
                    await db.SaveChangesAsync(ct);
                    log.LogWarning("RDA {Id} NO enviado: pre-flight fallo, profesional {Cc} no esta en IHCE (HTTP {Code})",
                        ev.Id, prof.NumeroDocumento, consultaCall.HttpStatus);
                    return new EnvioRdaResultado(consultaCall, ev.Id, ev.Estado, null);
                }
                log.LogInformation("Pre-flight OK: profesional {Cc} encontrado en IHCE", prof.NumeroDocumento);
            }
        }

        // PASO CLAVE: resolver los UUIDs FHIR reales de las Organizations contra el directorio
        // IHCE y reescribir el bundle. El builder pone REPS y PAYER-<codigo> como Ids, pero
        // MinSalud rechaza con BUNDLE-005 si esos ids no existen en su directorio interno —
        // en su lugar hay que usar los UUIDs FHIR asignados por su servidor. Al enviar, hacemos
        // $consultar-organizacion / $consultar-eapb, cacheamos el uuid, y reemplazamos en el JSON.
        var bundleJson = await ResolverOrganizationUuidsAsync(ev, urlBase, apimSubskey, bearer, ct);

        log.LogInformation("Enviando RDA {Id} ({Ambiente}) a {Url}", ev.Id, ev.Ambiente, url);

        var call = await PostJsonAsync(url, apimSubskey, bundleJson, bearer, ct);

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
        // Consultar paciente lleva ADEMAS un 'humanuser' (operador), que es CC del usuario que consulta.
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

    public async Task<IhceCallResult> ConsultarProfesionalAsync(ConsultaPacienteRequest req, CancellationToken ct = default)
    {
        var cfgEntity = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Interoperabilidad no configurada.");
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(cfgEntity.AmbienteActivo, ct);
        var url = JoinUrl(urlBase, cfg.PathConsultarProfesional);

        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Ambiente == cfgEntity.AmbienteActivo
                && !string.IsNullOrEmpty(c.ClientSecretCifrado), ct)
            ?? throw new InvalidOperationException(
                $"No hay credencial de sede configurada para el ambiente {cfgEntity.AmbienteActivo}.");
        var bearer = await ObtenerBearerAsync(credencial.SucursalId, cfgEntity.AmbienteActivo, cfg, ct);

        var json = ParametersPayload(req.TipoDocumento, req.NumeroDocumento);
        return await PostJsonAsync(url, apimSubskey, json, bearer, ct);
    }

    public async Task<IhceCallResult> ConsultarOrganizacionAsync(ConsultaOrganizacionRequest req, CancellationToken ct = default)
    {
        var cfgEntity = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Interoperabilidad no configurada.");
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(cfgEntity.AmbienteActivo, ct);
        // El path no vive en la config todavia; el Postman oficial lo fija en /Organization/$consultar-organizacion.
        var url = JoinUrl(urlBase, "/Organization/$consultar-organizacion");

        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Ambiente == cfgEntity.AmbienteActivo
                && !string.IsNullOrEmpty(c.ClientSecretCifrado), ct)
            ?? throw new InvalidOperationException(
                $"No hay credencial de sede configurada para el ambiente {cfgEntity.AmbienteActivo}.");
        var bearer = await ObtenerBearerAsync(credencial.SucursalId, cfgEntity.AmbienteActivo, cfg, ct);

        // Construimos Parameters con solo los campos con valor (MinSalud tolera params opcionales).
        var parameters = new List<object>();
        if (!string.IsNullOrWhiteSpace(req.TaxIdentifier))
            parameters.Add(new { name = "TaxIdentifier", valueString = req.TaxIdentifier });
        if (!string.IsNullOrWhiteSpace(req.HealthcareProviderIdentifier))
            parameters.Add(new { name = "HealthcareProviderIdentifier", valueString = req.HealthcareProviderIdentifier });
        if (!string.IsNullOrWhiteSpace(req.Name))
            parameters.Add(new { name = "name", valueString = req.Name });
        if (parameters.Count == 0)
        {
            return new IhceCallResult(false, 0, null, null,
                "Ingresa NIT, codigo de habilitacion o nombre para consultar la organizacion.", 0);
        }

        var json = JsonSerializer.Serialize(new { resourceType = "Parameters", parameter = parameters });
        return await PostJsonAsync(url, apimSubskey, json, bearer, ct);
    }

    public async Task<IhceCallResult> ConsultarEapbAsync(ConsultaEapbRequest req, CancellationToken ct = default)
    {
        var cfgEntity = await db.InteroperabilidadConfigs.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Interoperabilidad no configurada.");
        var (cfg, urlBase, apimSubskey) = await CargarContextoAsync(cfgEntity.AmbienteActivo, ct);
        var url = JoinUrl(urlBase, "/Organization/$consultar-eapb");

        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Ambiente == cfgEntity.AmbienteActivo
                && !string.IsNullOrEmpty(c.ClientSecretCifrado), ct)
            ?? throw new InvalidOperationException(
                $"No hay credencial de sede configurada para el ambiente {cfgEntity.AmbienteActivo}.");
        var bearer = await ObtenerBearerAsync(credencial.SucursalId, cfgEntity.AmbienteActivo, cfg, ct);

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return new IhceCallResult(false, 0, null, null, "Ingresa el nombre de la EAPB a consultar.", 0);
        }

        var payload = new
        {
            resourceType = "Parameters",
            parameter = new object[]
            {
                new { name = "name", valueString = req.Name.Trim() }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        return await PostJsonAsync(url, apimSubskey, json, bearer, ct);
    }

    /// <summary>
    /// Reescribe el <c>bundle_json</c> del RdaEvento reemplazando los <c>id</c> locales de las
    /// Organizations (REPS y PAYER-{codigo}) por los UUIDs FHIR reales que MinSalud tiene en su
    /// directorio sandbox/prod. La primera vez que se envia contra una sede/EAPB, se consulta
    /// <c>$consultar-organizacion</c> y <c>$consultar-eapb</c>; los uuids quedan cacheados en
    /// memoria hasta que se reinicie el proceso.
    /// </summary>
    private async Task<string> ResolverOrganizationUuidsAsync(RdaEvento ev, string urlBase,
        string apimSubskey, string bearer, CancellationToken ct)
    {
        var bundleJson = ev.BundleJson;

        // ----- Sede prestadora (REPS) -----
        var credencial = await db.InteroperabilidadCredencialesSede.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SucursalId == ev.SucursalId && c.Ambiente == ev.Ambiente, ct);
        var reps = credencial?.CodigoHabilitacion;
        if (!string.IsNullOrWhiteSpace(reps))
        {
            var sedeUuid = await ResolverOrgUuidAsync(
                cacheKey: $"{ev.Ambiente}|REPS|{reps}",
                urlPath: "/Organization/$consultar-organizacion",
                payload: BuildOrgConsultaPayload(healthcareProviderIdentifier: reps),
                urlBase: urlBase, apimSubskey: apimSubskey, bearer: bearer, ct: ct);
            if (!string.IsNullOrEmpty(sedeUuid))
            {
                // Reemplazamos SOLO el REPS como valor de id/ref del recurso Organization de la
                // sede. No tocamos los identifier.value (los mantenemos como REPS legibles).
                bundleJson = ReemplazarOrgId(bundleJson, reps!, sedeUuid);
                log.LogInformation("RDA {Id}: reemplazado id Organization sede REPS={Reps} -> uuid {Uuid}",
                    ev.Id, reps, sedeUuid);
            }
            else
            {
                log.LogWarning("RDA {Id}: no se pudo resolver UUID FHIR para REPS {Reps}. Se envia con el id local.",
                    ev.Id, reps);
            }
        }

        // ----- Pagador (EAPB) -----
        // El builder pone "PAYER-{codigo}" como id. Buscamos el nombre del pagador dentro del
        // bundle para consultar $consultar-eapb por nombre.
        var payerInfo = ExtraerPagadorDelBundle(bundleJson);
        if (payerInfo is { LocalId: string localId, Name: string nombre } && !string.IsNullOrWhiteSpace(nombre))
        {
            var eapbUuid = await ResolverOrgUuidAsync(
                cacheKey: $"{ev.Ambiente}|EAPB|{nombre.ToUpperInvariant()}",
                urlPath: "/Organization/$consultar-eapb",
                payload: BuildEapbConsultaPayload(nombre),
                urlBase: urlBase, apimSubskey: apimSubskey, bearer: bearer, ct: ct);
            if (!string.IsNullOrEmpty(eapbUuid))
            {
                bundleJson = ReemplazarOrgId(bundleJson, localId, eapbUuid);
                log.LogInformation("RDA {Id}: reemplazado id EAPB {LocalId} -> uuid {Uuid} (nombre {Nombre})",
                    ev.Id, localId, eapbUuid, nombre);
            }
            else
            {
                log.LogWarning("RDA {Id}: no se pudo resolver UUID FHIR para EAPB '{Nombre}'.", ev.Id, nombre);
            }
        }

        return bundleJson;
    }

    private async Task<string?> ResolverOrgUuidAsync(string cacheKey, string urlPath, string payload,
        string urlBase, string apimSubskey, string bearer, CancellationToken ct)
    {
        // Hit-cache o consulta.
        if (_orgUuidCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }
        await _orgUuidLock.WaitAsync(ct);
        try
        {
            if (_orgUuidCache.TryGetValue(cacheKey, out cached)) { return cached; }
            var url = JoinUrl(urlBase, urlPath);
            var call = await PostJsonAsync(url, apimSubskey, payload, bearer, ct);
            if (!call.Exito) { return null; }
            var uuid = ExtraerPrimerUuidDeSearchset(call.ResponseBody);
            if (!string.IsNullOrEmpty(uuid))
            {
                _orgUuidCache[cacheKey] = uuid;
            }
            return uuid;
        }
        finally { _orgUuidLock.Release(); }
    }

    /// <summary>Devuelve el id del primer Organization del Bundle searchset devuelto por MinSalud.</summary>
    private static string? ExtraerPrimerUuidDeSearchset(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array) { return null; }
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("resource", out var res)) { continue; }
                if (!res.TryGetProperty("resourceType", out var rt) ||
                    rt.GetString() != "Organization") { continue; }
                if (res.TryGetProperty("id", out var idProp) &&
                    idProp.ValueKind == JsonValueKind.String)
                {
                    return idProp.GetString();
                }
            }
        }
        catch (JsonException) { /* body no-JSON */ }
        return null;
    }

    private static string BuildOrgConsultaPayload(string? taxIdentifier = null,
        string? healthcareProviderIdentifier = null, string? name = null)
    {
        var parameters = new List<object>();
        if (!string.IsNullOrWhiteSpace(taxIdentifier))
            parameters.Add(new { name = "TaxIdentifier", valueString = taxIdentifier });
        if (!string.IsNullOrWhiteSpace(healthcareProviderIdentifier))
            parameters.Add(new { name = "HealthcareProviderIdentifier", valueString = healthcareProviderIdentifier });
        if (!string.IsNullOrWhiteSpace(name))
            parameters.Add(new { name = "name", valueString = name });
        return JsonSerializer.Serialize(new { resourceType = "Parameters", parameter = parameters });
    }

    private static string BuildEapbConsultaPayload(string name)
        => JsonSerializer.Serialize(new
        {
            resourceType = "Parameters",
            parameter = new object[] { new { name = "name", valueString = name } }
        });

    /// <summary>
    /// Reemplaza <c>"id": "{viejo}"</c> en el recurso Organization y todas las references
    /// del formato <c>"reference": "Organization/{viejo}"</c> y <c>"reference": "#{viejo}"</c>
    /// por el UUID nuevo. Preserva los identifier.value (que siguen siendo REPS/EAPB legibles).
    /// </summary>
    private static string ReemplazarOrgId(string bundleJson, string idViejo, string idNuevo)
    {
        // Reemplazo textual estricto — evita tocar por accidente valores en identifier.value.
        // El id aparece SOLO como valor de "id" en el recurso Organization y en "reference".
        // Como los REPS pueden ser subcadenas de otros identifiers, usamos delimitadores.
        var s = bundleJson;
        s = s.Replace($"\"id\": \"{idViejo}\"", $"\"id\": \"{idNuevo}\"");
        s = s.Replace($"\"reference\": \"Organization/{idViejo}\"", $"\"reference\": \"Organization/{idNuevo}\"");
        s = s.Replace($"\"reference\": \"#{idViejo}\"", $"\"reference\": \"#{idNuevo}\"");
        return s;
    }

    /// <summary>Busca la Organization con profile HealthBenefitPlanAdmin (pagador) y devuelve su id local + name.</summary>
    private static (string LocalId, string Name)? ExtraerPagadorDelBundle(string bundleJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(bundleJson);
            if (!doc.RootElement.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array) { return null; }
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("resource", out var res)) { continue; }
                if (!res.TryGetProperty("resourceType", out var rt) ||
                    rt.GetString() != "Organization") { continue; }
                if (!res.TryGetProperty("meta", out var meta) ||
                    !meta.TryGetProperty("profile", out var prof) ||
                    prof.ValueKind != JsonValueKind.Array) { continue; }
                var esPagador = false;
                foreach (var p in prof.EnumerateArray())
                {
                    if (p.GetString()?.Contains("HealthBenefitPlan") == true) { esPagador = true; break; }
                }
                if (!esPagador) { continue; }
                var id = res.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var name = res.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    return (id!, name!);
                }
            }
        }
        catch (JsonException) { /* bundle mal formado, ignoramos */ }
        return null;
    }

    /// <summary>
    /// Construye el cuerpo FHIR R4 <c>Parameters</c> que esperan las operaciones custom
    /// <c>$consultar-paciente-exacto</c> y <c>$consultar-profesional-salud</c>.
    /// </summary>
    private static string ParametersPayload(string tipoDoc, string numero)
    {
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
                        new { name = "type",  valueString = tipoDoc },
                        new { name = "value", valueString = numero }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(payload);
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

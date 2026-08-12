using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

/// <summary>Config del webhook de formularios web del tenant para Mi cuenta (incluye el token en claro).</summary>
public sealed record FormWebhookConfigDto(Guid TenantId, string? Token, bool IsEnabled, bool HasToken, DateTimeOffset? LastUsedAt);

/// <summary>
/// Resultado de procesar una recepcion del webhook de formularios. StatusCode mapea directo a la
/// respuesta HTTP del endpoint. Duplicate=true cuando el payload ya se recibio en la ventana de dedup.
/// </summary>
public sealed record FormWebhookResult(int StatusCode, bool Ok, Guid? CardId = null, string? Error = null, bool Duplicate = false);

public interface IFormWebhookService
{
    Task<FormWebhookConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<FormWebhookConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<FormWebhookConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Procesa una recepcion del webhook publico de formularios: resuelve el tenant por el token de
    /// la URL, deduplica, asegura la etapa "PQRS" y crea la tarjeta. Acepta el cuerpo como
    /// application/x-www-form-urlencoded (Elementor) o application/json.
    /// </summary>
    Task<FormWebhookResult> ProcessAsync(string token, string? contentType, string rawBody, CancellationToken cancellationToken = default);
}

/// <summary>
/// Intake publico de formularios web (WordPress -> tarjeta en el embudo). Cada tenant tiene un token
/// opaco (hash para buscar, cifrado para mostrar en Mi cuenta) que viaja EN LA URL del webhook. La
/// tarjeta se enruta a la etapa "PQRS" del tenant (se crea idempotente en la primera recepcion). El
/// webhook es la frontera de confianza: opera sin contexto de sesion y con datos por tenant explicito.
/// Ver ADR docs/decisiones/0009.
/// </summary>
public sealed class FormWebhookService : IFormWebhookService
{
    // Etapa destino de todas las tarjetas de formularios web (Contactanos y PQRS).
    public const string PqrsStageName = "PQRS";

    // Ventana de dedup: un reenvio del mismo payload dentro de este lapso no crea tarjeta doble.
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);

    // Prefijo de campos reservados que NO se aceptan como FieldKey (van en campos nativos del Lead).
    // Aqui solo mapeamos claves conocidas, asi que ninguna reservada puede colarse desde el payload.

    private readonly IApplicationDbContext _db;
    private readonly ITenantApiService _api;
    private readonly ISecretProtector _secretProtector;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public FormWebhookService(
        IApplicationDbContext db,
        ITenantApiService api,
        ISecretProtector secretProtector,
        TimeProvider timeProvider,
        IAuditWriter audit)
    {
        _db = db;
        _api = api;
        _secretProtector = secretProtector;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<FormWebhookConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantFormWebhookConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        if (cfg is null) { return new FormWebhookConfigDto(tenantId, null, false, false, null); }
        return new FormWebhookConfigDto(tenantId, Decrypt(cfg.TokenEncrypted), cfg.IsEnabled, !string.IsNullOrEmpty(cfg.TokenEncrypted), cfg.LastUsedAt);
    }

    public async Task<FormWebhookConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantFormWebhookConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var isNew = cfg is null;
        if (cfg is null) { cfg = new TenantFormWebhookConfig { TenantId = tenantId, IsEnabled = true }; _db.TenantFormWebhookConfigs.Add(cfg); }

        var token = "vfw_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        cfg.TokenHash = Hash(token);
        cfg.TokenEncrypted = _secretProtector.Protect(token);

        _audit.Write(actorUserId, isNew ? "form-webhook.create" : "form-webhook.regenerate",
            nameof(TenantFormWebhookConfig), cfg.Id, previousValue: null, newValue: new { cfg.IsEnabled }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormWebhookConfigDto(tenantId, token, cfg.IsEnabled, true, cfg.LastUsedAt);
    }

    public async Task<FormWebhookConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantFormWebhookConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        if (cfg is null) { return null; }
        cfg.IsEnabled = enabled;
        _audit.Write(actorUserId, "form-webhook.toggle", nameof(TenantFormWebhookConfig), cfg.Id,
            previousValue: null, newValue: new { enabled }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormWebhookConfigDto(tenantId, Decrypt(cfg.TokenEncrypted), cfg.IsEnabled, true, cfg.LastUsedAt);
    }

    public async Task<FormWebhookResult> ProcessAsync(string token, string? contentType, string rawBody, CancellationToken cancellationToken = default)
    {
        // 1) Resolver tenant por el token de la URL (no se loggea el token).
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return new FormWebhookResult(401, false, Error: "Token invalido.");
        }
        var cfg = await _db.TenantFormWebhookConfigs.FirstOrDefaultAsync(c => c.TokenHash == Hash(token.Trim()), cancellationToken);
        if (cfg is null || !cfg.IsEnabled)
        {
            return new FormWebhookResult(401, false, Error: "Token invalido o deshabilitado.");
        }

        // 2) Parsear el cuerpo (form-urlencoded o JSON) a un diccionario de claves normalizadas.
        Dictionary<string, string> raw;
        try { raw = ParsePayload(contentType, rawBody); }
        catch { return new FormWebhookResult(400, false, Error: "Cuerpo no valido: se espera x-www-form-urlencoded o JSON."); }

        // 3) Mapear a campos nativos + configurables.
        var nombre = Cap(Get(raw, "nombre"), 200);
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return new FormWebhookResult(400, false, Error: "El campo 'nombre' es obligatorio.");
        }
        var telefono = Cap(Get(raw, "telefono"), 50);
        var email = Get(raw, "email");
        var asunto = Get(raw, "asunto");
        var mensaje = Get(raw, "mensaje");
        var tipo = NormalizeTipo(Get(raw, "tipo"));
        var pagina = Get(raw, "pagina");

        // 4) Idempotencia: mismo (tenant + payload) dentro de la ventana -> duplicado.
        var dedupHash = Hash($"{cfg.TenantId}|nombre={nombre}|tel={telefono}|email={email}|asunto={asunto}|mensaje={mensaje}|tipo={tipo}|pagina={pagina}");
        var now = _timeProvider.GetUtcNow();
        var windowStart = now - DedupWindow;
        var prior = await _db.FormWebhookEvents
            .Where(e => e.TenantId == cfg.TenantId && e.DedupHash == dedupHash && e.ReceivedAt >= windowStart)
            .OrderByDescending(e => e.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (prior is not null)
        {
            cfg.LastUsedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            return new FormWebhookResult(200, true, prior.LeadId, Duplicate: true);
        }

        // 5) Resolver la etapa destino segun el tipo (configurable en Configuracion de Empresa).
        //    Si no esta configurada, cae a "PQRS" (comportamiento por defecto).
        var stageName = await ResolverEtapaAsync(cfg.TenantId, tipo, cancellationToken);

        // 6) Asegurar la etapa + sus campos (idempotente, por entorno).
        await EnsureStageAndFieldsAsync(cfg.TenantId, stageName, cancellationToken);

        // 7) Crear la tarjeta enrutada a esa etapa.
        var fields = new Dictionary<string, JsonElement>();
        Put(fields, "email", email);
        Put(fields, "asunto", asunto);
        Put(fields, "mensaje", mensaje);
        Put(fields, "tipo", tipo);
        Put(fields, "pagina_origen", pagina);

        var req = new ApiCreateLeadRequest(nombre, telefono, null, null, null, fields, stageName);
        var result = await _api.CreateLeadAsync(cfg.TenantId, req, cancellationToken);
        if (!result.Ok || result.LeadId is null)
        {
            return new FormWebhookResult(400, false, Error: result.Error ?? "No se pudo crear la tarjeta.");
        }

        // 8) Registrar el evento (dedup) + actividad con origen web:{tipo}.
        _db.FormWebhookEvents.Add(new FormWebhookEvent
        {
            TenantId = cfg.TenantId,
            DedupHash = dedupHash,
            LeadId = result.LeadId,
            ReceivedAt = now
        });
        _db.LeadActivities.Add(new LeadActivity
        {
            TenantId = cfg.TenantId,
            LeadId = result.LeadId.Value,
            ActivityType = $"web:{tipo}",
            Description = $"Formulario web ({tipo}) recibido desde el sitio."
        });
        cfg.LastUsedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return new FormWebhookResult(201, true, result.LeadId);
    }

    /// <summary>
    /// Etapa destino segun el tipo de formulario. Lee la config del tenant (Configuracion de Empresa);
    /// si no esta configurada, cae a "PQRS". Contexto-less: por eso lee por TenantId con IgnoreQueryFilters.
    /// </summary>
    private async Task<string> ResolverEtapaAsync(Guid tenantId, string tipo, CancellationToken cancellationToken)
    {
        var key = string.Equals(tipo, "contacto", StringComparison.OrdinalIgnoreCase)
            ? ConfiguracionClinicaService.KeyEtapaFormContacto
            : ConfiguracionClinicaService.KeyEtapaFormPqrs;
        var cfg = await _db.TenantConfigurations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ConfigKey == key, cancellationToken);
        var v = cfg?.ConfigValue?.Trim();
        return string.IsNullOrWhiteSpace(v) ? PqrsStageName : v!;
    }

    /// <summary>
    /// Crea (si no existe) la etapa indicada del tenant y sus campos configurables. Idempotente: solo
    /// agrega lo que falte. Se ejecuta en la recepcion, sin depender de GUIDs por entorno.
    /// </summary>
    private async Task EnsureStageAndFieldsAsync(Guid tenantId, string stageName, CancellationToken cancellationToken)
    {
        var target = stageName.Trim().ToLowerInvariant();
        var stage = await _db.PipelineStages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Name.ToLower() == target, cancellationToken);
        if (stage is null)
        {
            // SortOrder alto: no se vuelve la etapa "por defecto" (la primera) del embudo.
            var maxOrder = await _db.PipelineStages.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync(cancellationToken) ?? 0;
            stage = new PipelineStage { TenantId = tenantId, Name = stageName.Trim(), SortOrder = maxOrder + 1 };
            _db.PipelineStages.Add(stage);
            await _db.SaveChangesAsync(cancellationToken); // necesitamos stage.Id para los campos
        }

        var existingKeys = await _db.PipelineFieldDefinitions.IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId)
            .Select(f => f.FieldKey)
            .ToListAsync(cancellationToken);
        var existing = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);

        var defs = new (string Key, string Label, PipelineFieldType Type, int Column, int SortOrder, string Description)[]
        {
            ("email",         "Correo",           PipelineFieldType.Text,     1, 1, "Correo del contacto (formulario web)."),
            ("asunto",        "Asunto",           PipelineFieldType.Text,     2, 2, "Asunto del mensaje (formulario web)."),
            ("mensaje",       "Mensaje",          PipelineFieldType.TextArea, 2, 3, "Mensaje / PQRS del ciudadano."),
            ("tipo",          "Tipo",             PipelineFieldType.Text,     1, 4, "Origen del formulario: pqrs o contacto."),
            ("pagina_origen", "Pagina de origen", PipelineFieldType.Text,     2, 5, "URL de la pagina que envio el formulario."),
        };

        var added = false;
        foreach (var d in defs)
        {
            if (existing.Contains(d.Key)) { continue; }
            _db.PipelineFieldDefinitions.Add(new PipelineFieldDefinition
            {
                TenantId = tenantId,
                StageId = stage.Id,
                FieldKey = d.Key,
                Label = d.Label,
                FieldType = d.Type,
                Column = d.Column,
                SortOrder = d.SortOrder,
                Description = d.Description
            });
            added = true;
        }
        if (added) { await _db.SaveChangesAsync(cancellationToken); }
    }

    // ---- Parsing -----------------------------------------------------------
    //
    // Formato OFICIAL soportado: Elementor Pro "Webhook" con "Advanced Data" = ON, que envia los
    // campos ANIDADOS como fields[<id>][value] (mas [id][type][title][raw_value][required]) y los
    // metadatos como meta[<clave>][value]. Leemos SIEMPRE fields[<id>][value].
    //
    // Tolerancia: si llega Advanced Data OFF (claves planas por id/etiqueta, p.ej. form_fields[nombre]
    // o "nombre") o un JSON, no revienta: esas claves caen al bucket "plano". El formato anidado gana
    // sobre el plano cuando ambos vienen.

    private static Dictionary<string, string> ParsePayload(string? contentType, string body)
    {
        var isJson = (contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false)
            || body.TrimStart().StartsWith('{');
        return isJson ? ParseJson(body) : ParseFormUrlEncoded(body);
    }

    private static Dictionary<string, string> ParseFormUrlEncoded(string body)
    {
        // Descomponemos SIN colapsar la clave, para distinguir el formato anidado (fields[<id>][value])
        // del plano (form_fields[<id>] o "nombre").
        var nested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);    // fields[<id>][value]
        var nestedRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // fields[<id>][raw_value]
        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);      // plano / form_fields[<id>]
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);      // meta[<clave>][value]

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var rawKey = eq >= 0 ? pair[..eq] : pair;
            var rawVal = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
            var key = (WebUtility.UrlDecode(rawKey) ?? string.Empty).Trim();
            if (key.Length == 0) { continue; }
            var val = WebUtility.UrlDecode(rawVal) ?? string.Empty;

            var (baseName, parts) = SplitKey(key);
            AbsorbPair(baseName, parts, val, nested, nestedRaw, flat, meta);
        }

        return MergeParsed(nested, nestedRaw, flat, meta);
    }

    // Parte "fields[nombre][value]" en base="fields" y partes=["nombre","value"]. Soporta corchetes
    // literales (como los manda Elementor). Una clave sin corchetes ("nombre") -> base="nombre", partes=[].
    private static (string Base, List<string> Parts) SplitKey(string key)
    {
        var parts = new List<string>();
        var open = key.IndexOf('[');
        if (open < 0) { return (key, parts); }
        var baseName = key[..open];
        var i = open;
        while (i < key.Length && key[i] == '[')
        {
            var close = key.IndexOf(']', i + 1);
            if (close < 0) { break; }
            parts.Add(key.Substring(i + 1, close - i - 1));
            i = close + 1;
        }
        return (baseName, parts);
    }

    private static void AbsorbPair(
        string baseName, List<string> parts, string val,
        Dictionary<string, string> nested, Dictionary<string, string> nestedRaw,
        Dictionary<string, string> flat, Dictionary<string, string> meta)
    {
        var b = baseName.Trim().ToLowerInvariant();

        // Metadatos del form en si (form[id], form[name]): no son campos.
        if (b == "form") { return; }

        // Formato oficial anidado: fields[<id>][value] / fields[<id>][raw_value].
        if (b == "fields" && parts.Count == 2)
        {
            var id = parts[0].Trim().ToLowerInvariant();
            if (id.Length == 0) { return; }
            var sub = parts[1].Trim().ToLowerInvariant();
            if (sub == "value") { nested[id] = val; }
            else if (sub == "raw_value") { nestedRaw[id] = val; }
            // [id][type][title][required] se ignoran a proposito.
            return;
        }

        // Metadatos de Elementor: meta[<clave>][value] (nos interesa page_url).
        if (b == "meta" && parts.Count == 2 && string.Equals(parts[1].Trim(), "value", StringComparison.OrdinalIgnoreCase))
        {
            var k = parts[0].Trim().ToLowerInvariant();
            if (k.Length > 0) { meta[k] = val; }
            return;
        }

        // Plano tolerante: form_fields[<id>] o fields[<id>] (un solo nivel) -> id.
        if ((b == "form_fields" || b == "fields") && parts.Count == 1)
        {
            var id = parts[0].Trim().ToLowerInvariant();
            if (id.Length > 0) { flat[id] = val; }
            return;
        }

        // Clave plana sin corchetes: "nombre", "email", ...
        if (parts.Count == 0 && b.Length > 0)
        {
            flat[b] = val;
        }
        // Cualquier otra forma con corchetes desconocidos se ignora (no rompe).
    }

    private static Dictionary<string, string> ParseJson(string body)
    {
        var nested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nestedRaw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            CollectJson(doc.RootElement, nested, nestedRaw, flat, meta);
        }
        return MergeParsed(nested, nestedRaw, flat, meta);
    }

    // Recorre el JSON soportando tanto el plano ({ "nombre": "..." }) como el anidado de Elementor
    // ({ "fields": { "nombre": { "value": "..." } }, "meta": { "page_url": { "value": "..." } } }).
    private static void CollectJson(
        JsonElement obj,
        Dictionary<string, string> nested, Dictionary<string, string> nestedRaw,
        Dictionary<string, string> flat, Dictionary<string, string> meta)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            var name = prop.Name.Trim().ToLowerInvariant();
            if (name == "form") { continue; }

            if (name == "fields" && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var f in prop.Value.EnumerateObject())
                {
                    var id = f.Name.Trim().ToLowerInvariant();
                    if (id.Length == 0) { continue; }
                    if (f.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (TryScalarProp(f.Value, "value", out var v)) { nested[id] = v; }
                        if (TryScalarProp(f.Value, "raw_value", out var rv)) { nestedRaw[id] = rv; }
                    }
                    else if (TryScalar(f.Value, out var sv)) { flat[id] = sv; }
                }
                continue;
            }

            if (name == "meta" && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var m in prop.Value.EnumerateObject())
                {
                    var k = m.Name.Trim().ToLowerInvariant();
                    if (k.Length == 0) { continue; }
                    if (m.Value.ValueKind == JsonValueKind.Object && TryScalarProp(m.Value, "value", out var mv)) { meta[k] = mv; }
                    else if (TryScalar(m.Value, out var msv)) { meta[k] = msv; }
                }
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                CollectJson(prop.Value, nested, nestedRaw, flat, meta); // objetos anidados desconocidos: aplanar
            }
            else if (TryScalar(prop.Value, out var s))
            {
                flat[name] = s;
            }
        }
    }

    private static bool TryScalar(JsonElement e, out string val)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: val = e.GetString() ?? string.Empty; return true;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False: val = e.ToString(); return true;
            default: val = string.Empty; return false;
        }
    }

    private static bool TryScalarProp(JsonElement obj, string prop, out string val)
    {
        if (obj.TryGetProperty(prop, out var e) && TryScalar(e, out val)) { return true; }
        val = string.Empty;
        return false;
    }

    // Combina los buckets en un diccionario final. El formato anidado (Advanced Data ON) gana sobre
    // el plano; raw_value solo cubre huecos de value; meta[page_url] alimenta "pagina".
    private static Dictionary<string, string> MergeParsed(
        Dictionary<string, string> nested, Dictionary<string, string> nestedRaw,
        Dictionary<string, string> flat, Dictionary<string, string> meta)
    {
        var dict = new Dictionary<string, string>(flat, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in nested) { dict[kv.Key] = kv.Value; }
        foreach (var kv in nestedRaw)
        {
            if (!dict.TryGetValue(kv.Key, out var v) || string.IsNullOrWhiteSpace(v)) { dict[kv.Key] = kv.Value; }
        }

        if (meta.TryGetValue("page_url", out var pageUrl) && !string.IsNullOrWhiteSpace(pageUrl)
            && (!dict.TryGetValue("pagina", out var pg) || string.IsNullOrWhiteSpace(pg)))
        {
            dict["pagina"] = pageUrl;
        }

        return dict;
    }

    // ---- Helpers -----------------------------------------------------------

    private static string? Get(Dictionary<string, string> dict, string key)
        => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string? Cap(string? value, int max)
        => value is null ? null : (value.Length <= max ? value : value[..max]);

    private static void Put(Dictionary<string, JsonElement> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = JsonSerializer.SerializeToElement(value);
        }
    }

    // Normaliza el tipo de formulario. Vacio -> "pqrs" (default seguro). Se acota para el ActivityType.
    private static string NormalizeTipo(string? raw)
    {
        var t = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0) { return "pqrs"; }
        return t.Length <= 20 ? t : t[..20];
    }

    private string? Decrypt(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) { return null; }
        try { return _secretProtector.Unprotect(enc); } catch { return null; }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

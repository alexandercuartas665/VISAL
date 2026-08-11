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

    private static Dictionary<string, string> ParsePayload(string? contentType, string body)
    {
        var isJson = (contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false)
            || body.TrimStart().StartsWith('{');
        return isJson ? ParseJson(body) : ParseFormUrlEncoded(body);
    }

    private static Dictionary<string, string> ParseFormUrlEncoded(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var rawKey = eq >= 0 ? pair[..eq] : pair;
            var rawVal = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
            var key = NormalizeKey(WebUtility.UrlDecode(rawKey) ?? string.Empty);
            if (key.Length == 0) { continue; }
            dict[key] = WebUtility.UrlDecode(rawVal) ?? string.Empty; // ultima gana
        }
        return dict;
    }

    private static Dictionary<string, string> ParseJson(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            FlattenJson(doc.RootElement, dict);
        }
        return dict;
    }

    // Aplana un objeto JSON un nivel: soporta payload plano y anidado (p.ej. { "form_fields": {..} }).
    private static void FlattenJson(JsonElement obj, Dictionary<string, string> dict)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    FlattenJson(prop.Value, dict);
                    break;
                case JsonValueKind.String:
                    dict[NormalizeKey(prop.Name)] = prop.Value.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    dict[NormalizeKey(prop.Name)] = prop.Value.ToString();
                    break;
                // arrays / null se ignoran
            }
        }
    }

    // Normaliza claves tipo "form_fields[nombre]" o "fields[email]" a "nombre" / "email".
    private static string NormalizeKey(string key)
    {
        key = key.Trim();
        var open = key.LastIndexOf('[');
        var close = key.LastIndexOf(']');
        if (open >= 0 && close > open)
        {
            key = key.Substring(open + 1, close - open - 1);
        }
        return key.Trim().ToLowerInvariant();
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

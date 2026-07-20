using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision.Ia;

public sealed class PreRevisionIaService : IPreRevisionIaService
{
    private readonly IApplicationDbContext _db;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;
    private readonly ISecretProtector _secretProtector;
    private readonly IRevisionMcpToolsService _tools;
    private readonly IRevisionClinicaService _revision;
    private readonly IRevisionPolicyService _policy;

    // Nombre de un AiAgentPrompt opcional que, si existe en el agente REVISOR CLINICO IA,
    // define el allow-list de tools como CSV en el Body. Si no existe, se usan las 9 tools.
    // Solucion pragmatica para RC5b sin migracion; RC5c evalua promover a columna dedicada.
    private const string AllowedToolsPromptName = "revision.allowed_tools";

    public PreRevisionIaService(
        IApplicationDbContext db,
        IAiProviderClient client,
        IAiUsageService usage,
        ISecretProtector secretProtector,
        IRevisionMcpToolsService tools,
        IRevisionClinicaService revision,
        IRevisionPolicyService policy)
    {
        _db = db;
        _client = client;
        _usage = usage;
        _secretProtector = secretProtector;
        _tools = tools;
        _revision = revision;
        _policy = policy;
    }

    public async Task<PreRevisionIaResult> EjecutarAsync(Guid revisionClinicaId, CancellationToken ct = default)
    {
        var revision = await _db.RevisionesClinica.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == revisionClinicaId, ct);
        if (revision is null) { return Err("El ciclo de revision no existe."); }

        var agent = await _db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Name == IPreRevisionIaService.AgenteNombre, ct);
        if (agent is null) { return Err($"No hay agente '{IPreRevisionIaService.AgenteNombre}' configurado para este tenant."); }
        if (!agent.IsActive) { return Err($"El agente '{IPreRevisionIaService.AgenteNombre}' esta apagado."); }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == agent.Provider, ct);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return Err($"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return Err("La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return Err($"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}).");
        }

        var allowed = await ResolveAllowedToolsAsync(agent.Id, ct);
        if (allowed.Count == 0) { return Err("El agente no tiene tools permitidas configuradas."); }

        var policy = await _policy.GetAsync(ct);

        var contexto = await _tools.ArmarContextoAsync(
            revision.HistoriaClinicaId,
            RevisionMcpToolNames.Todas,
            allowed,
            policy.VentanaAsignacionesRelacionadasDias,
            ct);

        var contextoJson = SerializarContexto(contexto);

        var systemPrompt = ArmarSystemPrompt(agent.SystemPrompt);
        var turns = new List<AiChatTurn>
        {
            new("user", "Analiza la siguiente Historia Clinica y emite tu veredicto. Responde SOLO con el JSON pedido en el system prompt.\n\n" + contextoJson),
        };

        var result = await CompleteWithRetryAsync(agent, apiKey, providerCfg.BaseUrl, model, systemPrompt, turns, ct);

        await _usage.RecordAsync(agent.Id, agent.Provider, model, result.InputTokens, result.OutputTokens, "revision", result.Ok, ct);

        if (!result.Ok || string.IsNullOrWhiteSpace(result.Text))
        {
            return Err(result.Error ?? "El proveedor no devolvio texto.") with { InputTokens = result.InputTokens, OutputTokens = result.OutputTokens };
        }

        VeredictoParsed parsed;
        try { parsed = ParseVeredicto(result.Text!); }
        catch (Exception ex)
        {
            return new PreRevisionIaResult(false, null, 0m, null, null, "No se pudo interpretar la respuesta del agente: " + ex.Message, result.InputTokens, result.OutputTokens);
        }

        var payload = new
        {
            resultado = parsed.Resultado.ToString(),
            confianza = parsed.Confianza,
            hallazgos = parsed.Hallazgos,
            umbral = policy.UmbralConfianza,
            tools_ok = contexto.InvocacionesExitosas.Count,
            tools_fail = contexto.InvocacionesFallidas.Count,
            tokens_in = result.InputTokens,
            tokens_out = result.OutputTokens,
        };
        var payloadJson = JsonSerializer.Serialize(payload);

        await _revision.RegistrarVeredictoAgenteAsync(
            new VeredictoAgenteCmd(revisionClinicaId, IPreRevisionIaService.AgenteNombre, parsed.Resultado, parsed.Nota, payloadJson),
            ct);

        // Ola 6 RC6c — Adopcion automatica del veredicto agente.
        // Reglas: solo aplica si (a) la policy del tenant lo permite explicitamente,
        // (b) el veredicto es Aprobado, (c) la confianza es >= umbral configurado.
        // Cualquier fallo aqui NO invalida el veredicto ya registrado — la HC queda
        // con el evento PreRevisionAgente y el revisor humano puede decidir.
        if (policy.AdopcionAutomaticaAgente
            && parsed.Resultado == RevisionResultado.Aprobado
            && parsed.Confianza >= policy.UmbralConfianza)
        {
            try
            {
                await _revision.AprobarPorSistemaAsync(new AprobarPorSistemaCmd(
                    revisionClinicaId,
                    IPreRevisionIaService.AgenteNombre,
                    parsed.Confianza,
                    policy.UmbralConfianza,
                    "Adopcion automatica por confianza >= umbral."), ct);
            }
            catch { /* Silencio: el veredicto quedo grabado, la adopcion es best-effort. */ }
        }

        return new PreRevisionIaResult(true, parsed.Resultado, parsed.Confianza, parsed.Nota, parsed.Hallazgos, null, result.InputTokens, result.OutputTokens);
    }

    /// <summary>
    /// Envuelve <see cref="IAiProviderClient.CompleteAsync"/> con retry+backoff
    /// exponencial (Ola 6 RC6b). Reintenta hasta 3 veces con delays 400ms/1200ms
    /// ante fallos transitorios: exception del cliente HTTP, timeouts, y errores
    /// del provider con mensaje que sugiere transitorio (429/5xx). Un
    /// <see cref="AiChatResult"/> con <c>Ok=false</c> pero motivo permanente
    /// (401/403, "invalid api key", "unauthorized") NO se reintenta.
    /// </summary>
    private async Task<AiChatResult> CompleteWithRetryAsync(
        AiAgent agent, string apiKey, string? baseUrl, string model,
        string systemPrompt, IReadOnlyList<AiChatTurn> turns, CancellationToken ct)
    {
        var delays = new[] { TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200) };
        AiChatResult? last = null;
        Exception? lastEx = null;

        for (var intento = 0; intento <= delays.Length; intento++)
        {
            try
            {
                var r = await _client.CompleteAsync(agent.Provider, apiKey, baseUrl, model, systemPrompt, turns, ct);
                if (r.Ok) { return r; }
                last = r;
                if (!EsErrorTransitorio(r.Error)) { return r; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastEx = ex;
            }

            if (intento < delays.Length)
            {
                try { await Task.Delay(delays[intento], ct); }
                catch (OperationCanceledException) { throw; }
            }
        }

        // Se agotaron los intentos: devolvemos el ultimo estado conocido.
        if (last is not null) { return last; }
        return new AiChatResult(false, null, lastEx?.Message ?? "El proveedor no respondio tras varios intentos.");
    }

    private static bool EsErrorTransitorio(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) { return true; }
        var low = error.ToLowerInvariant();
        // No reintentamos si la config del provider es la que esta mal — reintentarlo
        // solo quema tokens y hace ruido en el log.
        if (low.Contains("unauthorized") || low.Contains("401")
            || low.Contains("forbidden") || low.Contains("403")
            || low.Contains("invalid api key") || low.Contains("api key")) { return false; }
        return low.Contains("timeout") || low.Contains("timed out")
            || low.Contains("429") || low.Contains("rate limit")
            || low.Contains("500") || low.Contains("502") || low.Contains("503") || low.Contains("504")
            || low.Contains("transient") || low.Contains("temporarily");
    }

    private async Task<IReadOnlyList<string>> ResolveAllowedToolsAsync(Guid agentId, CancellationToken ct)
    {
        // Ola 8 RC8a — solo columna dedicada `AiAgent.AllowedToolsCsv`.
        // El fallback legacy a `AiAgentPrompt` name=revision.allowed_tools fue eliminado.
        // La migracion `MigrateAllowedToolsSeedsToColumn` (Ola 8) trasladó los datos
        // seedeados por Ola 5c a la columna dedicada. Si `AllowedToolsCsv` esta null
        // o vacio, se usan las 9 tools por defecto.
        var csv = await _db.AiAgents.AsNoTracking()
            .Where(a => a.Id == agentId)
            .Select(a => a.AllowedToolsCsv)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(csv)) { return RevisionMcpToolNames.Todas; }

        var raw = csv!.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = raw.Where(x => RevisionMcpToolNames.Todas.Contains(x)).Distinct().ToList();
        return valid.Count == 0 ? Array.Empty<string>() : valid;
    }

    private static string ArmarSystemPrompt(string agentSystemPrompt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentSystemPrompt))
        {
            sb.AppendLine(agentSystemPrompt);
            sb.AppendLine();
        }
        sb.AppendLine("Eres el agente REVISOR CLINICO IA de una IPS colombiana. Tu unica salida es un JSON valido con este schema exacto:");
        sb.AppendLine("{");
        sb.AppendLine("  \"resultado\": \"Aprobado\" | \"Rechazado\" | \"Neutral\",");
        sb.AppendLine("  \"confianza\": number entre 0 y 1,");
        sb.AppendLine("  \"nota\": \"cadena breve en espanol\",");
        sb.AppendLine("  \"hallazgos\": [\"observacion 1\", \"observacion 2\"]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Basate SOLO en el contexto que te llega. No inventes datos ni condiciones.");
        sb.AppendLine("- 'Aprobado' = HC completa, congruente, sin faltantes clinicos evidentes.");
        sb.AppendLine("- 'Rechazado' = falta un dato clinico critico, incongruencia grave o riesgo clinico.");
        sb.AppendLine("- 'Neutral' = evidencia insuficiente para decidir.");
        sb.AppendLine("- Devuelve JSON puro. Nada de markdown, ni prefijos, ni sufijos, ni ```json```.");
        return sb.ToString();
    }

    private static string SerializarContexto(HistoriaClinicaContextoIa contexto)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Contexto clinico cargado por tools MCP de solo lectura:");
        sb.AppendLine();
        foreach (var inv in contexto.InvocacionesExitosas)
        {
            sb.AppendLine("### tool " + inv.ToolName);
            sb.AppendLine(inv.JsonPayload ?? "null");
            sb.AppendLine();
        }
        if (contexto.InvocacionesFallidas.Count > 0)
        {
            sb.AppendLine("### tools con error");
            foreach (var f in contexto.InvocacionesFallidas)
            {
                sb.AppendLine("- " + f.ToolName + ": " + (f.Error ?? "(sin detalle)"));
            }
        }
        return sb.ToString();
    }

    private sealed record VeredictoParsed(RevisionResultado Resultado, decimal Confianza, string? Nota, IReadOnlyList<string> Hallazgos);

    private static VeredictoParsed ParseVeredicto(string texto)
    {
        var json = ExtraerJson(texto);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var resultadoStr = root.TryGetProperty("resultado", out var rEl) ? rEl.GetString() : "Neutral";
        var resultado = ParseResultado(resultadoStr);

        decimal confianza = 0m;
        if (root.TryGetProperty("confianza", out var cEl))
        {
            if (cEl.ValueKind == JsonValueKind.Number) { confianza = cEl.GetDecimal(); }
            else if (cEl.ValueKind == JsonValueKind.String && decimal.TryParse(cEl.GetString(), out var d)) { confianza = d; }
        }
        if (confianza < 0m) { confianza = 0m; }
        if (confianza > 1m) { confianza = 1m; }

        var nota = root.TryGetProperty("nota", out var nEl) ? nEl.GetString() : null;

        var hallazgos = new List<string>();
        if (root.TryGetProperty("hallazgos", out var hEl) && hEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in hEl.EnumerateArray())
            {
                var s = it.ValueKind == JsonValueKind.String ? it.GetString() : it.ToString();
                if (!string.IsNullOrWhiteSpace(s)) { hallazgos.Add(s!.Trim()); }
            }
        }

        return new VeredictoParsed(resultado, confianza, nota, hallazgos);
    }

    private static RevisionResultado ParseResultado(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return RevisionResultado.Neutral; }
        var norm = s.Trim();
        if (Enum.TryParse<RevisionResultado>(norm, ignoreCase: true, out var val)) { return val; }
        return RevisionResultado.Neutral;
    }

    private static string ExtraerJson(string texto)
    {
        // Si el modelo devuelve el JSON dentro de una fence ```json ... ``` o texto adicional,
        // tomamos el primer bloque { ... } balanceado. Es defensivo pero simple.
        var start = texto.IndexOf('{');
        var end = texto.LastIndexOf('}');
        if (start < 0 || end <= start) { return texto; }
        return texto.Substring(start, end - start + 1);
    }

    private static PreRevisionIaResult Err(string msg)
        => new(false, null, 0m, null, null, msg, 0, 0);
}

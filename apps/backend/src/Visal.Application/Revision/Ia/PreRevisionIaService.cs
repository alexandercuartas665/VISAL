using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Forms;
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

    public async Task<PreRevisionIaResult> EjecutarAsync(Guid revisionClinicaId, Guid? agenteId = null, CancellationToken ct = default)
    {
        var revision = await _db.RevisionesClinica.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == revisionClinicaId, ct);
        if (revision is null) { return Err("El ciclo de revision no existe."); }

        // Resolucion del agente:
        //   - Si viene agenteId (usuario eligio en el selector) buscamos por Id.
        //     El global query filter por tenant garantiza que no se pueda usar
        //     un agente de otro tenant aunque el Id se conozca.
        //   - Sin agenteId (triggers automaticos, botones legacy) caemos al
        //     canonico REVISOR CLINICO IA por nombre.
        AiAgent? agent;
        if (agenteId is Guid aid)
        {
            agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid, ct);
            if (agent is null) { return Err("El agente seleccionado no existe o no pertenece a este tenant."); }
            if (!agent.IsActive) { return Err($"El agente '{agent.Name}' esta apagado."); }
        }
        else
        {
            agent = await _db.AiAgents.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Name == IPreRevisionIaService.AgenteNombre, ct);
            if (agent is null) { return Err($"No hay agente '{IPreRevisionIaService.AgenteNombre}' configurado para este tenant."); }
            if (!agent.IsActive) { return Err($"El agente '{IPreRevisionIaService.AgenteNombre}' esta apagado."); }
        }

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
            await GuardarAgenteLogAsync(revision, agent, model, systemPrompt, contextoJson, contexto,
                ok: false, error: result.Error ?? "El proveedor no devolvio texto.", resultado: null, confianza: null,
                result.InputTokens, result.OutputTokens, result.Text, ct);
            return Err(result.Error ?? "El proveedor no devolvio texto.") with { InputTokens = result.InputTokens, OutputTokens = result.OutputTokens };
        }

        VeredictoParsed parsed;
        try { parsed = ParseVeredicto(result.Text!); }
        catch (Exception ex)
        {
            await GuardarAgenteLogAsync(revision, agent, model, systemPrompt, contextoJson, contexto,
                ok: false, error: "No se pudo interpretar la respuesta del agente: " + ex.Message, resultado: null, confianza: null,
                result.InputTokens, result.OutputTokens, result.Text, ct);
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

        // Log completo del agente (que recibio, que proceso hizo, que respondio) para el
        // panel "Log del agente". Best-effort: nunca rompe la revision ya registrada.
        await GuardarAgenteLogAsync(revision, agent, model, systemPrompt, contextoJson, contexto,
            ok: true, error: null, resultado: parsed.Resultado.ToString(), confianza: parsed.Confianza,
            result.InputTokens, result.OutputTokens, result.Text, ct,
            nota: parsed.Nota, hallazgos: parsed.Hallazgos);

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
        sb.AppendLine("  \"nota\": \"justificacion clara y especifica de tu decision, en 2 a 4 frases\",");
        sb.AppendLine("  \"hallazgos\": [\"hallazgo concreto 1\", \"hallazgo concreto 2\"]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Basate SOLO en el contexto que te llega. No inventes datos ni condiciones.");
        sb.AppendLine("- 'Aprobado' = HC completa, congruente, sin faltantes clinicos evidentes.");
        sb.AppendLine("- 'Rechazado' = falta un dato clinico critico, incongruencia grave o riesgo clinico.");
        sb.AppendLine("- 'Neutral' = evidencia insuficiente para decidir.");
        sb.AppendLine("- La 'nota' es tu JUSTIFICACION, no una etiqueta generica: en 2 a 4 frases explica POR QUE");
        sb.AppendLine("  llegaste a ese resultado, CITANDO los datos concretos de la HC que lo sustentan (nombres de");
        sb.AppendLine("  campos, fechas, valores, ordenes, incongruencias). Si RECHAZAS, di EXACTAMENTE que falta o");
        sb.AppendLine("  que esta mal, en que parte de la HC, y POR QUE impide aprobar o facturar.");
        sb.AppendLine("- Cada 'hallazgo' es un punto CONCRETO y verificable (no generalidades): nombra el campo o dato");
        sb.AppendLine("  y el problema puntual. Si Apruebas, puedes listar lo que validaste. Ordena los mas graves primero.");
        sb.AppendLine("- Se especifico y evita frases de relleno. La nota debe permitirle a un auditor humano entender");
        sb.AppendLine("  tu decision sin volver a leer toda la HC.");
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

    /// <summary>Persiste el log completo de una corrida del agente (best-effort). Nunca lanza.</summary>
    private async Task GuardarAgenteLogAsync(
        RevisionClinica revision, AiAgent agent, string model,
        string? systemPrompt, string? contextoEnviado, HistoriaClinicaContextoIa? contexto,
        bool ok, string? error, string? resultado, decimal? confianza,
        int tokensIn, int tokensOut, string? respuestaCruda, CancellationToken ct,
        string? nota = null, IReadOnlyList<string>? hallazgos = null)
    {
        try
        {
            var camposJson = await BuildCamposLegiblesAsync(revision.HistoriaClinicaId, ct);
            string? toolsJson = null;
            if (contexto is not null)
            {
                var tools = contexto.InvocacionesExitosas
                    .Select(i => new { tool = i.ToolName, ok = true, tokens = i.EstimatedTokens, error = (string?)null, payload = i.JsonPayload })
                    .Concat(contexto.InvocacionesFallidas
                        .Select(i => new { tool = i.ToolName, ok = false, tokens = i.EstimatedTokens, error = i.Error, payload = i.JsonPayload }))
                    .ToList();
                toolsJson = JsonSerializer.Serialize(tools, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            _db.RevisionAgenteLogs.Add(new RevisionAgenteLog
            {
                TenantId = revision.TenantId,
                RevisionClinicaId = revision.Id,
                HistoriaClinicaId = revision.HistoriaClinicaId,
                AgenteCodigo = string.IsNullOrWhiteSpace(agent.Name) ? IPreRevisionIaService.AgenteNombre : agent.Name,
                Proveedor = agent.Provider.ToString(),
                Modelo = model,
                Ok = ok,
                Error = error,
                Resultado = resultado,
                Confianza = confianza,
                TokensIn = tokensIn,
                TokensOut = tokensOut,
                Nota = nota,
                HallazgosJson = hallazgos is { Count: > 0 }
                    ? JsonSerializer.Serialize(hallazgos, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
                    : null,
                SystemPrompt = systemPrompt,
                ContextoEnviado = contextoEnviado,
                CamposLegiblesJson = camposJson,
                ToolsJson = toolsJson,
                RespuestaCruda = respuestaCruda,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch { /* el log es best-effort; nunca romper la revision */ }
    }

    /// <summary>HC legible como JSON { "label del campo": "contenido" } cruzando ValoresJson con el
    /// esquema del formato. Solo para mostrar en el log (no es lo que se le envia al agente).</summary>
    private async Task<string?> BuildCamposLegiblesAsync(Guid hcId, CancellationToken ct)
    {
        var hc = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new { h.ValoresJson, h.FormDefinitionId })
            .FirstOrDefaultAsync(ct);
        if (hc is null || string.IsNullOrWhiteSpace(hc.ValoresJson)) { return null; }
        var schemaJson = await _db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId)
            .Select(f => f.SchemaJson)
            .FirstOrDefaultAsync(ct);
        return CamposLegibles(hc.ValoresJson, schemaJson);
    }

    private static string? CamposLegibles(string valoresJson, string? schemaJson)
    {
        Dictionary<string, string?>? v;
        try { v = JsonSerializer.Deserialize<Dictionary<string, string?>>(valoresJson); }
        catch { return null; }
        if (v is null || v.Count == 0) { return null; }

        var keyToLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                var schema = FormSchema.FromJson(schemaJson);
                if (schema?.Children is not null)
                {
                    void Recurse(IEnumerable<FormNode> nodes)
                    {
                        foreach (var n in nodes)
                        {
                            if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                            if (!string.IsNullOrWhiteSpace(n.Name) && !string.IsNullOrWhiteSpace(n.Label))
                            {
                                keyToLabel[n.Name!] = n.Label!;
                            }
                        }
                    }
                    Recurse(schema.Children);
                }
                if (schema?.Header?.Campos is { Count: > 0 } campos)
                {
                    foreach (var c in campos)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Label)) { keyToLabel["hdr:" + c.Id] = c.Label!; }
                    }
                }
            }
            catch { /* sin labels, se usan las claves crudas */ }
        }

        var outMap = new Dictionary<string, object?>();
        foreach (var kv in v)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) { continue; }
            if ((kv.Key ?? "").Contains("firma", StringComparison.OrdinalIgnoreCase)) { continue; } // firmas base64
            var label = keyToLabel.TryGetValue(kv.Key!, out var lbl) && !string.IsNullOrWhiteSpace(lbl) ? lbl : kv.Key!;
            var finalLabel = label;
            var i = 2;
            while (outMap.ContainsKey(finalLabel)) { finalLabel = label + " (" + i++ + ")"; }
            outMap[finalLabel] = kv.Value;
        }
        if (outMap.Count == 0) { return null; }
        return JsonSerializer.Serialize(outMap, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
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

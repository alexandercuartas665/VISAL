using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class AiInferenceService : IAiInferenceService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;

    public AiInferenceService(IApplicationDbContext db, ISecretProtector secretProtector, IAiProviderClient client, IAiUsageService usage)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _usage = usage;
    }

    public async Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return new AiChatResult(false, null, "El agente no existe."); }

        // La cuenta del proveedor (API key, modelo, base url) la define el Super Admin (config global).
        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new AiChatResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return new AiChatResult(false, null, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        if (turns.Count == 0) { return new AiChatResult(false, null, "Escribe un mensaje para probar el agente."); }

        // Control de cupo: si el plan tiene limite duro y ya se agoto el mes, no se ejecuta.
        // (Las consultas a BD se hacen en serie sobre el DbContext scoped: cupo -> prompt -> proveedor.)
        var quota = await _usage.GetQuotaAsync(cancellationToken);
        if (quota.Exceeded && quota.Hard)
        {
            return new AiChatResult(false, null, $"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}). Actualiza tu plan para seguir usando los agentes.");
        }

        // Recursos del agente (todos los tipos): se usan para componer el prompt y para resolver adjuntos.
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .OrderBy(r => r.SortOrder)
            .Select(r => new AiChatAttachment(r.Name, r.ResourceType, r.FileUrl, r.FileName, r.Detail))
            .ToListAsync(cancellationToken);

        var systemPrompt = await BuildSystemPrompt(agentId, systemPromptOverride ?? agent.SystemPrompt, resources, cancellationToken);

        var result = await _client.CompleteAsync(agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt, turns, cancellationToken);

        // Todo consumo de IA del tenant pasa por el modulo de tokens (incluido el chat de prueba).
        if (result.Ok)
        {
            await _usage.RecordAsync(agent.Id, agent.Provider, model, result.InputTokens, result.OutputTokens, "test", true, cancellationToken);
        }

        // Entrega de recursos: el modelo marca [[enviar: Nombre]] y aqui adjuntamos el recurso (archivo o texto).
        if (result.Ok && !string.IsNullOrEmpty(result.Text))
        {
            var (cleanText, attachments) = ExtractAttachments(result.Text!, resources);
            return result with { Text = cleanText, Attachments = attachments };
        }

        return result;
    }

    // Arma el prompt del sistema: prompt base + enrutador (con {{recurso}} expandido) + catalogo de recursos.
    private async Task<string> BuildSystemPrompt(Guid agentId, string basePrompt, IReadOnlyList<AiChatAttachment> resources, CancellationToken ct)
    {
        var sb = new StringBuilder(ExpandResourceRefs(basePrompt, resources));

        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == agentId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Name, p.Rule, p.Body })
            .ToListAsync(ct);
        if (prompts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Enrutador de prompts: evalua el mensaje del cliente y, si coincide alguna de estas reglas, sigue PRIMERO las instrucciones del prompt correspondiente (ademas del comportamiento base). Si ninguna aplica, responde con el comportamiento base.");
            foreach (var p in prompts)
            {
                sb.AppendLine();
                sb.AppendLine($"### Prompt \"{p.Name}\"");
                sb.AppendLine($"Regla (cuando usarlo): {(string.IsNullOrWhiteSpace(p.Rule) ? "(sin regla; usar a criterio)" : p.Rule)}");
                sb.AppendLine($"Instrucciones: {ExpandResourceRefs(p.Body, resources)}");
            }
        }

        if (resources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Recursos disponibles. REGLA IMPORTANTE: cuando vayas a comunicar el contenido de un recurso (precios, politicas, textos, imagenes, videos, PDF, ubicacion), NO lo reescribas ni lo resumas: entregalo EXACTO incluyendo en tu respuesta el marcador [[enviar: Nombre exacto del recurso]]. El sistema agregara el contenido o el archivo tal cual. Puedes acompanarlo con una frase breve, pero el contenido del recurso lo entrega el marcador.");
            foreach (var r in resources)
            {
                var kind = r.ResourceType == AgentResourceType.Text ? "Texto" : r.ResourceType.ToString();
                var desc = string.IsNullOrWhiteSpace(r.Detail) ? "archivo" : r.Detail;
                sb.AppendLine($"- ({kind}) {r.Name}: {desc}  -> entregar con [[enviar: {r.Name}]]");
            }
        }

        return sb.ToString();
    }

    // Reemplaza {{nombre}} por la instruccion de entregar ese recurso de forma EXACTA (sin degradarlo).
    private static string ExpandResourceRefs(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{")) { return text; }
        return Regex.Replace(text, @"\{\{\s*([^}]+?)\s*\}\}", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is null) { return m.Value; }
            return $"el recurso \"{res.Name}\" (entregalo EXACTO incluyendo el marcador [[enviar: {res.Name}]]; el sistema agrega su contenido, no lo reescribas)";
        });
    }

    // Extrae los marcadores [[enviar: Nombre]], los quita del texto y devuelve los recursos a adjuntar.
    private static (string, IReadOnlyList<AiChatAttachment>) ExtractAttachments(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        var attachments = new List<AiChatAttachment>();
        var clean = Regex.Replace(text, @"\[\[\s*enviar\s*:\s*([^\]]+?)\s*\]\]", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is not null && attachments.All(a => a.Name != res.Name)) { attachments.Add(res); }
            return string.Empty;
        }, RegexOptions.IgnoreCase);

        // Limpia espacios/lineas sobrantes que deja el marcador.
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n").Trim();
        return (clean, attachments);
    }

    private static AiChatAttachment? FindResource(IReadOnlyList<AiChatAttachment> resources, string name)
    {
        var key = Normalize(name);
        return resources.FirstOrDefault(r => Normalize(r.Name) == key);
    }

    // Normaliza para comparar nombres: minusculas y sin acentos (asi "politica" == "{{politica}}").
    private static string Normalize(string s)
    {
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}

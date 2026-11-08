using System.Globalization;
using System.Text;
using System.Text.Json;
using Visal.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public interface IQuoteRenderService
{
    /// <summary>Renderiza el HTML de la cotizacion de un lead, reemplazando los marcadores de la plantilla
    /// (la predeterminada de la agencia, o la indicada) con los datos del lead. Sin contexto de sesion.</summary>
    Task<string?> RenderHtmlAsync(Guid leadId, Guid? templateId = null, CancellationToken cancellationToken = default);
}

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class QuoteRenderService : IQuoteRenderService
{
    private readonly IApplicationDbContextFactory _dbFactory;

    public QuoteRenderService(IApplicationDbContextFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<string?> RenderHtmlAsync(Guid leadId, Guid? templateId = null, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var lead = await db.Leads.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == lead.TenantId, cancellationToken);

        var template = templateId is Guid tid
            ? await db.QuoteTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tid && t.TenantId == lead.TenantId, cancellationToken)
            : null;
        template ??= await db.QuoteTemplates.IgnoreQueryFilters()
            .Where(t => t.TenantId == lead.TenantId)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (template is null)
        {
            return $"<html><body style='font-family:sans-serif;padding:40px;color:#555'>La agencia aun no tiene una plantilla de cotizacion configurada.</body></html>";
        }

        var sb = new StringBuilder(template.HtmlContent);
        sb.Replace("{{agencyName}}", Esc(tenant?.Name ?? "Agencia"));
        sb.Replace("{{contactName}}", Esc(lead.ContactName ?? ""));
        sb.Replace("{{contactPhone}}", Esc(lead.ContactPhone ?? ""));
        sb.Replace("{{destination}}", Esc(lead.Destination ?? ""));
        sb.Replace("{{currency}}", Esc(lead.Currency ?? "COP"));
        sb.Replace("{{estimatedValue}}", lead.EstimatedValue is decimal v ? v.ToString("#,##0", CultureInfo.InvariantCulture) : "");
        sb.Replace("{{date}}", DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

        // Campos del embudo: {{campo.clave}} -> valor del lead.
        var fields = ParseFields(lead.FieldValuesJson);
        var result = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\{\{campo\.([a-zA-Z0-9_]+)\}\}", m =>
            fields.TryGetValue(m.Groups[1].Value, out var val) ? Esc(val) : "");
        return result;
    }

    private static Dictionary<string, string> ParseFields(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) { return dict; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                dict[p.Name] = FormatValue(p.Value);
            }
        }
        catch { /* json invalido: sin campos */ }
        return dict;
    }

    // Convierte el valor guardado a texto legible: escalar (con miles si es numero), arreglo de textos,
    // o arreglo de objetos {d,v} (detalle: valor).
    private static string FormatValue(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString() ?? "";
            var t = s.TrimStart();
            if (t.StartsWith("[")) { return FormatArray(s); }
            return FormatScalar(s);
        }
        if (el.ValueKind is JsonValueKind.Number) { return FormatScalar(el.GetRawText()); }
        if (el.ValueKind == JsonValueKind.Array) { return FormatArrayElement(el); }
        return el.GetRawText();
    }

    private static string FormatArray(string raw)
    {
        try { using var doc = JsonDocument.Parse(raw); return FormatArrayElement(doc.RootElement); }
        catch { return raw; }
    }

    private static string FormatArrayElement(JsonElement arr)
    {
        var parts = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                var d = el.TryGetProperty("d", out var dp) && dp.ValueKind == JsonValueKind.String ? dp.GetString() : null;
                var vv = el.TryGetProperty("v", out var vp) ? (vp.ValueKind == JsonValueKind.String ? vp.GetString() : vp.GetRawText()) : null;
                parts.Add(string.IsNullOrWhiteSpace(d) ? FormatScalar(vv ?? "") : $"{d}: {FormatScalar(vv ?? "")}");
            }
            else { parts.Add(FormatScalar(el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText())); }
        }
        return string.Join(", ", parts);
    }

    private static string FormatScalar(string s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n == Math.Truncate(n) && Math.Abs(n) >= 1000
            ? n.ToString("#,##0", CultureInfo.InvariantCulture)
            : s;

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

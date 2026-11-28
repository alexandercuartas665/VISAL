using System.Text.Encodings.Web;
using System.Text.Json;

namespace Visal.Application.Revision.Ia;

/// <summary>
/// IMP-73: el agente separa los HALLAZGOS (lo que hay que corregir) de las OBSERVACIONES (menores o
/// administrativas, no impiden aprobar). Sin columna nueva: las observaciones viajan en la misma
/// lista de <c>revision_agente_logs.hallazgos_json</c> con el prefijo <see cref="PrefijoObservacion"/>.
/// Todo lector de esa columna debe separar con <see cref="Separar"/>.
/// </summary>
public static class HallazgosIa
{
    public const string PrefijoObservacion = "[Observacion] ";

    private static readonly JsonSerializerOptions Opts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool EsObservacion(string? texto)
        => texto is not null && texto.StartsWith(PrefijoObservacion, StringComparison.Ordinal);

    /// <summary>Hallazgos + observaciones (con prefijo) como JSON; null si no hay nada.</summary>
    public static string? Serializar(IReadOnlyList<string>? hallazgos, IReadOnlyList<string>? observaciones)
    {
        var todos = new List<string>();
        if (hallazgos is not null) { todos.AddRange(hallazgos.Where(h => !string.IsNullOrWhiteSpace(h))); }
        if (observaciones is not null)
        {
            todos.AddRange(observaciones.Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => EsObservacion(o) ? o : PrefijoObservacion + o.Trim()));
        }
        return todos.Count == 0 ? null : JsonSerializer.Serialize(todos, Opts);
    }

    /// <summary>Lee la lista guardada (JSON de strings; otro tipo de elemento se toma como texto crudo).</summary>
    public static List<string> Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new List<string>(); }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return new List<string>(); }
            return doc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
        }
        catch (JsonException) { return new List<string>(); }
    }

    /// <summary>Separa una lista mezclada en (hallazgos, observaciones sin prefijo).</summary>
    public static (List<string> Hallazgos, List<string> Observaciones) Separar(IEnumerable<string>? items)
    {
        var h = new List<string>();
        var o = new List<string>();
        foreach (var s in items ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(s)) { continue; }
            if (EsObservacion(s)) { o.Add(s[PrefijoObservacion.Length..].Trim()); }
            else { h.Add(s); }
        }
        return (h, o);
    }

    public static (List<string> Hallazgos, List<string> Observaciones) Separar(string? json) => Separar(Leer(json));
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Modelo del esquema del disenador de formularios (se serializa a FormDefinition.SchemaJson).
/// Arbol de dos niveles: la raiz contiene secciones y/o campos; una seccion contiene campos.
/// </summary>
public sealed class FormSchema
{
    [JsonPropertyName("children")]
    public List<FormNode> Children { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static FormSchema FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new FormSchema();
        }
        try
        {
            return JsonSerializer.Deserialize<FormSchema>(json, JsonOptions) ?? new FormSchema();
        }
        catch
        {
            return new FormSchema();
        }
    }
}

/// <summary>Un nodo del arbol: una seccion (contenedor) o un campo.</summary>
public sealed class FormNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>"section" | "field".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "field";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    // ── Seccion ──
    [JsonPropertyName("children")]
    public List<FormNode>? Children { get; set; }

    // ── Campo ──
    /// <summary>text | number | email | date | textarea | select | autocomplete | calculated.</summary>
    [JsonPropertyName("fieldType")]
    public string? FieldType { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("widthColumns")]
    public int WidthColumns { get; set; } = 12;

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    // ── Calculado ──
    [JsonPropertyName("formula")]
    public string? Formula { get; set; }

    // ── Lista / autocompletar (origen de datos) ──
    /// <summary>Clave de catalogo: cie11, cups, medicamentos, profesionales, ips, generos, estatico.</summary>
    [JsonPropertyName("catalog")]
    public string? Catalog { get; set; }

    /// <summary>Opciones fijas cuando catalog = "estatico".</summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    public bool IsSection => Type == "section";
}

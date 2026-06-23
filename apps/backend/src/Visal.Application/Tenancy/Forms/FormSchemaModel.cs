using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Modelo del esquema del disenador de formularios (se serializa a FormDefinition.SchemaJson).
/// Arbol de dos niveles: la raiz contiene secciones y/o campos; una seccion contiene campos.
/// </summary>
public sealed class FormSchema
{
    [JsonPropertyName("header")]
    public FormHeader? Header { get; set; }

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

    /// <summary>"section" | "field" | "text".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "field";

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    // ── Seccion ──
    [JsonPropertyName("children")]
    public List<FormNode>? Children { get; set; }

    // ── Bloque de texto (Type = "text") ──
    /// <summary>heading | subheading | paragraph.</summary>
    [JsonPropertyName("textStyle")]
    public string? TextStyle { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    // ── Campo ──
    /// <summary>text | number | email | date | textarea | select | autocomplete | calculated | table.</summary>
    [JsonPropertyName("fieldType")]
    public string? FieldType { get; set; }

    // ── Tabla repetible (fieldType = "table") ──
    [JsonPropertyName("columns")]
    public List<FormColumn>? Columns { get; set; }

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

    // ── Tabla con filas pre-semilladas (FieldType = "table") ──
    /// <summary>
    /// Filas iniciales que ya vienen rellenadas. Cada fila es una lista paralela
    /// a Columns; null o vacio = celda editable, valor = texto fijo (no editable).
    /// Util para tablas matriciales tipo escala/test (TEST MOVILIDAD ARTICULAR,
    /// FUERZA MUSCULAR MRC, etc.).
    /// </summary>
    [JsonPropertyName("seedRows")]
    public List<List<string?>>? SeedRows { get; set; }

    /// <summary>
    /// Si true, oculta el boton "+ Agregar fila" para que la tabla quede limitada
    /// a las filas semilla. Por defecto false (permite agregar).
    /// </summary>
    [JsonPropertyName("lockRows")]
    public bool LockRows { get; set; }

    public bool IsSection => Type == "section";
    public bool IsText => Type == "text";
    public bool IsTable => Type == "field" && FieldType == "table";
}

/// <summary>Encabezado institucional del formato (logo, institucion, titulo y campos de cabecera).</summary>
public sealed class FormHeader
{
    [JsonPropertyName("institucion")]
    public string? Institucion { get; set; }

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; }

    /// <summary>Titulo del documento. Si esta vacio se usa el nombre del formulario.</summary>
    [JsonPropertyName("titulo")]
    public string? Titulo { get; set; }

    /// <summary>URL del logo (en /uploads/forms). Si esta vacio se usa el icono por defecto.</summary>
    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    /// <summary>Campos de cabecera personalizables (ej. No Historia, Consecutivo, Ciudad y Fecha).</summary>
    [JsonPropertyName("campos")]
    public List<FormHeaderField> Campos { get; set; } = new();

    public static FormHeader Default() => new()
    {
        Institucion = "IPS VISAL RT",
        Tagline = "Atencion Humana, Agil y Oportuna",
        Titulo = "",
        Campos = new()
        {
            new() { Label = "No Historia" },
            new() { Label = "Consecutivo" },
            new() { Label = "Ciudad y Fecha" }
        }
    };
}

/// <summary>Campo de cabecera (solo etiqueta; el valor se diligencia al usar el formato).</summary>
public sealed class FormHeaderField
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("label")]
    public string Label { get; set; } = "Campo";
}

/// <summary>Columna de una tabla repetible.</summary>
public sealed class FormColumn
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("label")]
    public string Label { get; set; } = "Columna";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>text | number | date | select | autocomplete.</summary>
    [JsonPropertyName("fieldType")]
    public string FieldType { get; set; } = "text";

    [JsonPropertyName("catalog")]
    public string? Catalog { get; set; }

    /// <summary>Valor por defecto que se aplica a las celdas editables de esta
    /// columna cuando la HC se inicia. Se persiste en valores. El usuario lo
    /// puede sobrescribir. Util para "NO REFIERE" / "NORMAL" / etc.</summary>
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}

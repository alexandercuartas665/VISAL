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

    /// <summary>
    /// Solo para fieldType = "select". Si true, el usuario puede escribir un
    /// valor libre que no este en la lista (render como input + datalist).
    /// Si false (default), el campo se renderiza como select estricto y el
    /// usuario solo puede elegir una de las opciones.
    /// </summary>
    [JsonPropertyName("allowCustom")]
    public bool AllowCustom { get; set; }

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
    /// Opciones por celda seed, indexadas por "rowIdx_colIdx". Permite que en
    /// una tabla tipo "Examen Fisico" la fila "Atrofia" tenga opciones
    /// distintas (NO SE OBSERVA, PRESENTE, LEVE) a la fila "Pupilas" (SI, NO).
    /// Solo se incluyen las celdas que tienen override; el resto usa las
    /// opciones de la columna (Options del FormColumn) o queda libre.
    /// Estructura JSON: { "0_1": ["opt1","opt2"], "1_1": [...] }.
    /// </summary>
    [JsonPropertyName("seedRowCellOptions")]
    public Dictionary<string, List<string>>? SeedRowCellOptions { get; set; }

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

    /// <summary>Opciones fijas para celdas tipo "select" cuando no se usa un
    /// catalogo dinamico. Una por linea en el editor (estilo del campo
    /// top-level Options).</summary>
    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    /// <summary>Si fieldType = "select" y allowCustom = true, la celda se
    /// renderiza como input + datalist (sugerencias pero permite escribir lo
    /// que sea). Si false, se renderiza como select estricto.</summary>
    [JsonPropertyName("allowCustom")]
    public bool AllowCustom { get; set; }

    /// <summary>Valor por defecto que se aplica a las celdas editables de esta
    /// columna cuando la HC se inicia. Se persiste en valores. El usuario lo
    /// puede sobrescribir. Util para "NO REFIERE" / "NORMAL" / etc.</summary>
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    /// <summary>Nombre (Name) de OTRA columna de la misma tabla que actua como
    /// disparador. Si esta seteado, las celdas de esta columna solo se habilitan
    /// cuando la celda hermana (mismo rowIdx) tenga el valor <see cref="EnabledByValue"/>.
    /// Ejemplo: en actividad_fisica, las columnas cantidad/frecuencia tienen
    /// EnabledByColumn="refiere" y EnabledByValue="SI", asi se desactivan si
    /// el paciente reporta NO. Vacio = sin condicion (comportamiento normal).</summary>
    [JsonPropertyName("enabledByColumn")]
    public string? EnabledByColumn { get; set; }

    /// <summary>Valor que debe tener la celda disparadora para habilitar esta
    /// columna. Compara case-insensitive con trim. Vacio = sin condicion.</summary>
    [JsonPropertyName("enabledByValue")]
    public string? EnabledByValue { get; set; }

    /// <summary>Texto de ayuda que aparece como placeholder de las celdas de esta
    /// columna (paralelo a FormNode.Placeholder en campos top-level). Vacio =
    /// sin placeholder. Antes el viewer caia a "Elige o escribe" / c.Catalog
    /// como hint hardcodeado; ahora cada formulario decide explicitamente.</summary>
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }
}

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

    /// <summary>
    /// Solo para Type = "section". Si true, la seccion aparece como fila con
    /// checkbox en el modal de impresion del paquete. El default del checkbox
    /// es DESmarcado — el usuario debe elegir explicitamente incluirla. Las
    /// secciones no marcadas como opcionales se imprimen siempre (comportamiento
    /// historico).
    /// </summary>
    [JsonPropertyName("printOptional")]
    public bool PrintOptional { get; set; }

    // ── Bloque de texto (Type = "text") ──
    /// <summary>heading | subheading | paragraph.</summary>
    [JsonPropertyName("textStyle")]
    public string? TextStyle { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Alineacion horizontal para bloques de texto: left | center |
    /// right. Default null = left (comportamiento historico). Aplica al
    /// heading/subheading/paragraph tanto en el visor como en la impresion.</summary>
    [JsonPropertyName("textAlign")]
    public string? TextAlign { get; set; }

    // ── Campo ──
    /// <summary>text | number | email | date | datetime | textarea | select | autocomplete | calculated | table.</summary>
    [JsonPropertyName("fieldType")]
    public string? FieldType { get; set; }

    /// <summary>
    /// Solo para fieldType = "textarea". Numero de filas (rows) que muestra
    /// visualmente el textarea. Default null => 3 filas (comportamiento
    /// historico). Rango razonable 1..30; el evaluador no impone limite duro,
    /// pero el disenador de UI si.
    /// </summary>
    [JsonPropertyName("rows")]
    public int? Rows { get; set; }

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

    /// <summary>
    /// Si true, el campo se puede EDITAR a mano aunque sea destino de un prefill
    /// (prellenado desde paciente, sistema, firma o historia medica). Por defecto
    /// (false) todo campo prellenado queda bloqueado ("auto"). Con este flag el
    /// prefill sigue rellenando el valor inicial, pero el profesional lo puede
    /// sobrescribir. No afecta el bloqueo por HC cerrada/lectura (ese manda igual).
    /// </summary>
    [JsonPropertyName("editableConPrefill")]
    public bool EditableConPrefill { get; set; }

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

    // ── Codigo QR (fieldType = "qr") ──
    /// <summary>
    /// Solo para fieldType = "qr". Name de OTRO campo del bag cuyo valor (un
    /// codigo de verificacion) se codifica en el QR. Si esta vacio, se usa el
    /// propio <see cref="Name"/> del nodo. El renderer construye la URL publica
    /// <c>{PublicBaseUrl}/verificar-orden/{codigo}</c> y genera el PNG. Ejemplo:
    /// "orden_codigo_verificacion".
    /// </summary>
    [JsonPropertyName("qrSource")]
    public string? QrSource { get; set; }

    /// <summary>Solo para fieldType = "qr". Tamano de visualizacion del QR en px. Default 200.</summary>
    [JsonPropertyName("qrSize")]
    public int? QrSize { get; set; }

    /// <summary>
    /// Solo para fieldType = "qr". Si true (default), muestra el codigo como texto
    /// monoespaciado debajo del QR para que se pueda tipear manualmente en la
    /// pagina publica de verificacion.
    /// </summary>
    [JsonPropertyName("showCode")]
    public bool ShowCode { get; set; } = true;

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
    /// Filas seed marcadas como "fila titulo" (banner). Key = indice de fila
    /// (como string). Value = alineacion del texto ("left"/"center"/"right",
    /// por defecto "center"). Las filas presentes aqui se renderizan como
    /// una unica celda con colspan a todo el ancho, en negrita y con la
    /// alineacion elegida — sirven para separar secciones dentro de una
    /// tabla larga (ej. "MEDICAMENTOS PRN" entre bloques). El texto sale
    /// de la primera celda de SeedRows[i]. Las filas no listadas aqui se
    /// comportan como hasta ahora (celdas normales).
    /// </summary>
    [JsonPropertyName("seedRowBanner")]
    public Dictionary<string, string>? SeedRowBanner { get; set; }

    /// <summary>
    /// Si true, oculta el boton "+ Agregar fila" para que la tabla quede limitada
    /// a las filas semilla. Por defecto false (permite agregar).
    /// </summary>
    [JsonPropertyName("lockRows")]
    public bool LockRows { get; set; }

    /// <summary>
    /// Habilita dictado por voz (Whisper) en este campo. Solo aplica a campos
    /// fieldType="textarea". El FormViewer muestra un boton flotante junto al
    /// area de texto; el JS captura audio en chunks de ~5s y lo manda a
    /// /api/transcribe. Default false: opt-in por campo del designer.
    /// </summary>
    [JsonPropertyName("enableVoice")]
    public bool EnableVoice { get; set; }

    /// <summary>
    /// Regla de visibilidad condicional del nodo. Si esta seteada, el nodo
    /// (seccion, campo o bloque de texto) solo se renderiza cuando la
    /// condicion contra <see cref="VisibleWhenRule.Field"/> se cumple. Si es
    /// null, el nodo se muestra siempre. El evaluador vive en
    /// <see cref="VisibleWhenEvaluator"/> y lo usa el FormViewer y la vista
    /// de impresion.
    /// </summary>
    [JsonPropertyName("visibleWhen")]
    public VisibleWhenRule? VisibleWhen { get; set; }

    /// <summary>
    /// Si true, el campo se omite en la IMPRESION (HistoriaDoc,
    /// ImprimirPaquete, ImprimirHcsPaciente/FormViewer read-only) cuando su
    /// valor esta vacio. En modo captura (FormViewer editable) el campo
    /// siempre aparece — el flag solo afecta al render impreso. Util para
    /// campos "activadores" tipo checkboxes o flags que solo tienen sentido
    /// cuando el usuario los diligencio. Default false. Aplica solo a
    /// nodos de tipo "field"; en secciones/bloques de texto se ignora.
    /// </summary>
    [JsonPropertyName("hideIfEmpty")]
    public bool HideIfEmpty { get; set; }

    /// <summary>
    /// Si true, el valor de este campo se toma como "fecha real de la atencion"
    /// y se copia al campo <c>HistoriaClinica.FechaAtencion</c> al guardar y al
    /// cerrar. Solo aplica a campos fieldType = "date" | "datetime". Se puede
    /// marcar en varios campos del mismo formulario; el helper toma la mayor
    /// fecha entre los campos marcados que tengan valor. Default false.
    /// </summary>
    [JsonPropertyName("isFechaAtencion")]
    public bool IsFechaAtencion { get; set; }

    /// <summary>
    /// Si true, el nodo (seccion, campo o bloque de texto) NUNCA aparece en la
    /// impresion — ni en HistoriaDoc, ImprimirPaquete, ImprimirHcsPaciente,
    /// DocumentoImprimible ni ImprimirOrdenesFiltradas. En captura sigue visible
    /// para que el profesional pueda diligenciarlo. Se diferencia de
    /// <see cref="HideIfEmpty"/> (que solo oculta si el valor esta vacio) y de
    /// <see cref="PrintOptional"/> (que ofrece un checkbox opcional en el modal
    /// de impresion). Este flag es duro: el nodo simplemente no se renderiza al
    /// imprimir. Util para secciones de trabajo interno tipo "Notas rapidas del
    /// equipo" o campos administrativos que no son parte del documento final.
    /// Default false. Aplica a todos los tipos de nodo (seccion, campo, texto).
    /// </summary>
    [JsonPropertyName("noImprimir")]
    public bool NoImprimir { get; set; }

    /// <summary>
    /// Plantilla reactiva para autorellenar el campo con valores derivados de
    /// OTROS campos del mismo formulario. Sintaxis: <c>{nombreCampo}</c> se
    /// sustituye por el valor actual del campo con ese Name. Ejemplo:
    /// <c>"TA: {ta} - FC: {fc} - Temp: {temperatura} - SatO2: {sato2}"</c>
    /// pega la lectura de signos vitales en un textarea de notas de turno.
    /// Comportamiento en el FormViewer:
    ///  - Se recalcula cada vez que cualquier campo referenciado cambia.
    ///  - Si el usuario ya edito manualmente el campo (dirty), NO se sobrescribe.
    ///  - Si CUALQUIER placeholder referenciado esta vacio, no expande — deja
    ///    el campo intacto (evita renglones parciales del tipo "TA: - FC: 72").
    ///  - Cuando <see cref="FormulaOnlyWhenVisible"/> es true (default), solo
    ///    aplica si el campo pasa la evaluacion <c>VisibleWhen</c>.
    /// El campo SIGUE siendo editable — el usuario puede modificar/agregar
    /// texto al valor autoexpandido; el dirty tracking lo respeta.
    /// </summary>
    [JsonPropertyName("formulaTemplate")]
    public string? FormulaTemplate { get; set; }

    /// <summary>Si true (default), la <see cref="FormulaTemplate"/> solo se
    /// aplica cuando el campo pasa su <see cref="VisibleWhen"/>. Utility para
    /// formularios con secciones por turno (Recibe/Mañana/Tarde/Noche/Entrega)
    /// donde cada textarea aparece condicionalmente y solo el activo debe
    /// llenarse con los signos actuales.</summary>
    [JsonPropertyName("formulaOnlyWhenVisible")]
    public bool FormulaOnlyWhenVisible { get; set; } = true;

    public bool IsSection => Type == "section";
    public bool IsText => Type == "text";
    public bool IsTable => Type == "field" && FieldType == "table";
    public bool IsQr => Type == "field" && FieldType == "qr";
}

/// <summary>
/// Regla de visibilidad condicional. Compara el valor actual del campo
/// referenciado por <see cref="Field"/> (por su Name en el schema o el
/// prefill del paciente) contra <see cref="Value"/> segun <see cref="Operator"/>.
/// Operadores soportados: equals, notEquals, in, notIn, isEmpty, isNotEmpty.
/// Diseñado para poder extenderse a greaterThan / lessThan en el futuro.
/// </summary>
public sealed class VisibleWhenRule
{
    /// <summary>Name del campo (schema field.Name o prefill key) a evaluar.</summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    /// <summary>Comparador. Case-insensitive.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "equals";

    /// <summary>
    /// Valor esperado. Para "in"/"notIn" se puede pasar como string separado
    /// por coma (ej. "MASCULINO,OTRO") o serializar como array JSON — el
    /// evaluador acepta ambos formatos.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
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

    /// <summary>
    /// Si true (default), el bloque "N° interno + fecha" (HC N° / Apertura o
    /// Cerrada, arriba a la derecha del documento) aparece en la IMPRESION. Si
    /// false, solo se ve en pantalla como referencia rapida del operador y se
    /// oculta en el PDF (d-print-none). Se configura por formato en el disenador:
    /// asi las notas / HCs del paciente no llevan el id interno ni la fecha en el
    /// papel, pero las ordenes / formula medica si. Historicamente estaba oculto
    /// para todos los formatos; ahora es parametrizable (default = mostrar).
    /// </summary>
    [JsonPropertyName("imprimirCodigoFecha")]
    public bool ImprimirCodigoFecha { get; set; } = true;

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

    /// <summary>text | textarea | number | date | datetime | select | autocomplete.</summary>
    [JsonPropertyName("fieldType")]
    public string FieldType { get; set; } = "text";

    /// <summary>
    /// Solo para fieldType = "textarea". Numero de filas visibles del textarea
    /// en cada celda. Default null => 2 filas (un poco mas compacto que el
    /// top-level, que default a 3). Rango razonable 1..30; el disenador acota
    /// el min/max, el viewer usa Math.Clamp defensivo.
    /// </summary>
    [JsonPropertyName("rows")]
    public int? Rows { get; set; }

    /// <summary>
    /// Habilita dictado por voz en las celdas textarea de esta columna. Reusa
    /// el mismo boton flotante que el top-level (Web Speech API en el
    /// FormViewer). Solo tiene efecto cuando FieldType = "textarea".
    /// </summary>
    [JsonPropertyName("enableVoice")]
    public bool EnableVoice { get; set; }

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

    /// <summary>Formula que se evalua por fila cuando FieldType = "calculated".
    /// Sintaxis identica a FormNode.Formula: funciones como imc(...),
    /// sum(...), cases(...), etc. Los identificadores se resuelven contra
    /// las celdas de la MISMA fila (Name de otras columnas). Ejemplo en
    /// tabla antropometria: imc(peso, talla). Solo tiene efecto cuando
    /// FieldType = "calculated"; en cualquier otro fieldType se ignora.</summary>
    [JsonPropertyName("formula")]
    public string? Formula { get; set; }

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

    /// <summary>Nombre (Name) de OTRA columna de la misma tabla cuyo valor
    /// determina las opciones de ESTA columna (cascading select). Si esta
    /// seteado y fieldType = "select", el viewer toma las opciones de
    /// <see cref="OptionsMap"/>[valorHermana] en vez de <see cref="Options"/>.
    /// Si no hay match en OptionsMap, la lista queda vacia y la celda muestra
    /// un placeholder tipo "Selecciona {OptionsMapKey} primero". Complementa
    /// (no reemplaza) a Options: cuando OptionsMapKey esta seteado, Options
    /// se ignora. Ejemplo: en HC-FO-10a la columna "patron" tiene
    /// OptionsMapKey="dominio" y OptionsMap poblado con los patrones APTA
    /// por dominio (CARDIOPULMONAR, OSTEOMUSCULAR, etc.).</summary>
    [JsonPropertyName("optionsMapKey")]
    public string? OptionsMapKey { get; set; }

    /// <summary>Mapa clave -> lista de opciones. La clave se compara
    /// case-insensitive con el valor de la celda hermana
    /// <see cref="OptionsMapKey"/>. Si no hay match, la lista de opciones
    /// queda vacia (celda muestra placeholder pero no permite elegir). Solo
    /// se usa cuando OptionsMapKey esta seteado.</summary>
    [JsonPropertyName("optionsMap")]
    public Dictionary<string, List<string>>? OptionsMap { get; set; }
}

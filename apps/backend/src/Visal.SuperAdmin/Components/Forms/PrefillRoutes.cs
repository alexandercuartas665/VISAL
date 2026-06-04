using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Conjunto de rutas de prefill asociadas a un FormDefinition. Se serializa al
/// jsonb FormDefinition.PrefillRoutesJson.
/// </summary>
public sealed class PrefillRouteSet
{
    [JsonPropertyName("routes")]
    public List<PrefillRoute> Routes { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static PrefillRouteSet FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new PrefillRouteSet(); }
        try
        {
            return JsonSerializer.Deserialize<PrefillRouteSet>(json, JsonOptions) ?? new PrefillRouteSet();
        }
        catch
        {
            return new PrefillRouteSet();
        }
    }
}

/// <summary>Una ruta nombrada: mapeo desde un modulo origen al schema del formulario.</summary>
public sealed class PrefillRoute
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Nombre legible. Ej. "Paciente", "Profesional", "Contrato vigente".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Clave del modulo origen: paciente | profesional | contrato | usuario. Define que campos source estan disponibles.</summary>
    [JsonPropertyName("sourceModule")]
    public string SourceModule { get; set; } = "paciente";

    [JsonPropertyName("mappings")]
    public List<PrefillFieldMap> Mappings { get; set; } = new();
}

/// <summary>Un mapeo: campo del modulo origen -> campo del schema del formulario.</summary>
public sealed class PrefillFieldMap
{
    /// <summary>Nombre del campo en el modulo origen (ej. "nombreCompleto", "numeroDocumento").</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>Name del campo del FormSchema destino (FormNode.Name).</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";
}

/// <summary>Catalogo de campos disponibles por modulo origen para alimentar el dropdown del modal.</summary>
public static class PrefillSourceCatalog
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Campos { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["paciente"] = new[]
        {
            "numeroDocumento", "tipoDocumento", "nombreCompleto",
            "primerNombre", "segundoNombre", "primerApellido", "segundoApellido",
            "fechaNacimiento", "edad", "sexo", "estadoCivil",
            "telefono", "email", "direccion", "ciudad", "zona",
            "ocupacion", "regimen",
            "contactoEmergencia", "parentesco", "telefonoEmergencia",
            "sede"
        },
        ["profesional"] = new[]
        {
            "numeroDocumento", "nombreCompleto", "registroMedico",
            "ciudad", "celular", "tipoProfesional"
        },
        ["contrato"] = new[]
        {
            "codigoContrato", "aseguradoraNombre", "estado"
        },
        ["usuario"] = new[]
        {
            "email", "displayName", "documento", "username",
            "primerNombre", "segundoNombre", "primerApellido", "segundoApellido",
            "celular", "fijo", "ciudad", "direccion"
        },
        // Datos derivados de la instancia actual de HC (no del paciente). Se
        // refresca en tiempo real cuando el doctor agrega/quita items en los
        // submodulos de la HC (orden de medicamentos, etc.). Los campos
        // marcados aqui se vuelven readonly en el FormViewer.
        ["historiaMedica"] = new[]
        {
            "medicamentos.lista_numerada"
        }
    };

    /// <summary>Nombre legible del sourceModule para el dropdown del modal Rutas de prefill.</summary>
    public static string NombreLegible(string sourceModule) => sourceModule switch
    {
        "paciente" => "Paciente",
        "profesional" => "Profesional",
        "contrato" => "Contrato",
        "usuario" => "Usuario",
        "historiaMedica" => "Historia Medica",
        _ => sourceModule
    };
}

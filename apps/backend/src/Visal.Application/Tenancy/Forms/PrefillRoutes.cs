using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visal.Application.Tenancy.Forms;

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

    /// <summary>Devuelve true si al menos una ruta con SourceModule que empiece
    /// con "firmaAcompanante" tiene mapeos activos (Source y Target no vacios).
    /// La usan los modulos de HC para decidir si preguntar al usuario cual
    /// acompanante firma cuando el paciente tiene varios contactos.</summary>
    public bool TieneMapeoAcompanante()
    {
        foreach (var r in Routes)
        {
            if (r?.SourceModule is null) { continue; }
            if (!r.SourceModule.StartsWith("firmaAcompanante", StringComparison.OrdinalIgnoreCase)) { continue; }
            foreach (var m in r.Mappings)
            {
                if (!string.IsNullOrWhiteSpace(m.Source) && !string.IsNullOrWhiteSpace(m.Target)) { return true; }
            }
        }
        return false;
    }

    public static PrefillRouteSet FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new PrefillRouteSet(); }
        PrefillRouteSet set;
        try
        {
            set = JsonSerializer.Deserialize<PrefillRouteSet>(json, JsonOptions) ?? new PrefillRouteSet();
        }
        catch
        {
            return new PrefillRouteSet();
        }
        // Dedup defensivo al leer: si en la BD quedaron mapeos duplicados por bugs
        // historicos de Auto-enlazar, el modal debe verse limpio y el runtime del
        // prefill no debe aplicar el mismo mapeo dos veces. Se conserva la primera
        // ocurrencia de cada par (source, target) dentro de cada ruta — normalmente
        // es la manual y trae ColumnMappings del usuario.
        foreach (var r in set.Routes)
        {
            if (r.Mappings.Count <= 1) { continue; }
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<PrefillFieldMap>(r.Mappings.Count);
            foreach (var m in r.Mappings)
            {
                var key = $"{m.Source}|{m.Target}";
                if (vistos.Add(key)) { deduped.Add(m); }
            }
            r.Mappings = deduped;
        }
        return set;
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

    /// <summary>
    /// Mapeo explicito columna-a-columna cuando el destino es una tabla. Clave =
    /// FormColumn.Id (id de la columna de la tabla destino). Valor = nombre del
    /// campo de la fuente (ej. "fechaDesde", "nombreMedicamento"). Si esta
    /// presente, el helper usa este mapeo en lugar de la heuristica por nombre.
    /// Solo aplica cuando target es una tabla repetible.
    /// </summary>
    [JsonPropertyName("columnMappings")]
    public Dictionary<string, string>? ColumnMappings { get; set; }

    /// <summary>
    /// Filtro opcional de categorias a incluir cuando el <see cref="Source"/> es
    /// la sub-ruta compuesta <c>historiaMedica / todo.lista_completa</c>. Cada
    /// entrada es un codigo de categoria (ver <see cref="PrefillCategoriasHm.Codigos"/>).
    /// Cuando es null o vacia, el helper incluye TODAS las categorias por default.
    /// Se ignora si Source es cualquier otra cosa.
    /// </summary>
    [JsonPropertyName("includeCategories")]
    public List<string>? IncludeCategories { get; set; }
}

/// <summary>
/// Catalogo de categorias que puede incluir la sub-ruta compuesta
/// <c>historiaMedica / todo.lista_completa</c>. El codigo es lo que se persiste
/// en <see cref="PrefillFieldMap.IncludeCategories"/>; el titulo es el heading
/// que aparece en el textarea al concatenar los bloques.
/// El orden aca es el orden en que se emiten los bloques en el textarea.
/// </summary>
public static class PrefillCategoriasHm
{
    public static readonly (string Codigo, string Titulo)[] Todas = new[]
    {
        ("medicamentos", "MEDICAMENTOS"),
        ("ordenes_servicio", "SERVICIOS"),
        ("insumos", "INSUMOS"),
        ("remisiones", "REMISIONES"),
        ("rx_imagenologia", "RX IMAGENOLOGIA"),
        ("laboratorios", "LABORATORIOS"),
        ("insumos_externos", "INSUMOS EXTERNOS"),
        ("incapacidades", "INCAPACIDADES"),
        ("certificaciones", "CERTIFICACIONES")
    };

    public static readonly string[] Codigos = Todas.Select(t => t.Codigo).ToArray();
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
            "telefono", "email", "direccion", "barrio", "ciudad", "zona",
            "ocupacion", "regimen",
            "contactoEmergencia", "parentesco", "telefonoEmergencia",
            "sede", "eps",
            // Clasificaciones y catalogos (nombre legible del FK ya resuelto).
            "tipoUsuario", "clasificacionPaciente", "clasificacionGrupoPatologia",
            // Salud y estados.
            "grupoRh", "incapacidad", "estado", "estratoSocial",
            // Administrativo PAD.
            "codigoAceptacion", "fechaComentan", "fechaIngresoPad", "fechaEgresoPad",
            // Diagnostico y tutela.
            "cie10Codigo", "diagnosticoPrincipal",
            "tutela", "tipoTutela", "medContratado"
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
            // Sub-ruta COMPUESTA: concatena las N categorias marcadas en
            // PrefillFieldMap.IncludeCategories (todas por default) en un solo
            // textarea con heading por bloque. Bloques vacios se omiten.
            "todo.lista_completa",
            "medicamentos.lista_numerada",
            "remisiones.lista_numerada",
            "incapacidades.lista_numerada",
            "certificaciones.lista_numerada",
            "ordenes_servicio.lista_numerada",
            "insumos.lista_numerada",
            "rx_imagenologia.lista_numerada",
            "laboratorios.lista_numerada",
            "insumos_externos.lista_numerada"
        },
        // Firma del paciente: PNG/URL del archivo mas reciente en NotaMedicaDocumento
        // con categoria "Firma del Paciente" para el paciente activo. Se resuelve en
        // runtime via FirmasPrefillHelper.
        ["firmaPaciente"] = new[] { "url" },
        // Firma del profesional logueado: Profesional.FirmaUrl del TenantUser que
        // esta llenando el formulario (resuelto por TenantUser.ProfesionalId).
        ["firmaProfesional"] = new[] { "url" },
        // Firmas y datos de los primeros 4 contactos de emergencia del paciente.
        // El slot N-esimo lee de la fila con Orden=N (o la N-esima por Nombre si no
        // tiene Orden). Cada slot expone la firma capturada por el pariente en
        // /firma/{token} (o la del canvas de admision), su nombre y parentesco.
        // Un formulario de consentimiento suele pedir "firma acompanante" +
        // "nombre acompanante" + "parentesco acompanante" — los 3 campos aqui.
        ["firmaAcompanante1"] = new[] { "url", "nombre", "parentesco" },
        ["firmaAcompanante2"] = new[] { "url", "nombre", "parentesco" },
        ["firmaAcompanante3"] = new[] { "url", "nombre", "parentesco" },
        ["firmaAcompanante4"] = new[] { "url", "nombre", "parentesco" },
        // Contexto del sistema al momento de iniciar el formulario: fecha y
        // hora actuales en distintos formatos, agencia (tenant), sede activa
        // del usuario y datos del usuario logueado. Util sobre todo para
        // escalas / evoluciones / consentimientos donde se pide la fecha y
        // la hora de aplicacion sin que el doctor las teclee.
        ["sistema"] = new[]
        {
            "fechaActual", "fechaCorta", "fechaLarga",
            "horaActual", "horaActualLarga",
            "fechaHoraActual",
            "agencia", "agenciaNombre", "agenciaSlogan",
            "sede", "sedeNombre", "sedeCiudad",
            "usuario", "usuarioNombre", "usuarioEmail",
            // Datos del profesional vinculado al usuario logueado.
            "usuarioIdentificacion", "usuarioRegistroMedico", "usuarioFirma"
        }
    };

    /// <summary>
    /// Campos disponibles para mapear a una COLUMNA de tabla destino, agrupados por
    /// el "campo fuente" tal cual aparece en el dropdown principal (ej.
    /// "medicamentos.lista_numerada"). Estos son los nombres logicos de cada
    /// propiedad del item, no la lista_numerada agregada. Se usan en el mini
    /// mapeo columna-a-columna del modal Rutas de prefill.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> CamposColumna { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["medicamentos.lista_numerada"] = new[]
        {
            "nombreMedicamento", "codigo", "cantidad", "cantidadTotal",
            "frecuencia", "dias", "posologia", "via", "observacion"
        },
        ["remisiones.lista_numerada"] = new[]
        {
            "codigoEspecialidad", "nombreEspecialidad", "capitulo", "motivo"
        },
        ["incapacidades.lista_numerada"] = new[]
        {
            "motivo", "fechaDesde", "fechaHasta", "dias", "tipo"
        },
        ["certificaciones.lista_numerada"] = new[]
        {
            "titulo", "contenido"
        },
        ["ordenes_servicio.lista_numerada"] = new[]
        {
            "codigoServicio", "descripcion", "cantidad", "observaciones"
        },
        ["insumos.lista_numerada"] = new[]
        {
            "codigo", "descripcion", "cantidad", "observaciones"
        },
        // Ordenes externas (RX Imagenologia / Laboratorios / Insumos externos).
        // Todas comparten el mismo shape del item (OrdenExternaItemDto): codigo +
        // descripcion + cantidad + observaciones. Al mapear a una tabla de HC
        // (ej. "estudios solicitados") cualquiera de estos campos puede caer a
        // una columna concreta desde el modal de rutas.
        ["rx_imagenologia.lista_numerada"] = new[]
        {
            "codigo", "descripcion", "cantidad", "observaciones"
        },
        ["laboratorios.lista_numerada"] = new[]
        {
            "codigo", "descripcion", "cantidad", "observaciones"
        },
        ["insumos_externos.lista_numerada"] = new[]
        {
            "codigo", "descripcion", "cantidad", "observaciones"
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
        "firmaPaciente" => "Firma del Paciente",
        "firmaProfesional" => "Firma del Profesional",
        "firmaAcompanante1" => "Firma Acompanante 1",
        "firmaAcompanante2" => "Firma Acompanante 2",
        "firmaAcompanante3" => "Firma Acompanante 3",
        "firmaAcompanante4" => "Firma Acompanante 4",
        "sistema" => "Sistema (fecha, hora, sede, agencia)",
        _ => sourceModule
    };
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class FormDefinitionService : IFormDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public FormDefinitionService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FormDefinitionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitions
            .AsNoTracking()
            .OrderBy(f => f.Nombre)
            .Select(f => new FormDefinitionDto(f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, f.UpdatedAt, f.CodigoSecundario))
            .ToListAsync(cancellationToken);
    }

    public async Task<FormDefinitionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitions
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new FormDefinitionDetailDto(f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, f.SchemaJson, f.PrefillRoutesJson, f.CodigoSecundario))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FormDefinitionDetailDto?> GetActivoByTipoAsync(string tipo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tipo)) { return null; }
        var t = tipo.Trim();
        return await _db.FormDefinitions
            .AsNoTracking()
            .Where(f => f.Activo && f.Tipo == t)
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .Select(f => new FormDefinitionDetailDto(f.Id, f.Codigo, f.Nombre, f.Version, f.Tipo, f.Activo, f.SchemaJson, f.PrefillRoutesJson, f.CodigoSecundario))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FormDefinitionDetailDto?> SaveAsync(SaveFormDefinitionRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var codigo = request.Codigo.Trim();
        var nombre = request.Nombre.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre))
        {
            throw new InvalidOperationException("El codigo y el nombre del formulario son obligatorios.");
        }

        FormDefinition entity;

        if (request.Id is Guid id)
        {
            // El filtro global garantiza que solo se edita un formulario del tenant activo.
            var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (existing is null)
            {
                return null;
            }

            var clash = await _db.FormDefinitions.AnyAsync(f => f.Codigo == codigo && f.Id != id, cancellationToken);
            if (clash)
            {
                throw new InvalidOperationException($"Ya existe otro formulario con el codigo '{codigo}'.");
            }

            existing.Codigo = codigo;
            existing.CodigoSecundario = string.IsNullOrWhiteSpace(request.CodigoSecundario) ? null : request.CodigoSecundario.Trim();
            existing.Nombre = nombre;
            existing.Version = request.Version?.Trim();
            existing.Tipo = request.Tipo?.Trim();
            existing.SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{\"children\":[]}" : request.SchemaJson;
            existing.Activo = request.Activo;
            if (request.PrefillRoutesJson is not null) { existing.PrefillRoutesJson = request.PrefillRoutesJson; }
            entity = existing;

            _audit.Write(actorUserId, "form-definition.update", nameof(FormDefinition), entity.Id,
                previousValue: null, newValue: new { entity.Codigo, entity.Nombre }, tenantId: entity.TenantId);
        }
        else
        {
            if (_tenantContext.TenantId is not Guid tenantId)
            {
                return null;
            }

            var clash = await _db.FormDefinitions.AnyAsync(f => f.Codigo == codigo, cancellationToken);
            if (clash)
            {
                throw new InvalidOperationException($"Ya existe un formulario con el codigo '{codigo}'.");
            }

            entity = new FormDefinition
            {
                TenantId = tenantId,
                Codigo = codigo,
                CodigoSecundario = string.IsNullOrWhiteSpace(request.CodigoSecundario) ? null : request.CodigoSecundario.Trim(),
                Nombre = nombre,
                Version = request.Version?.Trim(),
                Tipo = request.Tipo?.Trim(),
                SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{\"children\":[]}" : request.SchemaJson,
                Activo = request.Activo,
                PrefillRoutesJson = request.PrefillRoutesJson
            };
            _db.FormDefinitions.Add(entity);

            _audit.Write(actorUserId, "form-definition.create", nameof(FormDefinition), entity.Id,
                previousValue: null, newValue: new { entity.Codigo, entity.Nombre }, tenantId: tenantId);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new FormDefinitionDetailDto(entity.Id, entity.Codigo, entity.Nombre, entity.Version, entity.Tipo, entity.Activo, entity.SchemaJson, entity.PrefillRoutesJson, entity.CodigoSecundario);
    }

    public async Task<bool> UpdatePrefillRoutesAsync(Guid id, string? prefillRoutesJson, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is null) { return false; }
        existing.PrefillRoutesJson = string.IsNullOrWhiteSpace(prefillRoutesJson) ? null : prefillRoutesJson;
        _audit.Write(actorUserId, "form-definition.update-prefill-routes", nameof(FormDefinition), existing.Id,
            previousValue: null, newValue: new { existing.Codigo }, tenantId: existing.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AutoEnlazarPacienteResultDto> AutoEnlazarPacienteEnTodosAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }

        // Cargamos solo los formularios del tenant activo. Si en el futuro queremos
        // filtrar (ej: solo Activo o solo Tipo=historia), se hace aqui.
        var forms = await _db.FormDefinitions
            .Where(f => f.TenantId == tid)
            .ToListAsync(cancellationToken);

        int revisados = 0, actualizados = 0, sinCambios = 0, mapeosAgregados = 0;

        foreach (var f in forms)
        {
            revisados++;

            // Names presentes en el schema (recursivo por secciones).
            var names = ExtraerNamesDelSchema(f.SchemaJson);
            if (names.Count == 0) { sinCambios++; continue; }

            // Cargamos las rutas actuales (puede ser null o vacio).
            var routes = ParsePrefillRoutes(f.PrefillRoutesJson);

            // Buscamos o creamos la ruta "paciente".
            var ruta = routes.FirstOrDefault(r =>
                string.Equals(r["sourceModule"]?.GetValue<string>(), "paciente", StringComparison.OrdinalIgnoreCase));
            if (ruta is null)
            {
                ruta = new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N")[..8],
                    ["name"] = "Paciente",
                    ["sourceModule"] = "paciente",
                    ["mappings"] = new JsonArray()
                };
                routes.Add(ruta);
            }

            // Mappings actuales: indexamos por target para preservar lo manual.
            var mappingsArr = ruta["mappings"] as JsonArray ?? new JsonArray();
            var targetsExistentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mappingsArr)
            {
                var tgt = m?["target"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(tgt)) { targetsExistentes.Add(tgt); }
            }

            int agregadosEnEsteForm = 0;
            foreach (var (name, label) in names)
            {
                if (targetsExistentes.Contains(name)) { continue; } // mapeo manual: respetar

                var source = InferirCampoPaciente(name, label);
                if (source is null) { continue; }

                mappingsArr.Add(new JsonObject
                {
                    ["source"] = source,
                    ["target"] = name
                });
                targetsExistentes.Add(name);
                agregadosEnEsteForm++;
            }

            if (agregadosEnEsteForm == 0) { sinCambios++; continue; }

            // Aseguramos que la ruta tenga el array actualizado.
            ruta["mappings"] = mappingsArr;

            // Reconstruir PrefillRoutesJson.
            var nuevoJson = new JsonObject { ["routes"] = routes }.ToJsonString();
            f.PrefillRoutesJson = nuevoJson;

            mapeosAgregados += agregadosEnEsteForm;
            actualizados++;

            _audit.Write(actorUserId, "form-definition.auto-enlazar-paciente", nameof(FormDefinition), f.Id,
                previousValue: null, newValue: new { f.Codigo, agregados = agregadosEnEsteForm }, tenantId: tid);
        }

        if (actualizados > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new AutoEnlazarPacienteResultDto(revisados, actualizados, mapeosAgregados, sinCambios);
    }

    /// <summary>
    /// Heuristica que decide a que campo del Paciente mapear un campo del formulario.
    /// Se basa en el Name (clave canonica) y, como respaldo, en el Label visible.
    /// Devuelve null cuando no hay match plausible.
    /// </summary>
    private static string? InferirCampoPaciente(string name, string? label)
    {
        // Normalizamos: minusculas, sin tildes, sin separadores. Esto resuelve casos
        // tipo "direcci_n" (la "o" con tilde fue sustituida por "_" al sanitizar el
        // Name) y "tel_fono" (la "e" con tilde quedo en "_").
        var n = Normalizar(name);
        var l = Normalizar(label);

        // Match exacto del catalogo: si el Name es identico al campo paciente, mapeamos directo.
        var directos = new[]
        {
            "numerodocumento", "tipodocumento", "nombrecompleto",
            "primernombre", "segundonombre", "primerapellido", "segundoapellido",
            "fechanacimiento", "sexo", "estadocivil",
            "telefono", "email", "direccion", "ciudad", "zona",
            "ocupacion", "regimen",
            "contactoemergencia", "parentesco", "telefonoemergencia",
            "sede"
        };
        foreach (var d in directos)
        {
            if (n == d) { return CanonPaciente(d); }
        }

        // Casos de Name corto / abreviado donde el match exacto es la unica forma
        // de evitar falsos positivos. "cc", "ti", "ce", "rc" como Name de un campo
        // suelen ser tipos de documento o el numero de documento.
        if (n == "cc" || n == "ti" || n == "ce" || n == "rc" || n == "pa" || n == "pep" || n == "ms")
        {
            return "numeroDocumento";
        }
        if (n == "tipodoc" || n == "tipid" || n == "tipoid" || n == "tipdocide" || n == "tipoide")
        {
            return "tipoDocumento";
        }

        // Heuristica por contains (de mas especifico a mas generico). El primer match gana.
        // Cubrimos snake_case con separadores ya removidos por Normalizar(), camelCase,
        // formatos legacy (NomPaciente, FecNac, NoDocIde, etc.) y variantes con/sin tilde.
        var reglas = new (string fragmento, string campo)[]
        {
            // Fecha de nacimiento (alta prioridad — "fecha" sola podria capturar otras fechas).
            ("fechadenacimiento",      "fechaNacimiento"),
            ("fechanacimiento",        "fechaNacimiento"),
            ("fecnacimiento",          "fechaNacimiento"),
            ("fechanac",               "fechaNacimiento"),
            ("fecnac",                 "fechaNacimiento"),
            ("fnacimiento",            "fechaNacimiento"),
            ("fechadenac",             "fechaNacimiento"),
            ("nacimiento",             "fechaNacimiento"),
            // Estado civil.
            ("estadocivil",            "estadoCivil"),
            ("estciv",                 "estadoCivil"),
            ("edocivil",               "estadoCivil"),
            // Contacto/telefono de emergencia (antes de telefono generico).
            ("telefonoemergencia",     "telefonoEmergencia"),
            ("telefonodeemergencia",   "telefonoEmergencia"),
            ("telacudiente",           "telefonoEmergencia"),
            ("telefonoacudiente",      "telefonoEmergencia"),
            ("celularacudiente",       "telefonoEmergencia"),
            ("celemergencia",          "telefonoEmergencia"),
            ("contactoemergencia",     "contactoEmergencia"),
            ("acompanante",            "contactoEmergencia"),
            ("acudiente",              "contactoEmergencia"),
            ("responsable",            "contactoEmergencia"),
            ("encasodeemergencia",     "contactoEmergencia"),
            ("encasoemergencia",       "contactoEmergencia"),
            ("parentesco",             "parentesco"),
            // Nombres y apellidos (antes que "nombre" generico, que es lo mas amplio).
            ("primernombre",           "primerNombre"),
            ("segundonombre",          "segundoNombre"),
            ("primerapellido",         "primerApellido"),
            ("segundoapellido",        "segundoApellido"),
            ("nombrecompleto",         "nombreCompleto"),
            ("nombresyapellidos",      "nombreCompleto"),
            ("nombresapellidos",       "nombreCompleto"),
            ("nombreyapellido",        "nombreCompleto"),
            ("nombresapell",           "nombreCompleto"),
            ("nomyapell",              "nombreCompleto"),
            ("nomyape",                "nombreCompleto"),
            ("nomape",                 "nombreCompleto"),
            ("nompaciente",            "nombreCompleto"),
            ("nombrepaciente",         "nombreCompleto"),
            ("nompac",                 "nombreCompleto"),
            ("nombres",                "nombreCompleto"),
            ("nombre",                 "nombreCompleto"),
            ("apellidos",              "nombreCompleto"),
            ("apellido",               "nombreCompleto"),
            ("paciente",               "nombreCompleto"),
            // Edad (calculada server-side desde FechaNacimiento por
            // PacientePrefillHelper). El form puede declarar el campo como
            // "edad", "edadanios", etc.
            ("edadanios",              "edad"),
            ("edadanos",               "edad"),
            ("edadenanos",             "edad"),
            ("edaddelpaciente",        "edad"),
            ("edad",                   "edad"),
            // Tipo de documento / numero de documento.
            ("tipodocumento",          "tipoDocumento"),
            ("tipodeidentificacion",   "tipoDocumento"),
            ("tipodocide",             "tipoDocumento"),
            ("tipdocide",              "tipoDocumento"),
            ("tipid",                  "tipoDocumento"),
            ("tipoid",                 "tipoDocumento"),
            ("tipdoc",                 "tipoDocumento"),
            ("tipodoc",                "tipoDocumento"),
            ("tipoidentificacion",     "tipoDocumento"),
            ("numerodeidentificacion", "numeroDocumento"),
            ("numerodocumento",        "numeroDocumento"),
            ("nrodocumento",           "numeroDocumento"),
            ("nrdocumento",            "numeroDocumento"),
            ("nrodoc",                 "numeroDocumento"),
            ("nrdoc",                  "numeroDocumento"),
            ("numdoc",                 "numeroDocumento"),
            ("nodoc",                  "numeroDocumento"),
            ("nodocide",               "numeroDocumento"),
            ("documentodeidentidad",   "numeroDocumento"),
            ("documentoidentidad",     "numeroDocumento"),
            ("documento",              "numeroDocumento"),
            ("identificacion",         "numeroDocumento"),
            ("identidad",              "numeroDocumento"),
            ("cedula",                 "numeroDocumento"),
            // Direccion / ciudad / zona.
            ("residencia",             "direccion"),
            ("lugarderesidencia",      "direccion"),
            ("direccion",              "direccion"),
            ("direccin",               "direccion"), // "direcci_n" -> "direccin" tras quitar separadores
            ("dir",                    "direccion"), // ultimo recurso; "dir" es ambiguo, mantener al final del grupo
            ("ciudad",                 "ciudad"),
            ("ciudadyfecha",           "ciudad"),
            ("municipio",              "ciudad"),
            ("urbanorural",            "zona"),
            ("zonaresidencia",         "zona"),
            ("zona",                   "zona"),
            // Sede.
            ("sedepaciente",           "sede"),
            ("sucursalpaciente",       "sede"),
            ("sede",                   "sede"),
            ("sucursal",               "sede"),
            // Ocupacion / profesion / regimen.
            ("ocupacion",              "ocupacion"),
            ("ocupacin",               "ocupacion"), // tras quitar tilde-separador
            // "profesion" SIN "al" final: matchea "profesion", "profesindelpac" pero
            // NO el campo "profesional" (rol del medico), que es legitimo aparte.
            ("profesiondel",           "ocupacion"),
            ("regimensubsidiado",      "regimen"),
            ("regimencontributivo",    "regimen"),
            ("regimen",                "regimen"),
            // Telefono / celular / email. Cuidado: "contacto" solo aplica al campo de
            // telefono cuando esta acompanado de telefono/numero/cel; demasiado generico
            // de lo contrario, asi que no lo metemos como fragmento suelto.
            ("telefonofijo",           "telefono"),
            ("telcontacto",            "telefono"),
            ("numtelefono",            "telefono"),
            ("notelefono",             "telefono"),
            ("telefono",               "telefono"),
            ("telefonn",               "telefono"), // tel_fonn etc.
            ("telefono",               "telefono"),
            ("telfono",                "telefono"), // "tel_fono" sin "_"
            ("celular",                "telefono"),
            ("celpaciente",            "telefono"),
            ("nocelular",              "telefono"),
            // "movil" SIN sufijos: evitamos falsos positivos con "movilidad" o
            // "movilizacion" exigiendo una variante mas restringida.
            ("telefonomovil",          "telefono"),
            ("numeromovil",            "telefono"),
            ("correoelectronico",      "email"),
            ("emailpaciente",          "email"),
            ("email",                  "email"),
            ("correo",                 "email"),
            ("mail",                   "email"),
            // Sexo / genero.
            ("sexo",                   "sexo"),
            ("genero",                 "sexo"),
            ("generop",                "sexo")
        };

        foreach (var (frag, campo) in reglas)
        {
            if (n.Contains(frag) || (l is not null && l.Contains(frag)))
            {
                return campo;
            }
        }
        return null;
    }

    private static string CanonPaciente(string normalized) => normalizadosACampo.TryGetValue(normalized, out var c) ? c : normalized;
    private static readonly Dictionary<string, string> normalizadosACampo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["numerodocumento"] = "numeroDocumento",
        ["tipodocumento"] = "tipoDocumento",
        ["nombrecompleto"] = "nombreCompleto",
        ["primernombre"] = "primerNombre",
        ["segundonombre"] = "segundoNombre",
        ["primerapellido"] = "primerApellido",
        ["segundoapellido"] = "segundoApellido",
        ["fechanacimiento"] = "fechaNacimiento",
        ["sexo"] = "sexo",
        ["estadocivil"] = "estadoCivil",
        ["telefono"] = "telefono",
        ["email"] = "email",
        ["direccion"] = "direccion",
        ["ciudad"] = "ciudad",
        ["zona"] = "zona",
        ["ocupacion"] = "ocupacion",
        ["regimen"] = "regimen",
        ["contactoemergencia"] = "contactoEmergencia",
        ["parentesco"] = "parentesco",
        ["telefonoemergencia"] = "telefonoEmergencia",
        ["sede"] = "sede"
    };

    /// <summary>Normaliza un string para matching: minusculas + sin tildes + sin separadores.</summary>
    private static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return ""; }
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) { continue; }
            if (ch == '_' || ch == '-' || ch == ' ' || ch == '.' || ch == ':') { continue; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>Recorre el SchemaJson y devuelve los pares (Name, Label) de los campos hoja.</summary>
    private static List<(string name, string? label)> ExtraerNamesDelSchema(string? schemaJson)
    {
        var result = new List<(string, string?)>();
        if (string.IsNullOrWhiteSpace(schemaJson)) { return result; }
        JsonNode? root;
        try { root = JsonNode.Parse(schemaJson); } catch { return result; }
        if (root is not JsonObject) { return result; }

        var children = root["children"] as JsonArray;
        if (children is null) { return result; }
        Recurse(children);
        return result;

        void Recurse(JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject obj) { continue; }
                var type = obj["type"]?.GetValue<string>() ?? "field";
                if (string.Equals(type, "section", StringComparison.OrdinalIgnoreCase))
                {
                    if (obj["children"] is JsonArray sub) { Recurse(sub); }
                    continue;
                }
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) { continue; }
                // Las tablas repetibles no se mapean a paciente porque cada fila se diligencia aparte.
                var fieldType = obj["fieldType"]?.GetValue<string>();
                if (string.Equals(fieldType, "table", StringComparison.OrdinalIgnoreCase)) { continue; }

                var name = obj["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) { continue; }
                var label = obj["label"]?.GetValue<string>();
                result.Add((name!, label));
            }
        }
    }

    private static JsonArray ParsePrefillRoutes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new JsonArray(); }
        try
        {
            var root = JsonNode.Parse(json);
            if (root is JsonObject obj && obj["routes"] is JsonArray arr)
            {
                // Hay que clonar porque no podemos reusar nodos que ya tienen parent.
                var clone = new JsonArray();
                foreach (var n in arr)
                {
                    if (n is null) { continue; }
                    clone.Add(JsonNode.Parse(n.ToJsonString())!);
                }
                return clone;
            }
        }
        catch { /* json invalido: arrancamos en blanco */ }
        return new JsonArray();
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.FormDefinitions.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        _db.FormDefinitions.Remove(existing);
        _audit.Write(actorUserId, "form-definition.delete", nameof(FormDefinition), existing.Id,
            previousValue: new { existing.Codigo, existing.Nombre }, newValue: null, tenantId: existing.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

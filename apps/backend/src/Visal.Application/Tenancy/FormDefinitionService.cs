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

    public async Task<FormDefinitionDetailDto?> GetActivoByCodigoSecundarioAsync(string codigoSecundario, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codigoSecundario)) { return null; }
        var c = codigoSecundario.Trim();
        return await _db.FormDefinitions
            .AsNoTracking()
            .Where(f => f.Activo && f.CodigoSecundario == c)
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

            // Tambien preparamos las rutas firmaPaciente / firmaProfesional /
            // historiaMedica / contrato / firmaAcompanante1. Lazy: solo se crean
            // si hay al menos un match.
            JsonObject? rutaFirmaPac = null, rutaFirmaProf = null, rutaHm = null, rutaContrato = null, rutaSistema = null, rutaFirmaAcomp1 = null;
            JsonArray? mappingsFirmaPac = null, mappingsFirmaProf = null, mappingsHm = null, mappingsContrato = null, mappingsSistema = null, mappingsFirmaAcomp1 = null;

            int agregadosEnEsteForm = 0;
            foreach (var (name, label) in names)
            {
                if (targetsExistentes.Contains(name)) { continue; } // mapeo manual: respetar

                // Orden de prueba: firma acompanante (mas especifico), firma paciente,
                // firma profesional, paciente. Acompanante primero porque nombres
                // como "firmaAcompananteMadre" contienen "firma" y podrian caer en
                // firmaPaciente si dejamos ese chequeo antes.
                var acompMatch = InferirCampoFirmaAcompanante(name, label);
                if (acompMatch is not null)
                {
                    EnsureRuta(routes, acompMatch.Value.SourceModule, "Firma Acompanante 1", ref rutaFirmaAcomp1, ref mappingsFirmaAcomp1);
                    mappingsFirmaAcomp1!.Add(new JsonObject { ["source"] = acompMatch.Value.SourceField, ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }
                if (InferirCampoFirmaPaciente(name, label))
                {
                    EnsureRuta(routes, "firmaPaciente", "Firma del Paciente", ref rutaFirmaPac, ref mappingsFirmaPac);
                    mappingsFirmaPac!.Add(new JsonObject { ["source"] = "url", ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }
                if (InferirCampoFirmaProfesional(name, label))
                {
                    EnsureRuta(routes, "firmaProfesional", "Firma del Profesional", ref rutaFirmaProf, ref mappingsFirmaProf);
                    mappingsFirmaProf!.Add(new JsonObject { ["source"] = "url", ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }

                // Historia medica: medicamentos / remisiones / incapacidades /
                // certificaciones / ordenes de servicio. Se llenan en tiempo real
                // cuando el doctor agrega items en los tabs respectivos del HC.
                var hmSource = InferirCampoHistoriaMedica(name, label);
                if (hmSource is not null)
                {
                    EnsureRuta(routes, "historiaMedica", "Historia Medica", ref rutaHm, ref mappingsHm);
                    mappingsHm!.Add(new JsonObject { ["source"] = hmSource, ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }

                // Sistema: fecha, hora, sede y agencia del momento de iniciar
                // el formulario. Util sobre todo en escalas/evoluciones.
                var sistemaSource = InferirCampoSistema(name, label);
                if (sistemaSource is not null)
                {
                    EnsureRuta(routes, "sistema", "Sistema", ref rutaSistema, ref mappingsSistema);
                    mappingsSistema!.Add(new JsonObject { ["source"] = sistemaSource, ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }

                // Contrato vigente del paciente: codigo y aseguradora.
                var contratoSource = InferirCampoContrato(name, label);
                if (contratoSource is not null)
                {
                    EnsureRuta(routes, "contrato", "Contrato", ref rutaContrato, ref mappingsContrato);
                    mappingsContrato!.Add(new JsonObject { ["source"] = contratoSource, ["target"] = name });
                    targetsExistentes.Add(name);
                    agregadosEnEsteForm++;
                    continue;
                }

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

            // Aseguramos que las rutas tengan los arrays actualizados.
            ruta["mappings"] = mappingsArr;
            if (rutaFirmaPac is not null && mappingsFirmaPac is not null) { rutaFirmaPac["mappings"] = mappingsFirmaPac; }
            if (rutaFirmaProf is not null && mappingsFirmaProf is not null) { rutaFirmaProf["mappings"] = mappingsFirmaProf; }
            if (rutaFirmaAcomp1 is not null && mappingsFirmaAcomp1 is not null) { rutaFirmaAcomp1["mappings"] = mappingsFirmaAcomp1; }
            if (rutaHm is not null && mappingsHm is not null) { rutaHm["mappings"] = mappingsHm; }
            if (rutaContrato is not null && mappingsContrato is not null) { rutaContrato["mappings"] = mappingsContrato; }
            if (rutaSistema is not null && mappingsSistema is not null) { rutaSistema["mappings"] = mappingsSistema; }

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

    /// <summary>Asegura que exista la ruta con el sourceModule indicado en el array de
    /// rutas. Si no existe la crea (con un id y nombre legible) y asigna sus referencias
    /// out a los punteros del caller. Si ya existia, reutiliza sus mappings actuales.</summary>
    private static void EnsureRuta(JsonArray routes, string sourceModule, string nombre, ref JsonObject? ruta, ref JsonArray? mappings)
    {
        if (ruta is not null) { return; }
        var existente = routes.FirstOrDefault(r =>
            string.Equals(r?["sourceModule"]?.GetValue<string>(), sourceModule, StringComparison.OrdinalIgnoreCase)) as JsonObject;
        if (existente is not null)
        {
            ruta = existente;
            mappings = (ruta["mappings"] as JsonArray) ?? new JsonArray();
            return;
        }
        ruta = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("N")[..8],
            ["name"] = nombre,
            ["sourceModule"] = sourceModule,
            ["mappings"] = new JsonArray()
        };
        routes.Add(ruta);
        mappings = (JsonArray)ruta["mappings"]!;
    }

    /// <summary>Heuristica para detectar si un campo del formulario es la firma del paciente.
    /// Devuelve true cuando matchea. La lista es extensible: agregar nuevos fragmentos al
    /// array conforme aparezcan nombres legacy en formularios reales del cliente.</summary>
    private static bool InferirCampoFirmaPaciente(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);
        // Fragmentos en orden de MAS especifico a mas generico. Importante que firma
        // profesional/medico/doctor NO matcheen aqui.
        var fragmentos = new[]
        {
            "firmapaciente", "firmapac", "firmapacient",
            "firmausuario", "firmacliente", "firmadelpaciente",
            "firmadelusuario", "firmadelacudiente", "firmaacudiente",
            "firmarecibe", "firmaderecibido", "firmaderecibo",
            "huellapaciente", "huelladelpaciente",
            "firmapacciente", // typo comun en formularios legacy
            "signaturepaciente", "signaturepac", "signaturepatient"
        };
        foreach (var f in fragmentos)
        {
            if (n.Contains(f) || (l is not null && l.Contains(f))) { return true; }
        }
        return false;
    }

    /// <summary>Heuristica para detectar campos "acompanante" en consentimientos:
    /// firmaAcompanante -> firmaAcompanante1.url,
    /// nombreAcompanante -> firmaAcompanante1.nombre,
    /// parentescoAcompanante -> firmaAcompanante1.parentesco.
    /// Retorna (sourceModule, sourceField) o null. Cubre variantes comunes:
    /// acudiente/acompanante/familiar/madre/padre/responsable. Auto-enlaza al slot 1
    /// por default; el admin puede reasignar a 2/3/4 desde el modal Rutas prefill.</summary>
    private static (string SourceModule, string SourceField)? InferirCampoFirmaAcompanante(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);

        // El campo es de tipo "acompanante" si menciona uno de estos actores.
        // OJO: "acudiente" solo aca — la firma del paciente ya tiene "firmaacudiente"
        // en su lista y matchea antes (orden importa).
        var esAcompanante = false;
        foreach (var frag in new[] { "acompanante", "acompaniante", "acompan", "familiar", "madre", "padre", "responsablefamiliar" })
        {
            if (n.Contains(frag) || (l is not null && l.Contains(frag))) { esAcompanante = true; break; }
        }
        if (!esAcompanante) { return null; }

        // Un campo con "firma" → url. Sin "firma" pero con "parentesco" → parentesco.
        // Sin "firma" ni "parentesco" pero con "nombre" → nombre.
        if (n.Contains("firma") || (l is not null && l.Contains("firma"))) { return ("firmaAcompanante1", "url"); }
        if (n.Contains("parentesco") || (l is not null && l.Contains("parentesco"))) { return ("firmaAcompanante1", "parentesco"); }
        if (n.Contains("nombre") || (l is not null && l.Contains("nombre"))) { return ("firmaAcompanante1", "nombre"); }
        return null;
    }

    /// <summary>Heuristica para detectar si un campo del formulario es la firma del profesional.
    /// Devuelve true cuando matchea. La lista es extensible.</summary>
    private static bool InferirCampoFirmaProfesional(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);
        var fragmentos = new[]
        {
            "firmaprofesional", "firmaespecialista", "firmaprof",
            "firmadoctor", "firmadr", "firmamedico", "firmadelmedico",
            "firmaterapeuta", "firmaenfermera", "firmaenfermero", "firmaenf",
            "firmafisioterapeuta", "firmafonoaudiologa", "firmafonoaudiologo",
            "firmaterapeutaocupacional",
            "firmaresponsable", // a veces "firma del responsable" = el profesional
            "firmaquienatiende", "firmaquienatendio",
            "signaturedoctor", "signatureprof", "signaturemedico"
        };
        foreach (var f in fragmentos)
        {
            if (n.Contains(f) || (l is not null && l.Contains(f))) { return true; }
        }
        return false;
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

    /// <summary>Heuristica para detectar si un campo del formulario es uno de los
    /// "agregados clinicos" de la HC: medicamentos, remisiones, incapacidades,
    /// certificaciones, ordenes de servicio. Devuelve el source del catalogo
    /// historiaMedica (ej. "medicamentos.lista_numerada") o null si no aplica.</summary>
    private static string? InferirCampoHistoriaMedica(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);
        // Reglas de mas especifico a mas generico. El primer match gana.
        // Orden importante: "ordendemedicamentos" antes de "medicamento" suelto.
        var reglas = new (string fragmento, string campo)[]
        {
            // Ordenes de servicios. Las tablas dedicadas (laboratorios, insumos)
            // tienen su propia entrada en el form pero NO comparten fuente con
            // ordenes_servicio — quedan sin auto-mapeo (placeholders editables).
            ("ordenservicios",        "ordenes_servicio.lista_numerada"),
            ("ordensservicios",       "ordenes_servicio.lista_numerada"),
            ("ordendeservicios",      "ordenes_servicio.lista_numerada"),
            ("ordendeservicio",       "ordenes_servicio.lista_numerada"),
            ("ordenesservicios",      "ordenes_servicio.lista_numerada"),
            ("serviciossolicitados",  "ordenes_servicio.lista_numerada"),
            ("serviciospropios",      "ordenes_servicio.lista_numerada"),
            ("ordensolicitada",       "ordenes_servicio.lista_numerada"),
            ("servicios",             "ordenes_servicio.lista_numerada"),
            // Insumos: tabla dedicada con su propia fuente backend.
            ("ordeninsumos",          "insumos.lista_numerada"),
            ("ordendeinsumos",        "insumos.lista_numerada"),
            ("insumos",               "insumos.lista_numerada"),
            ("insumo",                "insumos.lista_numerada"),
            // Remisiones / interconsultas.
            ("ordenremisiones",       "remisiones.lista_numerada"),
            ("ordenremision",         "remisiones.lista_numerada"),
            ("remisiones",            "remisiones.lista_numerada"),
            ("remision",              "remisiones.lista_numerada"),
            ("interconsultas",        "remisiones.lista_numerada"),
            ("interconsulta",         "remisiones.lista_numerada"),
            // Incapacidades.
            ("ordenincapacidad",      "incapacidades.lista_numerada"),
            ("ordendeincapacidad",    "incapacidades.lista_numerada"),
            ("incapacidades",         "incapacidades.lista_numerada"),
            ("incapacidad",           "incapacidades.lista_numerada"),
            // Certificaciones / certificados.
            ("ordencertificacion",    "certificaciones.lista_numerada"),
            ("certificaciones",       "certificaciones.lista_numerada"),
            ("certificacion",         "certificaciones.lista_numerada"),
            ("certificados",          "certificaciones.lista_numerada"),
            ("certificado",           "certificaciones.lista_numerada"),
            // Medicamentos / formula / tratamiento / receta. Se chequean al final
            // porque "tratamiento" es muy generico.
            ("ordenmedicamentos",     "medicamentos.lista_numerada"),
            ("ordendemedicamentos",   "medicamentos.lista_numerada"),
            ("ordenmedicamento",      "medicamentos.lista_numerada"),
            ("medicamentos",          "medicamentos.lista_numerada"),
            ("medicamento",           "medicamentos.lista_numerada"),
            ("formulamedica",         "medicamentos.lista_numerada"),
            ("formulamed",            "medicamentos.lista_numerada"),
            ("recetario",             "medicamentos.lista_numerada"),
            ("receta",                "medicamentos.lista_numerada"),
            ("medicacion",            "medicamentos.lista_numerada"),
            ("plan_de_manejo",        "medicamentos.lista_numerada"),
            ("planmanejo",            "medicamentos.lista_numerada"),
            ("planfarmacologico",     "medicamentos.lista_numerada"),
            ("tratamientofarmaco",    "medicamentos.lista_numerada"),
            ("tratamiento",           "medicamentos.lista_numerada")
        };
        foreach (var (frag, campo) in reglas)
        {
            if (n.Contains(frag) || (l is not null && l.Contains(frag))) { return campo; }
        }
        return null;
    }

    /// <summary>Heuristica para detectar si un campo es del contexto del sistema
    /// (fecha, hora, sede, agencia, usuario). Devuelve el source o null. Es
    /// agresiva con "fecha" y "hora" porque casi siempre que aparecen sueltos
    /// en formularios clinicos se refieren al momento de aplicacion.</summary>
    private static string? InferirCampoSistema(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);
        var reglas = new (string fragmento, string campo)[]
        {
            // Fecha + hora combinadas — chequear antes de "fecha" y "hora"
            // sueltas para no quedarse con la primera variante.
            ("fechahora",            "fechaHoraActual"),
            ("fechaaplicacion",      "fechaActual"),
            ("fechadeaplicacion",    "fechaActual"),
            ("fechadediligenciamiento", "fechaActual"),
            ("fechadelvaloracion",   "fechaActual"),
            ("fechavaloracion",      "fechaActual"),
            ("fechaescala",          "fechaActual"),
            ("fechaevolucion",       "fechaActual"),
            ("fechaconsentimiento",  "fechaActual"),
            ("fechadenacimiento",    null!),   // se descarta — la maneja PacientePrefillHelper
            ("fechanacimiento",      null!),
            ("fec_nac",              null!),
            ("fecnac",               null!),
            ("fechaapertura",        null!),   // la pone HeaderAutoFillHelper
            ("fechacierre",          null!),
            ("fechaexpedicion",      null!),
            ("fechavencimiento",     null!),
            ("fechadesde",           null!),   // incapacidades
            ("fechahasta",           null!),
            ("fec_eve",              null!),
            ("fecev",                null!),
            ("feccita",              null!),
            ("feccons",              null!),
            ("fecprog",              null!),
            ("fecasig",              null!),
            // Quedan los "fecha"/"hora" genericos para el sistema.
            ("horaaplicacion",       "horaActual"),
            ("horavaloracion",       "horaActual"),
            ("horainicio",           "horaActual"),
            ("horadeaplicacion",     "horaActual"),
            ("horaescala",           "horaActual"),
            ("hora",                 "horaActual"),
            ("fecha",                "fechaActual"),
            // Sede / agencia / usuario.
            ("agenciaslogan",        "agenciaSlogan"),
            ("agencia",              "agenciaNombre"),
            ("nombreagencia",        "agenciaNombre"),
            ("sedeatencion",         "sedeNombre"),
            ("sedeciudad",           "sedeCiudad"),
            ("sede",                 "sedeNombre"),
            ("nombresede",           "sedeNombre"),
            ("usuarioemail",         "usuarioEmail"),
            ("emailusuario",         "usuarioEmail"),
            ("nombreusuario",        "usuarioNombre"),
            ("usuario",              "usuarioNombre")
        };
        foreach (var (frag, campo) in reglas)
        {
            if (n.Contains(frag) || (l is not null && l.Contains(frag)))
            {
                // null como campo es una marca de "ignorar esta regla y no
                // mapear a sistema" — para no robar campos que ya manejan
                // PacientePrefillHelper / HeaderAutoFillHelper.
                return campo;
            }
        }
        return null;
    }

    /// <summary>Heuristica para detectar si un campo es del contrato vigente del
    /// paciente. Devuelve el source del catalogo contrato o null.</summary>
    private static string? InferirCampoContrato(string name, string? label)
    {
        var n = Normalizar(name);
        var l = Normalizar(label);
        var reglas = new (string fragmento, string campo)[]
        {
            ("codigocontrato",        "codigoContrato"),
            ("nrocontrato",           "codigoContrato"),
            ("numerocontrato",        "codigoContrato"),
            ("contratoaseguradora",   "codigoContrato"),
            ("contrato",              "codigoContrato"),
            // EPS / aseguradora — solo si NO ya capturado por paciente.eps
            ("nombreaseguradora",     "aseguradoraNombre"),
            ("aseguradora",           "aseguradoraNombre"),
            ("nombreeps",             "aseguradoraNombre"),
            ("eapb",                  "aseguradoraNombre")
        };
        foreach (var (frag, campo) in reglas)
        {
            if (n.Contains(frag) || (l is not null && l.Contains(frag))) { return campo; }
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

    // ============================================================================
    // Exportar / Importar JSON. Formato portable {codigo,tipo,schema,prefill}.
    // El objetivo es que un formulario exportado en un tenant pueda importarse en
    // otro sin perder NADA: campos, tablas, seedRows, formulas del schema, rutas
    // de prefill. Todo lo que hoy vive en FormDefinition (jsonb columns) viaja.
    // ============================================================================

    private static readonly JsonSerializerOptions _exportJsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<byte[]?> ExportarJsonAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var e = await _db.FormDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null) { return null; }

        // Deserializamos schema y prefill a JsonNode para que se re-serialicen
        // como objetos anidados (no como strings escapados) — asi el JSON de
        // exportacion se lee legible y en la importacion no hay que doble-parsear.
        JsonNode? schemaNode = null;
        try { schemaNode = string.IsNullOrWhiteSpace(e.SchemaJson) ? null : JsonNode.Parse(e.SchemaJson); }
        catch { schemaNode = null; }
        JsonNode? prefillNode = null;
        try { prefillNode = string.IsNullOrWhiteSpace(e.PrefillRoutesJson) ? null : JsonNode.Parse(e.PrefillRoutesJson); }
        catch { prefillNode = null; }

        var payload = new JsonObject
        {
            ["$schema"] = "visal-form-export/v1",
            ["exportedAt"] = DateTimeOffset.UtcNow.ToString("o"),
            ["codigo"] = e.Codigo,
            ["codigoSecundario"] = e.CodigoSecundario,
            ["nombre"] = e.Nombre,
            ["version"] = e.Version,
            ["tipo"] = e.Tipo,
            ["activo"] = e.Activo,
            ["schema"] = schemaNode,
            ["prefillRoutes"] = prefillNode
        };

        var json = payload.ToJsonString(_exportJsonOpts);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public async Task<string?> GetExportFileNameAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var codigo = await _db.FormDefinitions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.Codigo)
            .FirstOrDefaultAsync(cancellationToken);
        if (codigo is null) { return null; }
        // Sanitiza para nombre de archivo (evita / \ : etc.)
        var safe = new string(codigo.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.').ToArray());
        if (string.IsNullOrWhiteSpace(safe)) { safe = id.ToString("N"); }
        return $"form-{safe}.json";
    }

    public async Task<FormDefinitionDetailDto?> ImportarJsonAsync(byte[] jsonBytes, bool sobrescribir, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (jsonBytes is null || jsonBytes.Length == 0) { throw new InvalidOperationException("Archivo vacio."); }

        JsonObject root;
        try
        {
            var parsed = JsonNode.Parse(jsonBytes) as JsonObject;
            root = parsed ?? throw new InvalidOperationException("El JSON debe ser un objeto raiz.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("JSON invalido: " + ex.Message);
        }

        var codigo = root["codigo"]?.GetValue<string>()?.Trim();
        var nombre = root["nombre"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre))
        {
            throw new InvalidOperationException("El JSON debe traer 'codigo' y 'nombre'.");
        }
        var codigoSecundario = root["codigoSecundario"]?.GetValue<string>();
        var version = root["version"]?.GetValue<string>();
        var tipo = root["tipo"]?.GetValue<string>();
        var activo = root["activo"]?.GetValue<bool>() ?? true;

        // schema y prefillRoutes pueden venir como objeto anidado (formato preferido)
        // o como string escapado (compatibilidad); re-serializamos siempre a string
        // porque la BD guarda jsonb como texto.
        static string? NodeToJsonString(JsonNode? n)
        {
            if (n is null) { return null; }
            if (n is JsonValue v && v.TryGetValue<string>(out var s)) { return s; }
            return n.ToJsonString();
        }
        var schemaJson = NodeToJsonString(root["schema"]) ?? "{\"children\":[]}";
        var prefillRoutesJson = NodeToJsonString(root["prefillRoutes"]);

        // Sufijo para evitar colision cuando no se pide sobrescribir.
        var codigoFinal = codigo;
        var existente = await _db.FormDefinitions
            .FirstOrDefaultAsync(x => x.Codigo == codigo, cancellationToken);
        if (existente is not null)
        {
            if (sobrescribir)
            {
                existente.CodigoSecundario = codigoSecundario;
                existente.Nombre = nombre;
                existente.Version = version;
                existente.Tipo = tipo;
                existente.Activo = activo;
                existente.SchemaJson = schemaJson;
                existente.PrefillRoutesJson = prefillRoutesJson;
                await _db.SaveChangesAsync(cancellationToken);
                _audit.Write(actorUserId, "form-definition.import.overwrite", nameof(FormDefinition), existente.Id,
                    previousValue: new { codigo = existente.Codigo }, newValue: new { codigo, tipo }, tenantId: tenantId);
                return await GetAsync(existente.Id, cancellationToken);
            }
            // Encuentra un codigo libre agregando -import, -import-2, etc.
            var i = 1;
            do
            {
                codigoFinal = i == 1 ? $"{codigo}-import" : $"{codigo}-import-{i}";
                i++;
            } while (await _db.FormDefinitions.AnyAsync(x => x.Codigo == codigoFinal, cancellationToken));
        }

        var nuevo = new FormDefinition
        {
            TenantId = tenantId,
            Codigo = codigoFinal!,
            CodigoSecundario = codigoSecundario,
            Nombre = nombre!,
            Version = version,
            Tipo = tipo,
            Activo = activo,
            SchemaJson = schemaJson,
            PrefillRoutesJson = prefillRoutesJson
        };
        _db.FormDefinitions.Add(nuevo);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Write(actorUserId, "form-definition.import.new", nameof(FormDefinition), nuevo.Id,
            previousValue: null,
            newValue: new { codigo = codigoFinal, codigoOriginal = codigo, tipo, colision = existente is not null && !sobrescribir },
            tenantId: tenantId);
        return await GetAsync(nuevo.Id, cancellationToken);
    }
}

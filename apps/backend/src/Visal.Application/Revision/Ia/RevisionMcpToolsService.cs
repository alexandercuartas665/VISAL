using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Revision.Ia;

/// <summary>
/// Implementacion de los 9 tools MCP de solo lectura para el agente REVISOR CLINICO IA.
/// Todas las consultas dependen del global query filter del DbContext para
/// aislar por tenant. La allow-list se valida por nombre antes de ejecutar
/// cualquier query; los tools no permitidos devuelven <see cref="ToolInvocationResult.Ok"/>=false.
///
/// Los payloads JSON son compactos y estables (no anidan objetos grandes de EF);
/// el objetivo es alimentar el prompt del LLM sin ruido.
/// </summary>
// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed partial class RevisionMcpToolsService : IRevisionMcpToolsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContextFactory _dbFactory;

    public RevisionMcpToolsService(IApplicationDbContextFactory dbFactory) => _dbFactory = dbFactory;

    public async Task<HistoriaClinicaContextoIa> ArmarContextoAsync(
        Guid historiaClinicaId,
        IReadOnlyCollection<string> toolNames,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default,
        OpcionesHistorial? historial = null)
    {
        using var db = _dbFactory.Create();
        var okList = new List<ToolInvocationResult>();
        var failList = new List<ToolInvocationResult>();
        foreach (var name in toolNames)
        {
            var r = await EjecutarToolCoreAsync(db, name, historiaClinicaId, allowedTools, ventanaAsignacionesDias, ct);
            (r.Ok ? okList : failList).Add(r);
        }
        // Historial del paciente: fuera de la allow-list, lo gobierna el interruptor de la politica.
        if (historial is { Activo: true })
        {
            try
            {
                var payload = await ArmarHistorialPacienteAsync(db, historiaClinicaId, historial, ct);
                okList.Add(new ToolInvocationResult(RevisionMcpToolNames.HistorialPaciente, true, payload, null,
                    Math.Max(1, (int)(payload.Length / CharsPorToken))));
            }
            catch (Exception ex)
            {
                failList.Add(new ToolInvocationResult(RevisionMcpToolNames.HistorialPaciente, false, null, ex.Message, 0));
            }
        }
        return new HistoriaClinicaContextoIa(historiaClinicaId, okList, failList);
    }

    public async Task<ToolInvocationResult> EjecutarToolAsync(
        string toolName,
        Guid historiaClinicaId,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await EjecutarToolCoreAsync(db, toolName, historiaClinicaId, allowedTools, ventanaAsignacionesDias, ct);
    }

    private async Task<ToolInvocationResult> EjecutarToolCoreAsync(
        IApplicationDbContext db,
        string toolName,
        Guid historiaClinicaId,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default)
    {
        if (!allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return new ToolInvocationResult(toolName, false, null,
                $"Tool '{toolName}' no esta en la allow-list del agente.", 0);
        }

        try
        {
            var payload = toolName switch
            {
                RevisionMcpToolNames.GetHistoriaClinica => await GetHistoriaClinicaAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.GetPaciente => await GetPacienteAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListOrdenesHc => await ListOrdenesHcAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListNotasHc => await ListNotasHcAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListEscalasHc => await ListEscalasHcAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListEvolucionesHc => await ListEvolucionesHcAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListConsentimientosHc => await ListConsentimientosHcAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListAsignacionesRelacionadas => await ListAsignacionesRelacionadasAsync(
                    db, historiaClinicaId, ventanaAsignacionesDias ?? 30, ct),
                RevisionMcpToolNames.GetFormDefinition => await GetFormDefinitionAsync(db, historiaClinicaId, ct),
                RevisionMcpToolNames.ListDocumentosHc => await ListDocumentosHcAsync(db, historiaClinicaId, ct),
                _ => null,
            };
            if (payload is null)
            {
                return new ToolInvocationResult(toolName, false, null,
                    $"Tool '{toolName}' no esta implementado.", 0);
            }
            // Estimacion muy simple de tokens: ~4 chars por token. Sirve como
            // presupuesto rough para el orquestador, no reemplaza el conteo real.
            var tokens = Math.Max(1, payload.Length / 4);
            return new ToolInvocationResult(toolName, true, payload, null, tokens);
        }
        catch (Exception ex)
        {
            return new ToolInvocationResult(toolName, false, null, ex.Message, 0);
        }
    }

    // ---- Tools ----

    private static async Task<string?> GetHistoriaClinicaAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new
            {
                h.Id,
                h.PacienteId,
                h.FormDefinitionId,
                Estado = h.Estado.ToString(),
                h.FechaApertura,
                h.FechaCierre,
                h.FechaAtencion,
                h.EspecialistaNombre,
                h.RipsViaIngresoCodigo,
                h.RipsViaIngresoNombre,
                h.RipsFinalidadCodigo,
                h.RipsFinalidadNombre,
                h.RipsCausaExternaCodigo,
                h.RipsCausaExternaNombre,
                h.ValoresJson,
            })
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return null; }

        // Fechas con nombres AUTO-EXPLICATIVOS para que el agente NO confunda la fecha del servicio
        // con las administrativas: la fecha de la atencion es SIEMPRE fecha_atencion_servicio;
        // apertura/cierre son solo marcas del registro y NO son la fecha del servicio.
        var payload = new
        {
            hc.Id,
            hc.PacienteId,
            hc.FormDefinitionId,
            hc.Estado,
            fechas = new
            {
                fecha_atencion_servicio = hc.FechaAtencion,   // LA fecha del servicio (usar SIEMPRE esta)
                apertura_registro_admin = hc.FechaApertura,   // administrativa, NO es la atencion
                cierre_registro_admin = hc.FechaCierre,       // administrativa, NO es la atencion
                nota = "La fecha de la atencion/servicio es fecha_atencion_servicio. apertura/cierre son "
                     + "marcas administrativas del registro, NO la fecha del servicio.",
            },
            hc.EspecialistaNombre,
            hc.RipsViaIngresoCodigo,
            hc.RipsViaIngresoNombre,
            hc.RipsFinalidadCodigo,
            hc.RipsFinalidadNombre,
            hc.RipsCausaExternaCodigo,
            hc.RipsCausaExternaNombre,
            hc.ValoresJson,
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private static async Task<string?> GetPacienteAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var pacId = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return null; }
        var pac = await db.Pacientes.AsNoTracking()
            .Where(p => p.Id == pacId)
            .Select(p => new
            {
                p.Id,
                p.NombreCompleto,
                p.TipoDocumento,
                p.NumeroDocumento,
                p.FechaNacimiento,
                p.Sexo,
                p.Telefono,
                p.EstadoAdmision,
            })
            .FirstOrDefaultAsync(ct);
        return pac is null ? null : JsonSerializer.Serialize(pac, JsonOpts);
    }

    private static async Task<string> ListOrdenesHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        // Mismas 10 categorias de siempre (incluidas vacias), cargadas con el helper en lote que
        // tambien usa el historial del paciente. REV-9: incluye las ordenes EXTERNAS (a otra IPS).
        var porHc = await CargarOrdenesPorHcAsync(db, new[] { hcId }, ct);
        return JsonSerializer.Serialize(porHc[hcId], JsonOpts);
    }

    private static async Task<string> ListNotasHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        // Notas del paciente (no atadas a hcId directo, filtramos por PacienteId).
        var pacId = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return "[]"; }
        var notas = await db.NotasMedicas.AsNoTracking()
            .Where(n => n.PacienteId == pacId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(30)
            .Select(n => new
            {
                n.Id,
                n.CreatedAt,
                n.EspecialistaNombre,
                n.Contenido,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(notas, JsonOpts);
    }

    private static async Task<string> ListEscalasHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var escalas = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(escalas, JsonOpts);
    }

    private static async Task<string> ListEvolucionesHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var docs = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId && x.Tipo == "EVOLUCION")
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(docs, JsonOpts);
    }

    private static async Task<string> ListConsentimientosHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var docs = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId && x.Tipo == "CONSENTIMIENTO")
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(docs, JsonOpts);
    }

    private static async Task<string> ListAsignacionesRelacionadasAsync(IApplicationDbContext db, Guid hcId, int ventanaDias, CancellationToken ct)
    {
        var pacId = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return "[]"; }
        var hcFecha = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => h.FechaCierre ?? h.FechaApertura).FirstOrDefaultAsync(ct);
        var desde = hcFecha.AddDays(-ventanaDias);
        var asigs = await db.Asignaciones.AsNoTracking()
            .Where(a => a.PacienteId == pacId && a.CreatedAt >= desde && a.CreatedAt <= hcFecha)
            .Select(a => new
            {
                a.Id,
                a.TipoServicio,
                a.NombreServicio,
                a.CodigoAutorizacion,
                // CreatedAt = cuando se COORDINO/asigno el servicio; FechaInicio = inicio programado de
                // la asignacion; MesVigencia = mes de vigencia. Sirven para la congruencia temporal:
                // que la FechaAtencion de la HC caiga dentro de la vigencia/autorizacion del servicio.
                a.CreatedAt,
                a.FechaInicio,
                a.MesVigencia,
                a.FormatoHistoria,
                a.PaqueteCodigo,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(asigs, JsonOpts);
    }

    /// <summary>REV-13 — Metadata de los archivos subidos al tab "Documentos
    /// externos" de la HC (entidad <see cref="NotaMedicaDocumento"/> con
    /// <c>HistoriaClinicaId</c> seteado). Se devuelve solo la metadata (nombre,
    /// tipologia/categoria, tipo MIME, tamano, url servible) — nunca el binario.
    /// Sin esto el agente no ve resultados de laboratorio adjuntos, consentimientos
    /// escaneados, fotos de la valoracion, etc.</summary>
    private static async Task<string> ListDocumentosHcAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var docs = await db.NotaMedicaDocumentos.AsNoTracking()
            .Where(d => d.HistoriaClinicaId == hcId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.NombreOriginal,
                d.Categoria,
                d.TipoMime,
                d.Tamano,
                d.RutaArchivo,
                d.Anotaciones,
                d.CreatedAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(docs, JsonOpts);
    }

    /// <summary>
    /// Devuelve las CAPACIDADES del formato: su metadata + el CATALOGO DE CAMPOS que el formato
    /// define, YA CRUZADO con los valores de esta HC (requerido + estado lleno/vacio + valor). Antes
    /// se enviaba el SchemaJson crudo (verboso) y el agente tenia que cruzarlo el mismo con los
    /// valores, lo que lo hacia exigir campos que el formato ni siquiera tiene. Ahora el alcance de
    /// la auditoria de COMPLETITUD queda explicito y objetivo: estos y solo estos son los campos que
    /// el formato captura.
    /// </summary>
    private static async Task<string?> GetFormDefinitionAsync(IApplicationDbContext db, Guid hcId, CancellationToken ct)
    {
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new { h.FormDefinitionId, h.ValoresJson })
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return null; }
        var fd = await db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == hc.FormDefinitionId)
            .Select(f => new { f.Codigo, f.Nombre, f.Tipo, f.Version, f.SchemaJson })
            .FirstOrDefaultAsync(ct);
        if (fd is null) { return null; }

        var cat = BuildCatalogo(fd.SchemaJson, hc.ValoresJson);
        var requeridos = cat.Campos.Count(c => c.Requerido);
        var requeridosVacios = cat.Campos.Count(c => c.Requerido && c.Estado == "vacio");
        var llenos = cat.Campos.Count(c => c.Estado == "lleno");

        var payload = new
        {
            formato = new { fd.Codigo, fd.Nombre, fd.Tipo, fd.Version },
            nota = "Este formato captura EXACTAMENTE los campos listados en 'campos'. Evalua la "
                 + "completitud SOLO contra estos campos; NO exijas datos que el formato no define.",
            resumen = new
            {
                total = cat.Campos.Count,
                requeridos,
                requeridos_vacios = requeridosVacios,
                llenos,
                vacios = cat.Campos.Count - llenos,
            },
            // Fechas de atencion para el chequeo de congruencia temporal: la del HEADER ("Ciudad y
            // Fecha") contra la(s) reportada(s) como atencion en el CUERPO (campos IsFechaAtencion).
            fechas_atencion = new
            {
                nota = "Compara 'header' (fecha de atencion del encabezado 'Ciudad y Fecha') contra "
                     + "'cuerpo' (fechas marcadas como fecha de atencion dentro de la HC). DEBEN "
                     + "COINCIDIR; si difieren es un hallazgo de congruencia temporal. "
                     + "FechaApertura/FechaCierre NO son la fecha de atencion.",
                header = cat.HeaderFechas,
                cuerpo = cat.CuerpoFechas,
            },
            campos = cat.Campos,
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private sealed record CampoCatalogo(string Campo, string? Name, string Tipo, bool Requerido, string Estado, string? Valor, string? Origen = null);
    /// <summary>Marca de campo armado por la plantilla del formato (no redactado por el profesional).</summary>
    public const string OrigenPlantilla = "AUTOGENERADO por la plantilla del formato a partir de otros campos "
        + "(no lo redacto el profesional). Si se repite igual en varios turnos o secciones NO es copia/pega.";
    private sealed record FechaAtencionCampo(string Campo, string? Name, string? Valor);
    private sealed record CatalogoResult(List<CampoCatalogo> Campos, List<FechaAtencionCampo> HeaderFechas, List<FechaAtencionCampo> CuerpoFechas);

    /// <summary>Cataloga los campos de CAPTURA del formato (header + cuerpo, recursando secciones;
    /// excluye bloques de texto estatico) cruzados con <paramref name="valoresJson"/> (lleno/vacio +
    /// valor), y aparte identifica las FECHAS DE ATENCION: las del header (campo de cabecera cuyo
    /// label contiene "fecha", mismo criterio que FechaAtencionHelper) y las del cuerpo (campos con
    /// IsFechaAtencion=true), para que el agente las contraste. Firmas base64 colapsadas, valores
    /// largos truncados.</summary>
    private static CatalogoResult BuildCatalogo(string? schemaJson, string? valoresJson, int maxChars = 300)
    {
        var campos = new List<CampoCatalogo>();
        var headerFechas = new List<FechaAtencionCampo>();
        var cuerpoFechas = new List<FechaAtencionCampo>();

        Dictionary<string, string?> valores;
        try
        {
            valores = string.IsNullOrWhiteSpace(valoresJson)
                ? new Dictionary<string, string?>()
                : (JsonSerializer.Deserialize<Dictionary<string, string?>>(valoresJson!) ?? new());
        }
        catch { valores = new Dictionary<string, string?>(); }

        string? ValorDe(string key)
        {
            if (!valores.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) { return null; }
            if (key.Contains("firma", StringComparison.OrdinalIgnoreCase)) { return "(firma registrada)"; }
            var s = raw.Trim();
            return s.Length > maxChars ? s[..maxChars] + "..." : s;
        }

        var schema = FormSchema.FromJson(schemaJson);

        // Campos de cabecera (clave en valores = "hdr:{id}").
        if (schema.Header?.Campos is { Count: > 0 } hcampos)
        {
            foreach (var c in hcampos)
            {
                if (string.IsNullOrWhiteSpace(c.Label)) { continue; }
                var val = ValorDe("hdr:" + c.Id);
                campos.Add(new CampoCatalogo(
                    c.Label!, "hdr:" + c.Id, string.IsNullOrWhiteSpace(c.Tipo) ? "texto" : c.Tipo!,
                    false, val is null ? "vacio" : "lleno", val));
                // Fecha de atencion del header: campo de cabecera cuyo label contiene "fecha".
                if (c.Label!.IndexOf("fecha", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    headerFechas.Add(new FechaAtencionCampo(c.Label!, "hdr:" + c.Id, val));
                }
            }
        }

        // Campos del cuerpo (recursando secciones; los bloques "text" son estaticos, no son campos).
        void Recurse(IEnumerable<FormNode>? nodes)
        {
            if (nodes is null) { return; }
            foreach (var n in nodes)
            {
                if (n.IsSection) { Recurse(n.Children); continue; }
                if (n.IsText) { continue; }
                if (n.IsTable)
                {
                    // Las tablas NO guardan su valor bajo n.Name: cada celda vive en
                    // "tbl:{id}:{fila}:{colId}". Antes se buscaba n.Name y TODA tabla salia
                    // "vacio" (hallazgos falsos tipo "la tabla Diagnosticos esta vacia").
                    var labelT = !string.IsNullOrWhiteSpace(n.Label) ? n.Label!
                        : !string.IsNullOrWhiteSpace(n.Name) ? n.Name! : "Tabla";
                    var (txt, filas) = LeerTabla(n, valores, Math.Max(maxChars, 1500));
                    campos.Add(new CampoCatalogo(
                        labelT, n.Name ?? ("tbl:" + n.Id), $"tabla ({filas} filas diligenciadas)",
                        n.Required, filas > 0 ? "lleno" : "vacio", txt));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(n.Name)) { continue; }
                var val = ValorDe(n.Name!);
                var label = string.IsNullOrWhiteSpace(n.Label) ? n.Name! : n.Label!;
                // Texto que el FORMATO arma solo (FormulaTemplate, p.ej. "TA: x/y FC: ..." desde los
                // signos vitales): el espejo "__tmpl:{name}" guarda lo expandido; si coincide con el
                // valor, el profesional no lo escribio ni lo edito.
                string? origen = null;
                if (!string.IsNullOrWhiteSpace(n.FormulaTemplate)
                    && valores.TryGetValue("__tmpl:" + n.Name, out var espejo)
                    && !string.IsNullOrWhiteSpace(espejo)
                    && valores.TryGetValue(n.Name!, out var actual)
                    && string.Equals(espejo, actual, StringComparison.Ordinal))
                {
                    origen = OrigenPlantilla;
                }
                campos.Add(new CampoCatalogo(
                    label, n.Name,
                    string.IsNullOrWhiteSpace(n.FieldType) ? "text" : n.FieldType!,
                    n.Required, val is null ? "vacio" : "lleno", val, origen));
                // Fecha de atencion reportada en el cuerpo: campos marcados IsFechaAtencion.
                if (n.IsFechaAtencion)
                {
                    cuerpoFechas.Add(new FechaAtencionCampo(label, n.Name, val));
                }
            }
        }
        Recurse(schema.Children);

        return new CatalogoResult(campos, headerFechas, cuerpoFechas);
    }

    /// <summary>Lee las filas de una tabla del formato desde <paramref name="valores"/> (claves
    /// "tbl:{id}:{fila}:{colId}", mismo criterio que la impresion). Una fila cuenta como
    /// diligenciada solo si tiene alguna celda CAPTURADA (las celdas fijas de la plantilla
    /// -seedRows- son rotulos, no datos). Devuelve el texto "Col: valor; Col: valor" por fila
    /// (null si no hay filas) y cuantas filas tienen datos.</summary>
    public static (string? Texto, int Filas) LeerTabla(FormNode tabla, IReadOnlyDictionary<string, string?> valores, int maxChars = 1500)
    {
        var cols = tabla.Columns ?? new List<FormColumn>();
        if (cols.Count == 0) { return (null, 0); }
        var key = $"tbl:{tabla.Id}";
        var seed = tabla.SeedRows?.Count ?? 0;
        var extra = valores.TryGetValue($"{key}:_rows", out var rs) && int.TryParse(rs, out var p) && p > 0 ? p : 0;
        // Defensivo (igual que HcDiagnosticosReader): si _rows no esta seteado se escanea una
        // ventana amplia para no perder filas capturadas.
        var scanMax = Math.Max(seed + extra, 60);

        var sb = new StringBuilder();
        var filas = 0;
        for (var i = 0; i < scanMax; i++)
        {
            var seedRow = i < seed ? tabla.SeedRows![i] : null;
            if (seedRow is not null && tabla.SeedRowBanner is not null && tabla.SeedRowBanner.ContainsKey(i.ToString()))
            {
                continue; // fila-titulo de la plantilla
            }
            var partes = new List<string>();
            var capturada = false;
            for (var c = 0; c < cols.Count; c++)
            {
                var col = cols[c];
                if (string.Equals(col.FieldType, "calculated", StringComparison.OrdinalIgnoreCase)) { continue; }
                var seedVal = seedRow is not null && c < seedRow.Count ? seedRow[c] : null;
                string? val;
                if (!string.IsNullOrWhiteSpace(seedVal)) { val = seedVal; }
                else
                {
                    val = valores.TryGetValue($"{key}:{i}:{col.Id}", out var v) ? v : null;
                    if (!string.IsNullOrWhiteSpace(val)) { capturada = true; }
                }
                if (string.IsNullOrWhiteSpace(val)) { continue; }
                var lbl = string.IsNullOrWhiteSpace(col.Label) ? (col.Name ?? col.Id) : col.Label;
                partes.Add($"{lbl}: {val!.Trim()}");
            }
            if (!capturada) { continue; }
            filas++;
            if (sb.Length > 0) { sb.Append('\n'); }
            sb.Append(filas).Append(") ").Append(string.Join("; ", partes));
        }
        if (filas == 0) { return (null, 0); }
        var s = sb.ToString();
        return (s.Length > maxChars ? s[..maxChars] + "..." : s, filas);
    }
}

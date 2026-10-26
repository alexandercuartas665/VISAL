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
public sealed class RevisionMcpToolsService : IRevisionMcpToolsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContext _db;

    public RevisionMcpToolsService(IApplicationDbContext db) => _db = db;

    public async Task<HistoriaClinicaContextoIa> ArmarContextoAsync(
        Guid historiaClinicaId,
        IReadOnlyCollection<string> toolNames,
        IReadOnlyCollection<string> allowedTools,
        int? ventanaAsignacionesDias,
        CancellationToken ct = default)
    {
        var okList = new List<ToolInvocationResult>();
        var failList = new List<ToolInvocationResult>();
        foreach (var name in toolNames)
        {
            var r = await EjecutarToolAsync(name, historiaClinicaId, allowedTools, ventanaAsignacionesDias, ct);
            (r.Ok ? okList : failList).Add(r);
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
        if (!allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return new ToolInvocationResult(toolName, false, null,
                $"Tool '{toolName}' no esta en la allow-list del agente.", 0);
        }

        try
        {
            var payload = toolName switch
            {
                RevisionMcpToolNames.GetHistoriaClinica => await GetHistoriaClinicaAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.GetPaciente => await GetPacienteAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListOrdenesHc => await ListOrdenesHcAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListNotasHc => await ListNotasHcAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListEscalasHc => await ListEscalasHcAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListEvolucionesHc => await ListEvolucionesHcAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListConsentimientosHc => await ListConsentimientosHcAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListAsignacionesRelacionadas => await ListAsignacionesRelacionadasAsync(
                    historiaClinicaId, ventanaAsignacionesDias ?? 30, ct),
                RevisionMcpToolNames.GetFormDefinition => await GetFormDefinitionAsync(historiaClinicaId, ct),
                RevisionMcpToolNames.ListDocumentosHc => await ListDocumentosHcAsync(historiaClinicaId, ct),
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

    private async Task<string?> GetHistoriaClinicaAsync(Guid hcId, CancellationToken ct)
    {
        var hc = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new
            {
                h.Id,
                h.PacienteId,
                h.FormDefinitionId,
                Estado = h.Estado.ToString(),
                h.FechaApertura,
                h.FechaCierre,
                // FechaAtencion = fecha REAL del servicio (sale del header "Ciudad y Fecha"); puede
                // diferir de apertura/cierre, que son marcas administrativas. Es la fecha contra la
                // que el agente debe validar la congruencia temporal (no contra apertura/cierre).
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
        return JsonSerializer.Serialize(hc, JsonOpts);
    }

    private async Task<string?> GetPacienteAsync(Guid hcId, CancellationToken ct)
    {
        var pacId = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return null; }
        var pac = await _db.Pacientes.AsNoTracking()
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

    private async Task<string> ListOrdenesHcAsync(Guid hcId, CancellationToken ct)
    {
        var medicamentos = await _db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new
            {
                Codigo = x.CodigoMedicamento,
                Nombre = x.NombreMedicamento,
                x.Cantidad,
                x.Posologia,
                x.Frecuencia,
                x.Dias,
                x.Observacion,
            })
            .ToListAsync(ct);
        var servicios = await _db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.CodigoServicio, x.Descripcion, x.Cantidad, x.Observaciones })
            .ToListAsync(ct);
        var incapacidades = await _db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Motivo, x.FechaDesde, x.FechaHasta, x.Dias, x.Tipo })
            .ToListAsync(ct);
        var certificaciones = await _db.HistoriaClinicaCertificaciones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Titulo, x.Contenido })
            .ToListAsync(ct);
        var remisiones = await _db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Capitulo, x.EspecialidadCodigo, x.EspecialidadNombre, x.Cantidad, x.Motivo })
            .ToListAsync(ct);
        var insumos = await _db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones })
            .ToListAsync(ct);

        // REV-9 — Ordenes EXTERNAS (a otra IPS): la misma tabla
        // HistoriaClinicaOrdenesExternas discrimina por Tipo. Cargamos las 4
        // en una sola query y particionamos en memoria para no repetir I/O.
        // Sin esto, el agente REVISOR CLINICO IA no ve nada de lo que se pide
        // por fuera de la propia IPS.
        var externas = await _db.HistoriaClinicaOrdenesExternas.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Tipo, x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones })
            .ToListAsync(ct);
        var rxExternos = externas.Where(x => x.Tipo == TipoCatalogoServicio.RxImagenologia)
            .Select(x => new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones }).ToList();
        var laboratoriosExternos = externas.Where(x => x.Tipo == TipoCatalogoServicio.Laboratorio)
            .Select(x => new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones }).ToList();
        var serviciosExternos = externas.Where(x => x.Tipo == TipoCatalogoServicio.ServicioGeneral)
            .Select(x => new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones }).ToList();
        var insumosExternos = externas.Where(x => x.Tipo == TipoCatalogoServicio.Insumo)
            .Select(x => new { x.Codigo, x.Descripcion, x.Cantidad, x.Observaciones }).ToList();

        var payload = new
        {
            medicamentos, servicios, incapacidades, certificaciones, remisiones, insumos,
            rxExternos, laboratoriosExternos, serviciosExternos, insumosExternos,
        };
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private async Task<string> ListNotasHcAsync(Guid hcId, CancellationToken ct)
    {
        // Notas del paciente (no atadas a hcId directo, filtramos por PacienteId).
        var pacId = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return "[]"; }
        var notas = await _db.NotasMedicas.AsNoTracking()
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

    private async Task<string> ListEscalasHcAsync(Guid hcId, CancellationToken ct)
    {
        var escalas = await _db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId)
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(escalas, JsonOpts);
    }

    private async Task<string> ListEvolucionesHcAsync(Guid hcId, CancellationToken ct)
    {
        var docs = await _db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId && x.Tipo == "EVOLUCION")
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(docs, JsonOpts);
    }

    private async Task<string> ListConsentimientosHcAsync(Guid hcId, CancellationToken ct)
    {
        var docs = await _db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => x.HistoriaClinicaId == hcId && x.Tipo == "CONSENTIMIENTO")
            .Select(x => new { x.Id, x.FormDefinitionId, x.FechaApertura, x.FechaCierre, x.EspecialistaNombre, x.ValoresJson })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(docs, JsonOpts);
    }

    private async Task<string> ListAsignacionesRelacionadasAsync(Guid hcId, int ventanaDias, CancellationToken ct)
    {
        var pacId = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.PacienteId).FirstOrDefaultAsync(ct);
        if (pacId is null) { return "[]"; }
        var hcFecha = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => h.FechaCierre ?? h.FechaApertura).FirstOrDefaultAsync(ct);
        var desde = hcFecha.AddDays(-ventanaDias);
        var asigs = await _db.Asignaciones.AsNoTracking()
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
    private async Task<string> ListDocumentosHcAsync(Guid hcId, CancellationToken ct)
    {
        var docs = await _db.NotaMedicaDocumentos.AsNoTracking()
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
    private async Task<string?> GetFormDefinitionAsync(Guid hcId, CancellationToken ct)
    {
        var hc = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId)
            .Select(h => new { h.FormDefinitionId, h.ValoresJson })
            .FirstOrDefaultAsync(ct);
        if (hc is null) { return null; }
        var fd = await _db.FormDefinitions.AsNoTracking()
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

    private sealed record CampoCatalogo(string Campo, string? Name, string Tipo, bool Requerido, string Estado, string? Valor);
    private sealed record FechaAtencionCampo(string Campo, string? Name, string? Valor);
    private sealed record CatalogoResult(List<CampoCatalogo> Campos, List<FechaAtencionCampo> HeaderFechas, List<FechaAtencionCampo> CuerpoFechas);

    /// <summary>Cataloga los campos de CAPTURA del formato (header + cuerpo, recursando secciones;
    /// excluye bloques de texto estatico) cruzados con <paramref name="valoresJson"/> (lleno/vacio +
    /// valor), y aparte identifica las FECHAS DE ATENCION: las del header (campo de cabecera cuyo
    /// label contiene "fecha", mismo criterio que FechaAtencionHelper) y las del cuerpo (campos con
    /// IsFechaAtencion=true), para que el agente las contraste. Firmas base64 colapsadas, valores
    /// largos truncados.</summary>
    private static CatalogoResult BuildCatalogo(string? schemaJson, string? valoresJson)
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
            return s.Length > 300 ? s[..300] + "..." : s;
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
                if (string.IsNullOrWhiteSpace(n.Name)) { continue; }
                var val = ValorDe(n.Name!);
                var label = string.IsNullOrWhiteSpace(n.Label) ? n.Name! : n.Label!;
                campos.Add(new CampoCatalogo(
                    label, n.Name,
                    string.IsNullOrWhiteSpace(n.FieldType) ? "text" : n.FieldType!,
                    n.Required, val is null ? "vacio" : "lleno", val));
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
}

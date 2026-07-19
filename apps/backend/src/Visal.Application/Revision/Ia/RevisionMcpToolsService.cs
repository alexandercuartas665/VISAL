using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

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
        var payload = new
        {
            medicamentos, servicios, incapacidades, certificaciones, remisiones, insumos,
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
                a.CreatedAt,
                a.FormatoHistoria,
                a.PaqueteCodigo,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(asigs, JsonOpts);
    }

    private async Task<string?> GetFormDefinitionAsync(Guid hcId, CancellationToken ct)
    {
        var fdId = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Id == hcId).Select(h => (Guid?)h.FormDefinitionId).FirstOrDefaultAsync(ct);
        if (fdId is null) { return null; }
        var fd = await _db.FormDefinitions.AsNoTracking()
            .Where(f => f.Id == fdId)
            .Select(f => new
            {
                f.Id,
                f.Codigo,
                f.Nombre,
                f.Tipo,
                f.Version,
                f.SchemaJson,
            })
            .FirstOrDefaultAsync(ct);
        return fd is null ? null : JsonSerializer.Serialize(fd, JsonOpts);
    }
}

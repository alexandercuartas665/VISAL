using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Consulta read-only del modulo "Ordenes Clinicas". No expone metodos de
/// edicion porque el modulo es de consulta + reimpresion.
/// </summary>
public sealed class OrdenesClinicasService(IApplicationDbContext db) : IOrdenesClinicasService
{
    public async Task<IReadOnlyList<OrdenClinicaItemDto>> BuscarAsync(
        OrdenesClinicasFiltro filtro, CancellationToken ct = default)
    {
        var q = db.HistoriasClinicas.AsNoTracking().AsQueryable();

        if (filtro.SoloCerradas)
        {
            q = q.Where(h => h.Estado == HistoriaClinicaEstado.Cerrada);
        }

        if (filtro.Desde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(h => (h.FechaCierre ?? h.FechaApertura) >= dStart);
        }
        if (filtro.Hasta is DateOnly h2)
        {
            var dEnd = new DateTimeOffset(h2.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(h => (h.FechaCierre ?? h.FechaApertura) <= dEnd);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Especialista))
        {
            var esp = filtro.Especialista.Trim().ToLower();
            q = q.Where(h => h.EspecialistaNombre != null && h.EspecialistaNombre.ToLower().Contains(esp));
        }

        var joined = q
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id, (h, p) => new { h, p })
            .Join(db.FormDefinitions.AsNoTracking(), x => x.h.FormDefinitionId, f => f.Id, (x, f) => new { x.h, x.p, f });

        if (!string.IsNullOrWhiteSpace(filtro.PacienteTexto))
        {
            var t = filtro.PacienteTexto.Trim().ToLower();
            joined = joined.Where(x =>
                x.p.NombreCompleto.ToLower().Contains(t) ||
                x.p.NumeroDocumento.ToLower().Contains(t));
        }

        // Orden: paciente alfabetico ascendente, secundario por fecha de cierre desc
        // (las mas recientes arriba dentro del mismo paciente). El usuario pidio "orden
        // alfabetico por la fecha de cierre" — interpretamos: alfabetico por paciente,
        // y fecha de cierre como criterio secundario.
        var rows = await joined
            .OrderBy(x => x.p.NombreCompleto)
            .ThenByDescending(x => x.h.FechaCierre ?? x.h.FechaApertura)
            .Take(500)
            .Select(x => new
            {
                Hc = x.h,
                Pa = x.p,
                Fo = x.f
            })
            .ToListAsync(ct);

        if (rows.Count == 0) { return Array.Empty<OrdenClinicaItemDto>(); }

        // Conteos por HC en una sola pasada por tabla.
        var hcIds = rows.Select(r => r.Hc.Id).ToList();
        var medCounts = await db.HistoriaClinicaMedicamentos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var srvCounts = await db.HistoriaClinicaOrdenesServicio.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var remCounts = await db.HistoriaClinicaRemisiones.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var incCounts = await db.HistoriaClinicaIncapacidades.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var certCounts = await db.HistoriaClinicaCertificaciones.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);

        var med = medCounts.ToDictionary(x => x.Id, x => x.N);
        var srv = srvCounts.ToDictionary(x => x.Id, x => x.N);
        var rem = remCounts.ToDictionary(x => x.Id, x => x.N);
        var inc = incCounts.ToDictionary(x => x.Id, x => x.N);
        var cert = certCounts.ToDictionary(x => x.Id, x => x.N);

        return rows.Select(r => new OrdenClinicaItemDto(
            r.Hc.Id,
            r.Pa.Id,
            r.Pa.NombreCompleto,
            r.Pa.TipoDocumento,
            r.Pa.NumeroDocumento,
            r.Hc.Estado.ToString(),
            r.Hc.FechaApertura,
            r.Hc.FechaCierre,
            r.Fo.Nombre,
            r.Hc.EspecialistaNombre,
            med.GetValueOrDefault(r.Hc.Id, 0),
            srv.GetValueOrDefault(r.Hc.Id, 0),
            rem.GetValueOrDefault(r.Hc.Id, 0),
            inc.GetValueOrDefault(r.Hc.Id, 0),
            cert.GetValueOrDefault(r.Hc.Id, 0)
        )).ToList();
    }

    public async Task<IReadOnlyList<string>> ListarEspecialistasAsync(CancellationToken ct = default)
    {
        return await db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.EspecialistaNombre != null && h.EspecialistaNombre != "")
            .Select(h => h.EspecialistaNombre!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }
}

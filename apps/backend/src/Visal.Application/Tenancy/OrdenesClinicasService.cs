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

        // LEFT JOIN a `revisiones_clinica` para traer el estado agregado + veredicto
        // agente sin romper filas de HCs que aun no entraron al ciclo (Capa 08 Ola 2).
        // Adicionalmente, LEFT JOIN a `contratos_aseguradora` via Paciente.Contrato1Id
        // y a `aseguradoras` para resolver la EPS del contrato principal — es el
        // proxy mas cercano a "EPS bajo la cual se ejecuto la atencion" que ofrece
        // el modelo actual (HC no tiene link directo a Asignacion).
        var joined = q
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id, (h, p) => new { h, p })
            .Join(db.FormDefinitions.AsNoTracking(), x => x.h.FormDefinitionId, f => f.Id, (x, f) => new { x.h, x.p, f })
            .GroupJoin(db.ContratosAseguradora.AsNoTracking(),
                x => x.p.Contrato1Id, c => (Guid?)c.Id,
                (x, cs) => new { x.h, x.p, x.f, cs })
            .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new { x.h, x.p, x.f, c })
            .GroupJoin(db.Aseguradoras.AsNoTracking(),
                x => x.c == null ? (Guid?)null : (Guid?)x.c.AseguradoraId, a => (Guid?)a.Id,
                (x, ase) => new { x.h, x.p, x.f, x.c, ase })
            .SelectMany(x => x.ase.DefaultIfEmpty(), (x, a) => new { x.h, x.p, x.f, x.c, a })
            .GroupJoin(db.RevisionesClinica.AsNoTracking(),
                x => x.h.Id, r => r.HistoriaClinicaId,
                (x, rs) => new { x.h, x.p, x.f, x.c, x.a, rs })
            .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new { x.h, x.p, x.f, x.c, x.a, r });

        if (!string.IsNullOrWhiteSpace(filtro.PacienteTexto))
        {
            var t = filtro.PacienteTexto.Trim().ToLower();
            joined = joined.Where(x =>
                x.p.NombreCompleto.ToLower().Contains(t) ||
                x.p.NumeroDocumento.ToLower().Contains(t));
        }

        if (filtro.AseguradoraId is Guid aseFiltro)
        {
            joined = joined.Where(x => x.a != null && x.a.Id == aseFiltro);
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
                Fo = x.f,
                Rv = x.r,
                AseNombre = x.a == null ? null : x.a.Nombre,
                AseId = x.a == null ? (Guid?)null : (Guid?)x.a.Id
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
        var insCounts = await db.HistoriaClinicaInsumos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        // Ordenes externas: agrupamos por HC y por tipo para no traer las 3 en
        // 3 queries separadas. Filtro por hcIds en una sola pasada.
        var extCounts = await db.HistoriaClinicaOrdenesExternas.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => new { x.HistoriaClinicaId, x.Tipo })
            .Select(g => new { g.Key.HistoriaClinicaId, g.Key.Tipo, N = g.Count() })
            .ToListAsync(ct);
        var escCounts = await db.HistoriaClinicaEscalas.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => x.HistoriaClinicaId)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToListAsync(ct);
        var docCounts = await db.HistoriaClinicaDocumentos.AsNoTracking()
            .Where(x => hcIds.Contains(x.HistoriaClinicaId))
            .GroupBy(x => new { x.HistoriaClinicaId, x.Tipo })
            .Select(g => new { g.Key.HistoriaClinicaId, g.Key.Tipo, N = g.Count() })
            .ToListAsync(ct);

        var med = medCounts.ToDictionary(x => x.Id, x => x.N);
        var srv = srvCounts.ToDictionary(x => x.Id, x => x.N);
        var rem = remCounts.ToDictionary(x => x.Id, x => x.N);
        var inc = incCounts.ToDictionary(x => x.Id, x => x.N);
        var cert = certCounts.ToDictionary(x => x.Id, x => x.N);
        var ins = insCounts.ToDictionary(x => x.Id, x => x.N);
        var esc = escCounts.ToDictionary(x => x.Id, x => x.N);
        var rxImag = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.RxImagenologia)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var labExt = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Laboratorio)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var insExt = extCounts
            .Where(x => x.Tipo == Visal.Domain.Enums.TipoCatalogoServicio.Insumo)
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var evo = docCounts
            .Where(x => x.Tipo == "EVOLUCION")
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);
        var con = docCounts
            .Where(x => x.Tipo == "CONSENTIMIENTO")
            .ToDictionary(x => x.HistoriaClinicaId, x => x.N);

        // Capa 08 Ola 2 — resumen del ultimo veredicto del agente por revision.
        // Solo se trae el ultimo evento tipo `PreRevisionAgente` de cada revision:
        // sirve para popular el tooltip del chip "Pre-revision agente" en el grid.
        var revisionIds = rows.Where(r => r.Rv != null).Select(r => r.Rv!.Id).ToList();
        var agenteResumenes = new Dictionary<Guid, string?>();
        if (revisionIds.Count > 0)
        {
            var eventosAgente = await db.RevisionClinicaEventos.AsNoTracking()
                .Where(e => revisionIds.Contains(e.RevisionClinicaId)
                            && e.Tipo == RevisionTipoEvento.PreRevisionAgente)
                .GroupBy(e => e.RevisionClinicaId)
                .Select(g => new
                {
                    RevisionClinicaId = g.Key,
                    Ultimo = g.OrderByDescending(x => x.OcurridoEn).First()
                })
                .ToListAsync(ct);
            agenteResumenes = eventosAgente.ToDictionary(
                x => x.RevisionClinicaId,
                x => x.Ultimo.Nota ?? x.Ultimo.Motivo);
        }

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
            cert.GetValueOrDefault(r.Hc.Id, 0),
            ins.GetValueOrDefault(r.Hc.Id, 0),
            rxImag.GetValueOrDefault(r.Hc.Id, 0),
            labExt.GetValueOrDefault(r.Hc.Id, 0),
            insExt.GetValueOrDefault(r.Hc.Id, 0),
            esc.GetValueOrDefault(r.Hc.Id, 0),
            evo.GetValueOrDefault(r.Hc.Id, 0),
            con.GetValueOrDefault(r.Hc.Id, 0),
            r.Rv?.Id,
            r.Rv?.EstadoAgregado,
            r.Rv?.EstadoAgente,
            r.Rv?.IteracionActual,
            r.Rv is null ? null : agenteResumenes.GetValueOrDefault(r.Rv.Id),
            r.AseNombre,
            r.AseId
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

    public async Task<IReadOnlyList<AseguradoraOpcionDto>> ListarAseguradorasAsync(CancellationToken ct = default)
    {
        // Solo aseguradoras que realmente aparecen en el listado — HCs -> Paciente
        // -> Contrato1 -> Aseguradora. Evita ensuciar el filtro con EPSes que el
        // tenant configuro pero que nadie usa clinicamente.
        return await db.HistoriasClinicas.AsNoTracking()
            .Join(db.Pacientes.AsNoTracking(), h => h.PacienteId, p => p.Id, (h, p) => p)
            .Where(p => p.Contrato1Id != null)
            .Join(db.ContratosAseguradora.AsNoTracking(),
                p => p.Contrato1Id!.Value, c => c.Id, (p, c) => c.AseguradoraId)
            .Distinct()
            .Join(db.Aseguradoras.AsNoTracking(), id => id, a => a.Id,
                (id, a) => new AseguradoraOpcionDto(a.Id, a.Nombre))
            .OrderBy(x => x.Nombre)
            .ToListAsync(ct);
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Compone el tablero Kanban + tab Archivo del modulo `/ordenes` (Ola 2). Las
/// transiciones reales se delegan en <see cref="IRevisionClinicaService"/>; este
/// servicio se encarga de:
///   - Consultar HC + revision + paciente + formulario en una sola pasada.
///   - Clasificar cada HC en la columna correcta segun <see cref="RevisionEstadoAgregado"/>.
///   - Traducir la columna destino del drag&amp;drop a la accion apropiada.
///   - Enriquecer las cards con el ultimo motivo/veredicto para tooltips.
/// </summary>
public sealed class RevisionKanbanService : IRevisionKanbanService
{
    private readonly IApplicationDbContext _db;
    private readonly IRevisionClinicaService _revisiones;
    private readonly TimeProvider _clock;

    public RevisionKanbanService(
        IApplicationDbContext db,
        IRevisionClinicaService revisiones,
        TimeProvider clock)
    {
        _db = db;
        _revisiones = revisiones;
        _clock = clock;
    }

    public async Task<RevisionKanbanBoardDto> GetBoardAsync(CancellationToken ct = default)
    {
        // Trae todas las HCs no-inactivas + su revision (LEFT JOIN). Excluye HCs
        // clinicamente Inactivas y revisiones terminales (ArchivadaOk / Inactivada).
        // El Kanban muestra: HCs abiertas (sin revision), HCs cerradas con revision
        // en estado no-terminal, y HCs cerradas sin revision (aparecen en Cerradas
        // como candidatas a "Enviar a revision"). Tope 500 igual que el grid Lista.
        var raw = await _db.HistoriasClinicas.AsNoTracking()
            .Where(h => h.Estado != HistoriaClinicaEstado.Inactiva)
            .GroupJoin(_db.RevisionesClinica.AsNoTracking(),
                h => h.Id, r => r.HistoriaClinicaId,
                (h, rs) => new { h, rs })
            .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new { x.h, r })
            .Where(x => x.r == null
                     || (x.r.EstadoAgregado != RevisionEstadoAgregado.ArchivadaOk
                         && x.r.EstadoAgregado != RevisionEstadoAgregado.Inactivada))
            .Join(_db.Pacientes.AsNoTracking(), x => x.h.PacienteId, p => p.Id,
                (x, p) => new { x.h, x.r, p })
            .Join(_db.FormDefinitions.AsNoTracking(), x => x.h.FormDefinitionId, f => f.Id,
                (x, f) => new { x.h, x.r, x.p, f })
            .OrderBy(x => x.p.NombreCompleto)
            .ThenByDescending(x => x.h.FechaCierre ?? x.h.FechaApertura)
            .Take(500)
            .Select(x => new
            {
                Hc = x.h,
                Rv = x.r,
                Pa = x.p,
                Fo = x.f
            })
            .ToListAsync(ct);

        if (raw.Count == 0)
        {
            var vacio = new RevisionKanbanKpisDto(0, 0, 0, 0, 0, null, null);
            return new RevisionKanbanBoardDto(Array.Empty<RevisionKanbanCardDto>(), vacio);
        }

        // Ultimo motivo de rechazo por revision (tooltip de cards Rechazadas).
        // Ultimo resumen del agente por revision (tooltip pre-revision agente).
        var revisionIds = raw.Where(x => x.Rv != null).Select(x => x.Rv!.Id).ToList();
        var motivosRechazo = new Dictionary<Guid, string?>();
        var resumenAgente = new Dictionary<Guid, string?>();
        if (revisionIds.Count > 0)
        {
            var rechazos = await _db.RevisionClinicaEventos.AsNoTracking()
                .Where(e => revisionIds.Contains(e.RevisionClinicaId)
                            && e.Tipo == RevisionTipoEvento.Rechazado)
                .GroupBy(e => e.RevisionClinicaId)
                .Select(g => new { Rid = g.Key, Ultimo = g.OrderByDescending(x => x.OcurridoEn).First() })
                .ToListAsync(ct);
            motivosRechazo = rechazos.ToDictionary(x => x.Rid, x => x.Ultimo.Motivo);

            var agentes = await _db.RevisionClinicaEventos.AsNoTracking()
                .Where(e => revisionIds.Contains(e.RevisionClinicaId)
                            && e.Tipo == RevisionTipoEvento.PreRevisionAgente)
                .GroupBy(e => e.RevisionClinicaId)
                .Select(g => new { Rid = g.Key, Ultimo = g.OrderByDescending(x => x.OcurridoEn).First() })
                .ToListAsync(ct);
            resumenAgente = agentes.ToDictionary(x => x.Rid, x => x.Ultimo.Nota ?? x.Ultimo.Motivo);
        }

        var cards = raw
            .Select(x => new RevisionKanbanCardDto(
                x.Hc.Id,
                x.Rv?.Id,
                x.Pa.Id,
                x.Pa.NombreCompleto,
                x.Pa.TipoDocumento,
                x.Pa.NumeroDocumento,
                x.Fo.Nombre,
                x.Hc.EspecialistaNombre,
                x.Hc.FechaCierre,
                x.Hc.FechaApertura,
                MapearColumna(x.Hc.Estado, x.Rv?.EstadoAgregado),
                x.Rv?.EstadoAgregado,
                x.Rv?.EstadoAgente,
                x.Rv?.IteracionActual ?? 0,
                x.Rv is null ? null : motivosRechazo.GetValueOrDefault(x.Rv.Id),
                x.Rv is null ? null : resumenAgente.GetValueOrDefault(x.Rv.Id),
                x.Rv?.UltimaAccionEn ?? x.Hc.FechaCierre ?? x.Hc.FechaApertura))
            .ToList();

        var kpis = CalcularKpis(cards);
        return new RevisionKanbanBoardDto(cards, kpis);
    }

    public async Task MoverCardAsync(MoverCardCmd cmd, bool tienePermisoRevisar, CancellationToken ct = default)
    {
        if (!tienePermisoRevisar)
        {
            throw new UnauthorizedAccessException(
                "Se requiere permiso historias.revisar para mover cards.");
        }

        var revision = await _db.RevisionesClinica.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == cmd.RevisionClinicaId, ct)
            ?? throw new InvalidOperationException(
                $"Revision {cmd.RevisionClinicaId} no existe o pertenece a otro tenant.");

        // Mapeo columna destino -> accion. La columna origen sale del estado agregado
        // actual; el servicio de revisiones ya valida transicion invalida.
        switch (cmd.ColumnaDestino)
        {
            case RevisionKanbanColumna.Cerradas:
                // "Devolver a la cola" == asignar revisor si venia de Rechazadas. Si
                // ya estaba en Cerradas, no hay accion. La UI no permite drop a la
                // misma columna origen, pero validamos por si acaso.
                if (revision.EstadoAgregado == RevisionEstadoAgregado.Rechazada)
                {
                    // Reenvio conceptual: el profesional autor confirma que corrigio.
                    // Aca lo permitimos como accion del revisor para reactivar el ciclo.
                    await _revisiones.ReenviarAsync(
                        new ReenviarCmd(revision.Id, cmd.RevisorUsuarioId, cmd.Nota), ct);
                }
                else if (revision.EstadoAgregado == RevisionEstadoAgregado.Aprobada)
                {
                    // Deshacer una aprobacion — vuelve a EnRevision via Rectificacion (Ola 6 backlog).
                    throw new InvalidOperationException(
                        "Deshacer una aprobacion requiere el flujo Rectificacion (Ola 6). Aun no disponible.");
                }
                // Cualquier otro origen (SinRevisar, PreRevision, EnRevision): no aplica mover.
                break;

            case RevisionKanbanColumna.Aprobadas:
                // Si viene de estado no-EnRevision, hay que asignar primero.
                if (revision.EstadoAgregado != RevisionEstadoAgregado.EnRevision)
                {
                    await _revisiones.AsignarRevisorAsync(
                        new AsignarRevisorCmd(revision.Id, cmd.RevisorUsuarioId, false, null), ct);
                }
                await _revisiones.AprobarAsync(
                    new AprobarCmd(revision.Id, cmd.RevisorUsuarioId, cmd.Nota), ct);
                break;

            case RevisionKanbanColumna.Rechazadas:
                if (string.IsNullOrWhiteSpace(cmd.Motivo))
                {
                    throw new ArgumentException("El motivo es obligatorio para rechazar.", nameof(cmd.Motivo));
                }
                if (revision.EstadoAgregado != RevisionEstadoAgregado.EnRevision)
                {
                    await _revisiones.AsignarRevisorAsync(
                        new AsignarRevisorCmd(revision.Id, cmd.RevisorUsuarioId, false, null), ct);
                }
                await _revisiones.RechazarAsync(
                    new RechazarCmd(revision.Id, cmd.RevisorUsuarioId, cmd.Motivo!, cmd.Nota), ct);
                break;

            case RevisionKanbanColumna.Abiertas:
                // La columna Abiertas es solo lectura desde el punto de vista del revisor.
                throw new InvalidOperationException(
                    "No se puede mover una revision a Abiertas — esa columna refleja HCs abiertas por el profesional.");

            default:
                throw new ArgumentOutOfRangeException(nameof(cmd.ColumnaDestino));
        }
    }

    public async Task ArchivarAsync(ArchivarKanbanCmd cmd, bool tienePermisoFinal, CancellationToken ct = default)
    {
        // El gate de permiso lo aplica el servicio de revisiones — replicamos el
        // check aca para dar un mensaje temprano y no cargar la revision en vano.
        if (!tienePermisoFinal)
        {
            throw new UnauthorizedAccessException(
                "Se requiere permiso historias.revisar.aprobar_final para archivar.");
        }

        if (cmd.Sabor == ArchivarSabor.Ok)
        {
            await _revisiones.ArchivarOkAsync(
                new ArchivarOkCmd(cmd.RevisionClinicaId, cmd.RevisorUsuarioId, cmd.Motivo),
                tienePermisoFinal: true, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(cmd.Motivo))
            {
                throw new ArgumentException("El motivo es obligatorio para inactivar.", nameof(cmd.Motivo));
            }
            await _revisiones.InactivarAsync(
                new InactivarCmd(cmd.RevisionClinicaId, cmd.RevisorUsuarioId, cmd.Motivo!),
                tienePermisoFinal: true, ct);
        }
    }

    public async Task<IReadOnlyList<RevisionArchivoItemDto>> GetArchivoAsync(
        RevisionArchivoFiltro filtro, CancellationToken ct = default)
    {
        var q = _db.RevisionesClinica.AsNoTracking()
            .Where(r => r.EstadoAgregado == RevisionEstadoAgregado.ArchivadaOk
                     || r.EstadoAgregado == RevisionEstadoAgregado.Inactivada);

        if (filtro.Sabor is RevisionEstadoAgregado sabor)
        {
            q = q.Where(r => r.EstadoAgregado == sabor);
        }
        if (filtro.FechaDesde is DateOnly d)
        {
            var dStart = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            q = q.Where(r => r.UltimaAccionEn >= dStart);
        }
        if (filtro.FechaHasta is DateOnly h)
        {
            var dEnd = new DateTimeOffset(h.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            q = q.Where(r => r.UltimaAccionEn <= dEnd);
        }

        var joined = q
            .Join(_db.HistoriasClinicas.AsNoTracking(), r => r.HistoriaClinicaId, h => h.Id,
                (r, h) => new { r, h })
            .Join(_db.Pacientes.AsNoTracking(), x => x.h.PacienteId, p => p.Id,
                (x, p) => new { x.r, x.h, p })
            .Join(_db.FormDefinitions.AsNoTracking(), x => x.h.FormDefinitionId, f => f.Id,
                (x, f) => new { x.r, x.h, x.p, f });

        if (!string.IsNullOrWhiteSpace(filtro.PacienteTexto))
        {
            var t = filtro.PacienteTexto.Trim().ToLower();
            joined = joined.Where(x =>
                x.p.NombreCompleto.ToLower().Contains(t) ||
                x.p.NumeroDocumento.ToLower().Contains(t));
        }

        var rows = await joined
            .OrderByDescending(x => x.r.UltimaAccionEn)
            .Take(500)
            .Select(x => new
            {
                x.r,
                x.h,
                x.p,
                x.f,
            })
            .ToListAsync(ct);

        if (rows.Count == 0) { return Array.Empty<RevisionArchivoItemDto>(); }

        // Ultimo evento terminal por revision — trae actor + motivo para la card.
        var revisionIds = rows.Select(x => x.r.Id).ToList();
        var terminales = await _db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => revisionIds.Contains(e.RevisionClinicaId)
                        && (e.Tipo == RevisionTipoEvento.ArchivadoOk
                            || e.Tipo == RevisionTipoEvento.Inactivacion))
            .GroupBy(e => e.RevisionClinicaId)
            .Select(g => new
            {
                Rid = g.Key,
                Ultimo = g.OrderByDescending(x => x.OcurridoEn).First()
            })
            .ToListAsync(ct);
        var terminalDic = terminales.ToDictionary(x => x.Rid, x => x.Ultimo);

        var filtered = rows;
        if (filtro.RevisorUsuarioId is Guid rev)
        {
            filtered = rows
                .Where(x => terminalDic.TryGetValue(x.r.Id, out var t) && t.ActorUsuarioId == rev)
                .ToList();
        }

        return filtered.Select(x =>
        {
            terminalDic.TryGetValue(x.r.Id, out var eventoTerminal);
            return new RevisionArchivoItemDto(
                x.h.Id,
                x.r.Id,
                x.p.Id,
                x.p.NombreCompleto,
                x.p.TipoDocumento,
                x.p.NumeroDocumento,
                x.f.Nombre,
                x.h.EspecialistaNombre,
                x.r.EstadoAgregado,
                x.r.UltimaAccionEn,
                eventoTerminal?.ActorUsuarioId,
                eventoTerminal?.Motivo ?? eventoTerminal?.Nota,
                x.r.IteracionActual);
        }).ToList();
    }

    public Task<RevisionClinicaDto> SolicitarSiFaltaAsync(
        Guid historiaClinicaId, Guid actorUsuarioId, CancellationToken ct = default)
    {
        // El servicio de revisiones ya es idempotente: si existe una revision viva
        // para la HC, la devuelve sin duplicar evento SolicitudCreada.
        return _revisiones.SolicitarAsync(
            new SolicitarRevisionCmd(historiaClinicaId, actorUsuarioId, null), ct);
    }

    // ---- Internos ----

    public static RevisionKanbanColumna MapearColumna(
        HistoriaClinicaEstado hcEstado, RevisionEstadoAgregado? revEstado)
    {
        // HC abierta -> columna Abiertas, sin importar si hay revision (no deberia haberla).
        if (hcEstado == HistoriaClinicaEstado.Abierta)
        {
            return RevisionKanbanColumna.Abiertas;
        }

        // HC cerrada sin revision -> Cerradas (candidata a "Enviar a revision").
        return revEstado switch
        {
            null => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.SinRevisar => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.PreRevision => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.EnRevision => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.Rechazada => RevisionKanbanColumna.Rechazadas,
            RevisionEstadoAgregado.Aprobada => RevisionKanbanColumna.Aprobadas,
            // Terminales no deberian aparecer aqui (excluidas en el query).
            _ => RevisionKanbanColumna.Cerradas,
        };
    }

    private static RevisionKanbanKpisDto CalcularKpis(List<RevisionKanbanCardDto> cards)
    {
        var abiertas = cards.Count(c => c.Columna == RevisionKanbanColumna.Abiertas);
        var cerradas = cards.Count(c => c.Columna == RevisionKanbanColumna.Cerradas);
        var rechazadas = cards.Count(c => c.Columna == RevisionKanbanColumna.Rechazadas);
        var aprobadas = cards.Count(c => c.Columna == RevisionKanbanColumna.Aprobadas);
        var total = cards.Count;

        // Tiempo medio en columna Cerradas — desde el cierre de la HC (o solicitud
        // si es posterior) hasta ahora. Aproximacion util para saber si hay HCs
        // estancadas. Se calcula solo con las cards que estan en Cerradas.
        TimeSpan? tmedio = null;
        var cerradasCards = cards.Where(c => c.Columna == RevisionKanbanColumna.Cerradas).ToList();
        if (cerradasCards.Count > 0)
        {
            var refNow = cerradasCards.Max(c => c.UltimaAccionEn);
            var msProm = cerradasCards
                .Average(c => (refNow - (c.FechaCierre ?? c.FechaApertura)).TotalMilliseconds);
            tmedio = TimeSpan.FromMilliseconds(Math.Max(0, msProm));
        }

        // % rechazadas sobre revisiones no-abiertas (excluye la columna Abiertas
        // porque aun no entraron al ciclo). Null si no hay ninguna revision viva.
        double? pctRech = null;
        var totalConCiclo = cerradas + rechazadas + aprobadas;
        if (totalConCiclo > 0)
        {
            pctRech = (double)rechazadas / totalConCiclo * 100.0;
        }

        return new RevisionKanbanKpisDto(total, cerradas, aprobadas, rechazadas, abiertas, tmedio, pctRech);
    }
}

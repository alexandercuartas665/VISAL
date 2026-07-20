using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Calcula los KPIs del tab Dashboard de <c>/ordenes</c> (Capa 08 Ola 7 RC7a).
///
/// Todas las queries respetan el global tenant filter. Los eventos de
/// <see cref="RevisionClinicaEvento"/> son la fuente de verdad para tasas
/// de rechazo y adopcion automatica; para tiempos por columna Kanban se
/// derivan de <see cref="RevisionClinica.SolicitadaEn"/> y
/// <see cref="RevisionClinica.UltimaAccionEn"/>.
///
/// Ola 8 (RC8b): cache in-memory por tenant con TTL 60s. El dashboard se
/// abre desde el tab Kanban y no cambia por segundo — un minuto de latencia
/// es aceptable para KPIs y quita 4 queries pesadas por render.
/// </summary>
public sealed class RevisionDashboardService : IRevisionDashboardService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;
    private readonly IMemoryCache _cache;

    private const string CacheKeyPrefix = "revision-dashboard:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public RevisionDashboardService(IApplicationDbContext db, ITenantContext tenant, TimeProvider clock, IMemoryCache cache)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
        _cache = cache;
    }

    public async Task<RevisionDashboardDto> GetAsync(CancellationToken ct = default)
    {
        // Sin tenant activo no cacheamos (edge case job/background sin scope).
        if (_tenant.TenantId is not Guid tid)
        {
            return await BuildAsync(ct);
        }

        var key = CacheKeyPrefix + tid;
        if (_cache.TryGetValue(key, out RevisionDashboardDto? cached) && cached is not null)
        {
            return cached;
        }

        var dto = await BuildAsync(ct);
        _cache.Set(key, dto, CacheTtl);
        return dto;
    }

    private async Task<RevisionDashboardDto> BuildAsync(CancellationToken ct)
    {
        var resumen = await CalcularResumenAsync(ct);
        var topRechazos = await CalcularTopRechazosPorProfesionalAsync(ct);
        var adopcion = await CalcularAdopcionAgenteAsync(ct);
        var tiempos = await CalcularTiemposPorColumnaAsync(ct);
        return new RevisionDashboardDto(resumen, topRechazos, adopcion, tiempos);
    }

    private async Task<RevisionDashboardResumenDto> CalcularResumenAsync(CancellationToken ct)
    {
        var porEstado = await _db.RevisionesClinica.AsNoTracking()
            .GroupBy(r => r.EstadoAgregado)
            .Select(g => new { Estado = g.Key, N = g.Count() })
            .ToListAsync(ct);

        int Get(RevisionEstadoAgregado e) => porEstado.FirstOrDefault(x => x.Estado == e)?.N ?? 0;

        var aprobadas = Get(RevisionEstadoAgregado.Aprobada);
        var rechazadas = Get(RevisionEstadoAgregado.Rechazada);
        var archOk = Get(RevisionEstadoAgregado.ArchivadaOk);
        var inact = Get(RevisionEstadoAgregado.Inactivada);
        var sinRev = Get(RevisionEstadoAgregado.SinRevisar);
        var preRev = Get(RevisionEstadoAgregado.PreRevision);
        var enRev = Get(RevisionEstadoAgregado.EnRevision);

        var terminales = archOk + inact;
        var activos = sinRev + preRev + enRev + aprobadas + rechazadas;
        var total = terminales + activos;

        // Tasa de rechazo global = eventos Rechazado / eventos con veredicto humano
        // (Aprobado + Rechazado). Da una tasa mas honesta que dividir por total de
        // ciclos porque los ciclos abiertos aun no fueron rechazados.
        var pct = 0d;
        if (aprobadas + rechazadas + archOk > 0)
        {
            pct = 100d * rechazadas / (aprobadas + rechazadas + archOk);
        }

        return new RevisionDashboardResumenDto(
            TotalCiclos: total,
            CiclosActivos: activos,
            CiclosTerminales: terminales,
            Aprobadas: aprobadas + archOk,
            Rechazadas: rechazadas,
            ArchivadasOk: archOk,
            Inactivadas: inact,
            PorcentajeRechazoGlobal: Math.Round(pct, 1));
    }

    private async Task<IReadOnlyList<RevisionRechazoPorProfesionalDto>> CalcularTopRechazosPorProfesionalAsync(CancellationToken ct)
    {
        // JOIN eventos con HC (via revision) para saber quien es el profesional
        // (EspecialistaNombre denormalizado). Agrupa por especialista, cuenta
        // rechazos absolutos y calcula porcentaje sobre sus veredictos humanos.
        var rows = await _db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => e.ActorTipo == RevisionActorTipo.Humano
                     && (e.Resultado == RevisionResultado.Rechazado
                         || e.Resultado == RevisionResultado.Aprobado))
            .Join(_db.RevisionesClinica.AsNoTracking(),
                e => e.RevisionClinicaId, r => r.Id,
                (e, r) => new { e.Resultado, r.HistoriaClinicaId })
            .Join(_db.HistoriasClinicas.AsNoTracking(),
                x => x.HistoriaClinicaId, h => h.Id,
                (x, h) => new { x.Resultado, h.EspecialistaNombre })
            .Where(x => x.EspecialistaNombre != null && x.EspecialistaNombre != "")
            .GroupBy(x => x.EspecialistaNombre!)
            .Select(g => new
            {
                Especialista = g.Key,
                Total = g.Count(),
                Rechazados = g.Count(x => x.Resultado == RevisionResultado.Rechazado),
            })
            .Where(g => g.Rechazados > 0)
            .OrderByDescending(g => g.Rechazados)
            .Take(10)
            .ToListAsync(ct);

        return rows.Select(r => new RevisionRechazoPorProfesionalDto(
            EspecialistaNombre: r.Especialista,
            TotalCiclos: r.Total,
            Rechazados: r.Rechazados,
            PorcentajeRechazo: r.Total == 0 ? 0d : Math.Round(100d * r.Rechazados / r.Total, 1)))
        .ToList();
    }

    private async Task<RevisionAdopcionAgenteDto> CalcularAdopcionAgenteAsync(CancellationToken ct)
    {
        // Aprobaciones totales (humano + sistema) vs aprobaciones automaticas
        // (ActorTipo = Sistema, marcado en Ola 6 RC6c por AprobarPorSistemaAsync).
        var contadores = await _db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => e.Tipo == RevisionTipoEvento.Aprobado)
            .GroupBy(e => e.ActorTipo)
            .Select(g => new { Actor = g.Key, N = g.Count() })
            .ToListAsync(ct);

        var automaticas = contadores.FirstOrDefault(x => x.Actor == RevisionActorTipo.Sistema)?.N ?? 0;
        var totales = contadores.Sum(x => x.N);
        var pct = totales == 0 ? 0d : Math.Round(100d * automaticas / totales, 1);

        return new RevisionAdopcionAgenteDto(
            TotalAprobaciones: totales,
            AprobacionesAutomaticas: automaticas,
            PorcentajeAdopcion: pct);
    }

    private async Task<IReadOnlyList<RevisionTiempoEnColumnaDto>> CalcularTiemposPorColumnaAsync(CancellationToken ct)
    {
        // Simplificacion pragmatica: para cada columna Kanban activa se calcula
        // el tiempo que las HCs llevan en su estado actual (UltimaAccionEn a ahora).
        // No es tan preciso como reconstruir el timeline de eventos, pero es una
        // buena aproximacion del "tiempo hasta la proxima accion humana".
        var now = _clock.GetUtcNow();
        var rows = await _db.RevisionesClinica.AsNoTracking()
            .Where(r => r.EstadoAgregado != RevisionEstadoAgregado.ArchivadaOk
                     && r.EstadoAgregado != RevisionEstadoAgregado.Inactivada)
            .Select(r => new { r.EstadoAgregado, r.UltimaAccionEn })
            .ToListAsync(ct);

        RevisionKanbanColumna? Map(RevisionEstadoAgregado e) => e switch
        {
            RevisionEstadoAgregado.SinRevisar => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.PreRevision => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.EnRevision => RevisionKanbanColumna.Cerradas,
            RevisionEstadoAgregado.Rechazada => RevisionKanbanColumna.Rechazadas,
            RevisionEstadoAgregado.Aprobada => RevisionKanbanColumna.Aprobadas,
            _ => null,
        };

        var enriched = rows
            .Select(r => new { Col = Map(r.EstadoAgregado), Wait = now - r.UltimaAccionEn })
            .Where(x => x.Col.HasValue)
            .GroupBy(x => x.Col!.Value)
            .Select(g => new RevisionTiempoEnColumnaDto(
                Columna: g.Key,
                Muestras: g.Count(),
                PromedioPermanencia: TimeSpan.FromTicks((long)g.Average(x => x.Wait.Ticks))))
            .ToList();

        // Rellena las 3 columnas de trabajo aunque no haya muestras — la UI es
        // mas honesta si muestra "0 muestras" que si oculta la columna.
        var esperadas = new[] { RevisionKanbanColumna.Cerradas, RevisionKanbanColumna.Rechazadas, RevisionKanbanColumna.Aprobadas };
        var final = new List<RevisionTiempoEnColumnaDto>(esperadas.Length);
        foreach (var col in esperadas)
        {
            var found = enriched.FirstOrDefault(x => x.Columna == col);
            final.Add(found ?? new RevisionTiempoEnColumnaDto(col, 0, null));
        }
        return final;
    }
}

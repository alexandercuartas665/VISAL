using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;

namespace Visal.SuperAdmin.Revision;

/// <summary>
/// Lotes de reproceso del auditor IA (experimento de instrucciones, API /api/bitacora-ia/reprocesos).
/// Cada lote reenvia al agente el contexto EXACTO de corridas ya guardadas con las instrucciones
/// actuales (o unas indicadas) y acumula ambos conceptos para compararlos. En memoria: un reinicio
/// de la app pierde los lotes (es un experimento, no un dato del negocio). No escribe en la HC ni
/// en el ciclo de revision; el consumo queda en ai_usage_logs con source "revision-reproceso".
/// </summary>
public sealed class ReprocesoIaJobs
{
    public const int MaxCorridas = 300;
    public const int Paralelo = 3;
    private static readonly TimeSpan Retencion = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReprocesoIaJobs> _log;
    private readonly ConcurrentDictionary<Guid, Lote> _lotes = new();

    public ReprocesoIaJobs(IServiceScopeFactory scopes, ILogger<ReprocesoIaJobs> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public sealed class Lote
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
        public DateTimeOffset CreadoEn { get; init; }
        public DateTimeOffset? TerminadoEn { get; set; }
        public string Estado { get; set; } = "EnCurso";
        public int Total { get; init; }
        public bool ConMemoria { get; init; }
        public bool InstruccionesPropias { get; init; }
        public List<ReprocesoIaResultado> Resultados { get; } = new();
        internal CancellationTokenSource Cts { get; } = new();
    }

    public (Lote? Lote, string? Error) Iniciar(Guid tenantId, IReadOnlyList<Guid> corridas, string? instrucciones, bool conMemoria)
    {
        Purgar();
        var ids = corridas.Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) { return (null, "Indica al menos una corrida en 'corridas'."); }
        if (ids.Count > MaxCorridas) { return (null, $"Maximo {MaxCorridas} corridas por lote."); }
        if (_lotes.Values.Any(l => l.TenantId == tenantId && l.Estado == "EnCurso"))
        {
            return (null, "Ya hay un lote de reproceso en curso; espera a que termine o cancelalo.");
        }

        var lote = new Lote
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreadoEn = DateTimeOffset.UtcNow,
            Total = ids.Count,
            ConMemoria = conMemoria,
            InstruccionesPropias = !string.IsNullOrWhiteSpace(instrucciones),
        };
        _lotes[lote.Id] = lote;
        // Fuera del request (y sin heredar su contexto): el tenant se fija por TenantAmbient en cada item.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => EjecutarAsync(lote, ids, instrucciones, conMemoria));
        }
        return (lote, null);
    }

    public Lote? Obtener(Guid tenantId, Guid id)
        => _lotes.TryGetValue(id, out var l) && l.TenantId == tenantId ? l : null;

    public bool Cancelar(Guid tenantId, Guid id)
    {
        if (Obtener(tenantId, id) is not { } l || l.Estado != "EnCurso") { return false; }
        l.Cts.Cancel();
        return true;
    }

    private async Task EjecutarAsync(Lote lote, List<Guid> ids, string? instrucciones, bool conMemoria)
    {
        var opciones = new OpcionesReproceso(instrucciones, conMemoria);
        try
        {
            await Parallel.ForEachAsync(ids,
                new ParallelOptions { MaxDegreeOfParallelism = Paralelo, CancellationToken = lote.Cts.Token },
                async (id, ct) =>
                {
                    using var _ = TenantAmbient.Scope(lote.TenantId);
                    using var scope = _scopes.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IPreRevisionIaService>();
                    ReprocesoIaResultado r;
                    try { r = await svc.ReprocesarCorridaAsync(id, opciones, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Sin datos clinicos en el log: solo el tipo de error y la corrida.
                        _log.LogWarning("Reproceso IA fallo corrida={Corrida}: {Tipo}", id, ex.GetType().Name);
                        r = new ReprocesoIaResultado(id, Guid.Empty, false, "Error interno: " + ex.GetType().Name, null, null);
                    }
                    lock (lote.Resultados) { lote.Resultados.Add(r); }
                });
            lote.Estado = "Terminado";
        }
        catch (OperationCanceledException)
        {
            lote.Estado = "Cancelado";
        }
        catch (Exception ex)
        {
            _log.LogWarning("Lote de reproceso IA {Lote} fallo: {Tipo}", lote.Id, ex.GetType().Name);
            lote.Estado = "Error";
        }
        finally
        {
            lote.TerminadoEn = DateTimeOffset.UtcNow;
        }
    }

    private void Purgar()
    {
        var limite = DateTimeOffset.UtcNow - Retencion;
        foreach (var kv in _lotes.Where(kv => kv.Value.TerminadoEn is { } t && t < limite).ToList())
        {
            _lotes.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>Vista del lote para la API: avance, matriz original -> nuevo y el detalle por corrida.</summary>
    public static object Vista(Lote l, bool conRespuestaCruda)
    {
        List<ReprocesoIaResultado> rs;
        lock (l.Resultados) { rs = l.Resultados.ToList(); }
        var ok = rs.Where(r => r.Ok && r.Original is not null && r.Nuevo is not null).ToList();
        var matriz = ok
            .GroupBy(r => $"{r.Original!.Resultado ?? "?"} -> {r.Nuevo!.Resultado ?? "?"}")
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        double Prom(Func<ReprocesoIaResultado, int> f) => ok.Count == 0 ? 0 : Math.Round(ok.Average(f), 2);
        return new
        {
            id = l.Id,
            estado = l.Estado,
            creadoEn = l.CreadoEn,
            terminadoEn = l.TerminadoEn,
            total = l.Total,
            hechos = rs.Count,
            fallidas = rs.Count(r => !r.Ok),
            instrucciones = l.InstruccionesPropias ? "propias (enviadas en el lote)" : "vigentes del agente",
            conMemoria = l.ConMemoria,
            tokensIn = rs.Sum(r => (long)r.InputTokens),
            tokensOut = rs.Sum(r => (long)r.OutputTokens),
            resumen = new
            {
                comparadas = ok.Count,
                iguales = ok.Count(r => r.Original!.Resultado == r.Nuevo!.Resultado),
                aprobadasOriginal = ok.Count(r => r.Original!.Resultado == "Aprobado"),
                aprobadasNuevo = ok.Count(r => r.Nuevo!.Resultado == "Aprobado"),
                hallazgosPromOriginal = Prom(r => r.Original!.Hallazgos.Count),
                hallazgosPromNuevo = Prom(r => r.Nuevo!.Hallazgos.Count),
                observacionesPromNuevo = Prom(r => r.Nuevo!.Observaciones.Count),
                matriz,
            },
            items = rs.Select(r => new
            {
                corridaId = r.CorridaId,
                historiaClinicaId = r.HistoriaClinicaId,
                ok = r.Ok,
                error = r.Error,
                original = r.Original,
                nuevo = r.Nuevo,
                tokensIn = r.InputTokens,
                tokensOut = r.OutputTokens,
                modelo = r.Modelo,
                respuestaCruda = conRespuestaCruda ? r.RespuestaCruda : null,
            }),
        };
    }
}

/// <summary>Cuerpo de POST /api/bitacora-ia/reprocesos.</summary>
public sealed record ReprocesoIaBody(List<Guid>? Corridas, string? Instrucciones, bool ConMemoria = false);

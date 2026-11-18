using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Voz;

namespace Visal.Infrastructure.Voz;

/// <summary>
/// Worker que consume <see cref="SeguimientoIaQueue"/> y resuelve con IA la
/// encuesta SIAU de cada llamada terminada, en su propio scope de tenant. Con
/// esto la resolucion es 100% sin-manos: el webhook de Retell (anonimo) solo
/// encola, y la inferencia (tenant-scoped) corre aqui via <c>TenantAmbient.Scope</c>.
///
/// Al arrancar, re-escanea las encuestas que tienen llamada con transcripcion y
/// aun estan sin responder y las re-encola (recuperacion sin tabla de staging:
/// la fuente de verdad son los datos; el resolver es idempotente y llena solo lo
/// vacio, asi que re-encolar no daña nada).
/// </summary>
public sealed class SeguimientoIaWorker : BackgroundService
{
    private readonly SeguimientoIaQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeguimientoIaWorker> _log;

    public SeguimientoIaWorker(
        ISeguimientoIaQueue queue, IServiceScopeFactory scopeFactory, ILogger<SeguimientoIaWorker> log)
    {
        // El DI resuelve la interfaz; casteamos a la concreta porque ReadAllAsync
        // no es parte del contrato publico (solo el worker lee).
        _queue = (SeguimientoIaQueue)queue;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("SeguimientoIaWorker arrancado.");

        try { await ReencolarPendientesAsync(stoppingToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "SeguimientoIaWorker: fallo reencolando pendientes al startup (sigue arrancando).");
        }

        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                await ProcesarAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutdown limpio */ }
        _log.LogInformation("SeguimientoIaWorker detenido.");
    }

    // Busca (cross-tenant) encuestas con llamada-con-transcripcion y cuestionario vacio,
    // y las encola. Idempotente: las ya resueltas no vuelven a entrar.
    private async Task ReencolarPendientesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IApplicationDbContextFactory>();
        using var db = dbFactory.Create();

        var conTranscripcion = await db.LlamadasVoz.IgnoreQueryFilters()
            .Where(l => l.SeguimientoEncuestaId != null && l.Transcripcion != null && l.Transcripcion != "")
            .Select(l => l.SeguimientoEncuestaId!.Value)
            .Distinct()
            .ToListAsync(ct);
        if (conTranscripcion.Count == 0) { return; }

        var pendientes = await db.SeguimientoEncuestas.IgnoreQueryFilters()
            .Where(e => conTranscripcion.Contains(e.Id)
                && e.Estado != "NoContactado"
                && e.Pregunta1 == null && e.Pregunta2 == null && e.Pregunta3 == null
                && e.Pregunta4 == null && e.Pregunta5 == null)
            .Select(e => new { e.TenantId, e.Id })
            .ToListAsync(ct);
        if (pendientes.Count == 0) { return; }

        _log.LogInformation("SeguimientoIaWorker: reencolando {N} encuesta(s) SIAU con transcripcion sin resolver.", pendientes.Count);
        foreach (var p in pendientes)
        {
            await _queue.EnqueueAsync(new SeguimientoIaJob(p.TenantId, p.Id), ct);
        }
    }

    private async Task ProcesarAsync(SeguimientoIaJob job, CancellationToken ct)
    {
        // Scope de tenant via AsyncLocal: todo lo resuelto en este scope ve el tenant correcto.
        using var _ = TenantAmbient.Scope(job.TenantId, null, null);
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ISeguimientoIaResolver>();
            var r = await resolver.ResolverDesdeUltimaLlamadaAsync(job.EncuestaId, ct);
            if (!r.Ok)
            {
                _log.LogWarning("SeguimientoIaWorker: no se resolvio encuesta {EncId} tenant {Tenant}: {Error}",
                    job.EncuestaId, job.TenantId, r.Error);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SeguimientoIaWorker: fallo resolviendo encuesta {EncId} tenant {Tenant} (ignorado).",
                job.EncuestaId, job.TenantId);
        }
    }
}

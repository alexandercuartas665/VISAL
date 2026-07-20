using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;

namespace Visal.Infrastructure.Revision;

/// <summary>
/// Ola 8 RC8e — worker que consume <see cref="PreRevisionIaQueue"/> y ejecuta
/// el orquestador REVISOR CLINICO IA en su propio scope tenant. Con esto el
/// cierre de HC deja de bloquearse esperando al proveedor de IA — el request
/// vuelve al usuario apenas la HC persistio, y la pre-revision se corre en
/// background.
///
/// Un solo consumer (SingleReader) por proceso: en horizontal scaling cada
/// nodo procesa lo suyo y la sobrecarga es minima; si la lista crece se puede
/// mover a un broker externo mas adelante.
///
/// Cada item se ejecuta dentro de un scope propio + <c>TenantAmbient.Scope</c>
/// para que las queries respeten el filtro tenant global sin exponer una API
/// mutable en <see cref="ITenantContext"/>.
/// </summary>
public sealed class PreRevisionIaWorker : BackgroundService
{
    private readonly PreRevisionIaQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreRevisionIaWorker> _log;

    public PreRevisionIaWorker(
        IPreRevisionIaQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<PreRevisionIaWorker> log)
    {
        // El DI resuelve la interfaz; casteamos a la clase concreta porque
        // ReadAllAsync no forma parte del contrato publico (deliberado: solo el
        // worker debe leer).
        _queue = (PreRevisionIaQueue)queue;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("PreRevisionIaWorker arrancado.");

        // Ola 9 RC9c — al startup, reencolamos lo que quedo en la staging table
        // (items enqueue-ados en la corrida anterior que murieron antes de que
        // el worker los procesara). Un scope temporal para leer el store.
        try
        {
            using var startupScope = _scopeFactory.CreateScope();
            var store = startupScope.ServiceProvider.GetRequiredService<IPreRevisionIaPendingStore>();
            var pending = await store.LoadAllAsync(stoppingToken);
            if (pending.Count > 0)
            {
                _log.LogInformation("RC9c reencolando {N} pre-revisiones IA pending del restart anterior.", pending.Count);
                foreach (var job in pending)
                {
                    await _queue.EnqueueAsync(job, stoppingToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "RC9c fallo cargando pending al startup — el worker sigue arrancando; los items en channel se procesan.");
        }

        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                await ProcessOneAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown limpio.
        }
        _log.LogInformation("PreRevisionIaWorker detenido.");
    }

    private async Task ProcessOneAsync(PreRevisionIaJob job, CancellationToken ct)
    {
        // Cada scope es corto — orquestador + queries + auditoria. El scope
        // tenant se ata al AsyncLocal para que TODAS las instancias resueltas
        // desde este scope vean el tenant correcto.
        using var _ = TenantAmbient.Scope(job.TenantId, job.ActorUserId, null);
        using var scope = _scopeFactory.CreateScope();
        try
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPreRevisionIaService>();
            await svc.EjecutarAsync(job.RevisionClinicaId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "RC8e worker PreRevision IA fallo tenant={TenantId} rev={RevId} (ignorado).",
                job.TenantId, job.RevisionClinicaId);
        }
        finally
        {
            // Ola 9 RC9c — borra la fila staging aunque el ejecutar haya fallado.
            // El orquestador es idempotente pero no queremos reintentar
            // infinitamente en cada restart un job que rompe deterministicamente.
            try
            {
                var store = scope.ServiceProvider.GetRequiredService<IPreRevisionIaPendingStore>();
                await store.DeleteAsync(job.PendingId, ct);
            }
            catch (Exception delEx)
            {
                _log.LogWarning(delEx,
                    "RC9c fallo borrando pending tenant={TenantId} rev={RevId} pending={PendingId} (item quedara y se reencolara al proximo restart).",
                    job.TenantId, job.RevisionClinicaId, job.PendingId);
            }
        }
    }
}

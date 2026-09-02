using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Tenancy.Alertas;
using Visal.Infrastructure.Persistence;

namespace Visal.Infrastructure.Alertas;

/// <summary>
/// Worker diario del motor de alertas. Revisa cada hora si cambio el dia (hora
/// Colombia, UTC-5); cuando cambia, evalua las reglas activas de TODOS los
/// tenants y dispara los envios que correspondan. La idempotencia la garantiza
/// el outbox (AlertaEnvio), asi que reintentar tras un reinicio no duplica.
/// Mismo patron multi-tenant que <c>EmailIngestWorker</c>.
/// </summary>
public sealed class AlertasWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertasWorker> _log;
    private static readonly TimeSpan LoopDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);
    private DateOnly? _ultimoDiaCorrido;

    public AlertasWorker(IServiceScopeFactory scopeFactory, ILogger<AlertasWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AlertasWorker arrancado.");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hoy = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ColombiaOffset).DateTime);
                if (_ultimoDiaCorrido != hoy)
                {
                    await CorrerAsync(hoy, stoppingToken);
                    _ultimoDiaCorrido = hoy;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "AlertasWorker: ciclo fallo (ignorado)."); }

            try { await Task.Delay(LoopDelay, stoppingToken); } catch { break; }
        }
        _log.LogInformation("AlertasWorker detenido.");
    }

    private async Task CorrerAsync(DateOnly hoy, CancellationToken ct)
    {
        // Tenants con al menos una regla activa (sin filtro tenant; solo el id).
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
            tenantIds = await db.AlertaReglas.IgnoreQueryFilters()
                .Where(r => r.Activa)
                .Select(r => r.TenantId)
                .Distinct()
                .ToListAsync(ct);
        }

        foreach (var tenantId in tenantIds)
        {
            ct.ThrowIfCancellationRequested();
            using var _ = TenantAmbient.Scope(tenantId, Guid.Empty, null);
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var svc = scope.ServiceProvider.GetRequiredService<IAlertaService>();
                var r = await svc.EvaluarYDispararAsync(hoy, forzar: false, actor: Guid.Empty, ct: ct);
                if (r.Enviadas > 0 || r.Errores > 0)
                {
                    _log.LogInformation("Alertas tenant {Tenant}: {Env} enviadas, {Err} errores.", tenantId, r.Enviadas, r.Errores);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AlertasWorker fallo tenant={TenantId} (ignorado).", tenantId);
            }
        }
    }
}

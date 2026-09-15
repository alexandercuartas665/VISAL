using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Infrastructure.Persistence;

namespace Visal.Infrastructure.Interoperabilidad;

/// <summary>
/// Worker de reintento de envios RDA al IHCE. Cada pocos minutos revisa los tenants con
/// reintento activo y reintenta sus eventos fallidos por causa transitoria (5xx/timeout,
/// o servicio de MinSalud indisponible como EVOL), respetando el intervalo y el maximo de
/// intentos configurados por tenant. Cada intento queda en la traza (rda_evento_intentos).
/// Mismo patron multi-tenant que <c>AlertasWorker</c>.
/// </summary>
public sealed class RdaReintentoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RdaReintentoWorker> _log;
    // Cadencia del chequeo. El intervalo real por evento lo controla la config del tenant
    // (ReintentoIntervaloMin); este loop solo despierta a mirar quien ya cumplio su espera.
    private static readonly TimeSpan LoopDelay = TimeSpan.FromMinutes(2);

    public RdaReintentoWorker(IServiceScopeFactory scopeFactory, ILogger<RdaReintentoWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("RdaReintentoWorker arrancado.");
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CorrerAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "RdaReintentoWorker: ciclo fallo (ignorado)."); }

            try { await Task.Delay(LoopDelay, stoppingToken); } catch { break; }
        }
        _log.LogInformation("RdaReintentoWorker detenido.");
    }

    private async Task CorrerAsync(CancellationToken ct)
    {
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
            tenantIds = await db.InteroperabilidadConfigs.IgnoreQueryFilters()
                .Where(c => c.ReintentoActivo)
                .Select(c => c.TenantId)
                .Distinct()
                .ToListAsync(ct);
        }
        if (tenantIds.Count == 0) { return; }

        foreach (var tenantId in tenantIds)
        {
            ct.ThrowIfCancellationRequested();
            using var _ = TenantAmbient.Scope(tenantId, Guid.Empty, null);
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var svc = scope.ServiceProvider.GetRequiredService<IRdaReintentoService>();
                var n = await svc.ReintentarPendientesAsync(ct);
                if (n > 0) { _log.LogInformation("RdaReintento tenant {Tenant}: {N} reintento(s).", tenantId, n); }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RdaReintentoWorker fallo tenant={TenantId} (ignorado).", tenantId);
            }
        }
    }
}

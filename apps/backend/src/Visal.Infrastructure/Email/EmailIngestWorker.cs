using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Tenancy.Email;
using Visal.Infrastructure.Persistence;

namespace Visal.Infrastructure.Email;

/// <summary>
/// Poller de ingesta de correos -> PQR. Cada minuto revisa los buzones habilitados de TODOS los
/// tenants cuya cadencia (PollIntervalMinutes) ya venció y procesa cada uno en su propio scope
/// tenant (igual patron que <c>PreRevisionIaWorker</c>). El procesamiento real (IMAP + IA + tarjeta)
/// lo hace <see cref="IEmailIngestProcessor"/>.
/// </summary>
public sealed class EmailIngestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailIngestWorker> _log;
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(60);

    public EmailIngestWorker(IServiceScopeFactory scopeFactory, ILogger<EmailIngestWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("EmailIngestWorker arrancado.");
        // Pequeña espera inicial para no competir con las migraciones del arranque.
        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "EmailIngestWorker: ciclo fallo (ignorado)."); }

            try { await Task.Delay(LoopDelay, stoppingToken); } catch { break; }
        }
        _log.LogInformation("EmailIngestWorker detenido.");
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        // Enumeramos configs habilitados de todos los tenants (sin filtro tenant): solo campos de
        // agenda, sin exponer datos del tenant. El procesamiento real re-establece el scope tenant.
        List<(Guid Id, Guid TenantId)> due;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
            var now = DateTimeOffset.UtcNow;
            var rows = await db.TenantEmailIngestConfigs.IgnoreQueryFilters()
                .Where(c => c.IsEnabled && c.AppPasswordEncrypted != null && c.ClassifierAgentId != null && c.TargetBoardId != null)
                .Select(c => new { c.Id, c.TenantId, c.PollIntervalMinutes, c.LastPolledAt })
                .ToListAsync(ct);
            due = rows
                .Where(r => r.LastPolledAt is null || r.LastPolledAt.Value.AddMinutes(Math.Max(1, r.PollIntervalMinutes)) <= now)
                .Select(r => (r.Id, r.TenantId))
                .ToList();
        }

        foreach (var (id, tenantId) in due)
        {
            ct.ThrowIfCancellationRequested();
            using var _ = TenantAmbient.Scope(tenantId, Guid.Empty, null);
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var proc = scope.ServiceProvider.GetRequiredService<IEmailIngestProcessor>();
                await proc.ProcessConfigAsync(id, progress: null, ct: ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "EmailIngest fallo tenant={TenantId} config={ConfigId} (ignorado).", tenantId, id);
            }
        }
    }
}

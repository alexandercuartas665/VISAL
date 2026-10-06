using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Visal.Application.Common;
using Visal.Application.Facturacion;

namespace Visal.SuperAdmin.Facturacion;

/// <inheritdoc />
public sealed class TipologiaZipJobService : ITipologiaZipJobService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TipologiaZipJobService> _log;
    private readonly ConcurrentDictionary<Guid, ZipJobInfo> _jobs = new();

    // Cuanto se retiene un job terminado antes de purgarlo (libera los bytes del ZIP).
    private static readonly TimeSpan Retencion = TimeSpan.FromMinutes(30);

    public TipologiaZipJobService(IServiceScopeFactory scopes, ILogger<TipologiaZipJobService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public Guid Iniciar(Guid snapshotId, Guid archivoItemId, Guid tenantId, Guid? userId, Guid? sucursalId)
    {
        PurgarViejos();
        var job = new ZipJobInfo { Id = Guid.NewGuid() };
        _jobs[job.Id] = job;
        // Fire-and-forget: corre fuera del request. No se ata al CancellationToken del
        // request (ese fue justo el bug: el navegador abortaba y mataba la generacion).
        _ = Task.Run(() => EjecutarAsync(job, snapshotId, archivoItemId, tenantId, userId, sucursalId));
        return job.Id;
    }

    private async Task EjecutarAsync(
        ZipJobInfo job, Guid snapshotId, Guid archivoItemId, Guid tenantId, Guid? userId, Guid? sucursalId)
    {
        try
        {
            // Scope propio (el ITipologiaZipService es Scoped con su DbContext) y tenant
            // fijado por AsyncLocal, ya que fuera del request no hay HttpContext del que
            // CookieUserContext pueda leer el claim tenant_id.
            using var scope = _scopes.CreateScope();
            using var _ = TenantAmbient.Scope(tenantId, userId, sucursalId);
            var svc = scope.ServiceProvider.GetRequiredService<ITipologiaZipService>();

            var progreso = new Progress<ZipProgreso>(p =>
            {
                job.Fase = p.Fase;
                job.Hechos = p.Hechos;
                job.Total = p.Total;
            });

            var archivo = await svc.GenerarZipArchivoAsync(snapshotId, archivoItemId, progreso, CancellationToken.None);
            if (archivo is null)
            {
                job.Estado = ZipJobEstado.Error;
                job.Error = "No se encontro el snapshot o el archivo.";
            }
            else
            {
                job.Resultado = archivo.Contenido;
                job.NombreArchivo = archivo.NombreArchivo;
                job.Fase = "Listo";
                job.Estado = ZipJobEstado.Listo;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ZIP job {Job} fallo (snapshot {Snap}, archivo {Item})", job.Id, snapshotId, archivoItemId);
            job.Estado = ZipJobEstado.Error;
            job.Error = ex.Message;
        }
        finally
        {
            job.Terminado = DateTimeOffset.UtcNow;
        }
    }

    public ZipJobInfo? Obtener(Guid jobId) => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public (byte[] Bytes, string Nombre)? TomarResultado(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var j) && j.Estado == ZipJobEstado.Listo && j.Resultado is { } bytes)
        {
            return (bytes, j.NombreArchivo ?? "archivo.zip");
        }
        return null;
    }

    private void PurgarViejos()
    {
        var limite = DateTimeOffset.UtcNow - Retencion;
        foreach (var kv in _jobs)
        {
            if (kv.Value.Terminado is { } t && t < limite)
            {
                _jobs.TryRemove(kv.Key, out _);
            }
        }
    }
}

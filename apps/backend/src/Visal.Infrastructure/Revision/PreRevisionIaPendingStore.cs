using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;

namespace Visal.Infrastructure.Revision;

/// <summary>
/// Ola 9 RC9c — persiste los jobs del channel <c>PreRevisionIaQueue</c> en la
/// tabla staging <c>pre_revision_ia_pendings</c>. Sirve para reencolar al
/// startup del worker cuando el proceso muere con items encolados sin procesar.
///
/// NO es tenant-scoped: es infra interna del worker. Las tres operaciones son
/// simples INSERT/DELETE/SELECT sin filtros por tenant — el worker recorre lo
/// que haya y ata el tenant a cada job via <see cref="PreRevisionIaJob.TenantId"/>.
/// </summary>
// Ola 4c (P0): contexto corto por operacion (using var db = _dbFactory.Create();) en vez del
// IApplicationDbContext scoped. El store se inyecta en HistoriaClinicaService (circuito) y en el
// worker (scope propio); con contexto corto deja de compartir el DbContext del circuito.
public sealed class PreRevisionIaPendingStore : IPreRevisionIaPendingStore
{
    private readonly IApplicationDbContextFactory _dbFactory;

    public PreRevisionIaPendingStore(IApplicationDbContextFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Guid> InsertAsync(PreRevisionIaJob job, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var row = new PreRevisionIaPending
        {
            TenantId = job.TenantId,
            RevisionClinicaId = job.RevisionClinicaId,
            ActorUserId = job.ActorUserId,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
        db.PreRevisionIaPendings.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task DeleteAsync(Guid pendingId, CancellationToken ct = default)
    {
        if (pendingId == Guid.Empty) { return; }
        using var db = _dbFactory.Create();
        var row = await db.PreRevisionIaPendings.FirstOrDefaultAsync(p => p.Id == pendingId, ct);
        if (row is null) { return; }
        db.PreRevisionIaPendings.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PreRevisionIaJob>> LoadAllAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var rows = await db.PreRevisionIaPendings.AsNoTracking()
            .OrderBy(p => p.EnqueuedAt)
            .Select(p => new PreRevisionIaJob(p.TenantId, p.RevisionClinicaId, p.ActorUserId, p.Id))
            .ToListAsync(ct);
        return rows;
    }
}

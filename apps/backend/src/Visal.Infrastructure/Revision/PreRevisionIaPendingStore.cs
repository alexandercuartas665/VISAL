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
public sealed class PreRevisionIaPendingStore : IPreRevisionIaPendingStore
{
    private readonly IApplicationDbContext _db;

    public PreRevisionIaPendingStore(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> InsertAsync(PreRevisionIaJob job, CancellationToken ct = default)
    {
        var row = new PreRevisionIaPending
        {
            TenantId = job.TenantId,
            RevisionClinicaId = job.RevisionClinicaId,
            ActorUserId = job.ActorUserId,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
        _db.PreRevisionIaPendings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task DeleteAsync(Guid pendingId, CancellationToken ct = default)
    {
        if (pendingId == Guid.Empty) { return; }
        var row = await _db.PreRevisionIaPendings.FirstOrDefaultAsync(p => p.Id == pendingId, ct);
        if (row is null) { return; }
        _db.PreRevisionIaPendings.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PreRevisionIaJob>> LoadAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.PreRevisionIaPendings.AsNoTracking()
            .OrderBy(p => p.EnqueuedAt)
            .Select(p => new PreRevisionIaJob(p.TenantId, p.RevisionClinicaId, p.ActorUserId, p.Id))
            .ToListAsync(ct);
        return rows;
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

public sealed class TaskBoardColumnPrefService(IApplicationDbContext db, ITenantContext tenant)
    : ITaskBoardColumnPrefService
{
    public async Task<IReadOnlyList<TaskColumnPrefDto>> ListAsync(Guid boardId, Guid userId, CancellationToken ct = default)
    {
        return await db.TaskBoardColumnPrefs.AsNoTracking()
            .Where(x => x.BoardId == boardId && x.PlatformUserId == userId)
            .OrderBy(x => x.Orden ?? int.MaxValue).ThenBy(x => x.ColumnKey)
            .Select(x => new TaskColumnPrefDto(x.ColumnKey, x.Visible, x.Alias, x.Orden, x.Ancho))
            .ToListAsync(ct);
    }

    public async Task GuardarLoteAsync(Guid boardId, Guid userId, IReadOnlyList<SaveTaskColumnPrefRequest> items, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        if (userId == Guid.Empty) { throw new InvalidOperationException("Sin usuario activo."); }
        if (items is null || items.Count == 0) { return; }

        // Merge por (usuario, tablero, columna_key). Fila con default puro (Visible=true,
        // Alias=null, Orden=null, Ancho=null) -> se borra: la UI cae al default sola.
        var existentes = await db.TaskBoardColumnPrefs
            .Where(x => x.BoardId == boardId && x.PlatformUserId == userId)
            .ToDictionaryAsync(x => x.ColumnKey, ct);

        foreach (var req in items)
        {
            var key = (req.ColumnKey ?? "").Trim();
            if (string.IsNullOrEmpty(key)) { continue; }
            var alias = string.IsNullOrWhiteSpace(req.Alias) ? null : req.Alias!.Trim();
            var esDefault = req.Visible && alias is null && req.Orden is null && req.Ancho is null;

            existentes.TryGetValue(key, out var row);
            if (esDefault)
            {
                if (row is not null) { db.TaskBoardColumnPrefs.Remove(row); }
                continue;
            }
            if (row is null)
            {
                db.TaskBoardColumnPrefs.Add(new TaskBoardColumnPref
                {
                    TenantId = tid,
                    PlatformUserId = userId,
                    BoardId = boardId,
                    ColumnKey = key,
                    Visible = req.Visible,
                    Alias = alias,
                    Orden = req.Orden,
                    Ancho = req.Ancho
                });
            }
            else
            {
                row.Visible = req.Visible;
                row.Alias = alias;
                row.Orden = req.Orden;
                row.Ancho = req.Ancho;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}

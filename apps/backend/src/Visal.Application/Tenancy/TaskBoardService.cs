using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// Implementacion de <see cref="ITaskBoardService"/>. TODAS las operaciones
/// aplican gate por membership: si el actor no es Owner ni tiene una fila
/// en <see cref="TaskBoardMember"/> para el tablero, devuelve null/false/vacio
/// sin distinguir "no existe" de "no autorizado" — esto evita filtrar
/// existencia de tableros ajenos por diferencias de codigo de retorno.
/// </summary>
public sealed class TaskBoardService : ITaskBoardService
{
    /// <summary>Columnas por defecto al crear un tablero nuevo.</summary>
    private static readonly (string Name, string Color, bool IsDone)[] DefaultColumns =
    {
        ("Por hacer",   "#e2e8f0", false),
        ("En progreso", "#bfdbfe", false),
        ("En revision", "#fed7aa", false),
        ("Completado",  "#bbf7d0", true),
    };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUploadStorage _uploads;

    public TaskBoardService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit, IUploadStorage uploads)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _uploads = uploads;
    }

    // =====================================================================
    // Permisos (helpers privados y publicos)
    // =====================================================================

    public async Task<bool> HasAccessAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        var isOwner = await _db.TaskBoards.AsNoTracking()
            .AnyAsync(b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);
        if (isOwner) { return true; }
        return await _db.TaskBoardMembers.AsNoTracking()
            .AnyAsync(m => m.BoardId == boardId && m.PlatformUserId == actor, ct);
    }

    public Task<bool> IsOwnerAsync(Guid boardId, Guid actor, CancellationToken ct = default) =>
        _db.TaskBoards.AsNoTracking().AnyAsync(b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);

    private async Task<Guid?> ResolveBoardIdFromCardAsync(Guid cardId, CancellationToken ct)
    {
        return await _db.TaskCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => (Guid?)c.BoardId)
            .FirstOrDefaultAsync(ct);
    }

    // =====================================================================
    // Tableros
    // =====================================================================

    public async Task<IReadOnlyList<TaskBoardDto>> ListMyBoardsAsync(Guid actor, bool includeArchived = false, CancellationToken ct = default)
    {
        // "Mis tableros" = owner O miembro. Consulta en 2 pasos para no
        // pelear con EF Core traduciendo OR entre tablas distintas tras
        // los query filters.
        var ownedIds = await _db.TaskBoards.AsNoTracking()
            .Where(b => b.OwnerPlatformUserId == actor && (includeArchived || !b.IsArchived))
            .Select(b => b.Id).ToListAsync(ct);
        var invitedIds = await _db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.PlatformUserId == actor)
            .Select(m => m.BoardId).ToListAsync(ct);
        var accessibleIds = ownedIds.Concat(invitedIds).Distinct().ToList();
        if (accessibleIds.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boards = await _db.TaskBoards.AsNoTracking()
            .Where(b => accessibleIds.Contains(b.Id) && (includeArchived || !b.IsArchived))
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .ToListAsync(ct);
        if (boards.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boardIds = boards.Select(b => b.Id).ToList();
        var doneColIds = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && c.IsDone)
            .Select(c => c.Id).ToListAsync(ct);
        var counts = (await _db.TaskCards.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && !c.IsArchived)
            .Select(c => new { c.BoardId, c.ColumnId })
            .ToListAsync(ct))
            .GroupBy(c => c.BoardId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Done: g.Count(c => doneColIds.Contains(c.ColumnId))));
        var members = (await _db.TaskBoardMembers.AsNoTracking()
            .Where(m => boardIds.Contains(m.BoardId))
            .Select(m => m.BoardId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return boards.Select(b =>
        {
            counts.TryGetValue(b.Id, out var c);
            members.TryGetValue(b.Id, out var mem);
            // Owner + miembros invitados (owner no cuenta como fila en members):
            var totalMembers = 1 + mem;
            return new TaskBoardDto(
                b.Id, b.Name, b.Description, b.Color, b.SortOrder, b.IsArchived,
                b.OwnerPlatformUserId, b.OwnerPlatformUserId == actor,
                c.Total, c.Done, totalMembers);
        }).ToList();
    }

    public async Task<TaskBoardDto?> GetBoardAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(boardId, actor, ct)) { return null; }
        var b = await _db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, ct);
        if (b is null) { return null; }
        var doneColIds = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId && c.IsDone).Select(c => c.Id).ToListAsync(ct);
        var cards = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .Select(c => c.ColumnId).ToListAsync(ct);
        var total = cards.Count;
        var done = cards.Count(cid => doneColIds.Contains(cid));
        var mem = await _db.TaskBoardMembers.AsNoTracking().CountAsync(m => m.BoardId == boardId, ct);
        return new TaskBoardDto(
            b.Id, b.Name, b.Description, b.Color, b.SortOrder, b.IsArchived,
            b.OwnerPlatformUserId, b.OwnerPlatformUserId == actor,
            total, done, 1 + mem);
    }

    public async Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var name = (req.Name ?? "Tablero").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await _db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var board = new TaskBoard
        {
            TenantId = tid,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            SortOrder = nextOrder,
            OwnerPlatformUserId = actor,
            CreatedBy = actor,
        };
        _db.TaskBoards.Add(board);

        // Columnas por defecto — el tablero es usable desde el primer click.
        for (var i = 0; i < DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = DefaultColumns[i];
            _db.TaskBoardColumns.Add(new TaskBoardColumn
            {
                TenantId = tid, BoardId = board.Id,
                Name = cname, Color = ccolor,
                SortOrder = i, IsDone = isDone,
                CreatedBy = actor,
            });
        }

        _audit.Write(actor, "task-board.create", nameof(TaskBoard), board.Id,
            previousValue: null, newValue: new { board.Name }, tenantId: tid);
        await _db.SaveChangesAsync(ct);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color,
            board.SortOrder, board.IsArchived, actor, true, 0, 0, 1);
    }

    public async Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest req, Guid actor, CancellationToken ct = default)
    {
        // Solo miembros pueden editar metadatos del tablero (nombre/desc/color/archivar).
        if (!await HasAccessAsync(boardId, actor, ct)) { return null; }
        var b = await _db.TaskBoards.FirstOrDefaultAsync(x => x.Id == boardId, ct);
        if (b is null) { return null; }
        b.Name = (req.Name ?? b.Name).Trim();
        b.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        b.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        b.IsArchived = req.IsArchived;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        b.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);
        return await GetBoardAsync(boardId, actor, ct);
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        // Solo el owner puede borrar el tablero.
        var b = await _db.TaskBoards.FirstOrDefaultAsync(x => x.Id == boardId && x.OwnerPlatformUserId == actor, ct);
        if (b is null) { return false; }
        _db.TaskBoards.Remove(b);
        _audit.Write(actor, "task-board.delete", nameof(TaskBoard), b.Id,
            previousValue: new { b.Name }, newValue: null, tenantId: b.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Columnas
    // =====================================================================

    public async Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskBoardColumnDto>(); }
        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .GroupBy(c => c.ColumnId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return columns.Select(c => new TaskBoardColumnDto(c.Id, c.BoardId, c.Name, c.Color, c.SortOrder, c.IsDone,
            counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var name = (req.Name ?? "Columna").Trim();
        if (name.Length == 0) { return null; }
        var nextOrder = (await _db.TaskBoardColumns.Where(c => c.BoardId == req.BoardId)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var col = new TaskBoardColumn
        {
            TenantId = tid, BoardId = req.BoardId,
            Name = name, Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            IsDone = req.IsDone, SortOrder = nextOrder, CreatedBy = actor,
        };
        _db.TaskBoardColumns.Add(col);
        await _db.SaveChangesAsync(ct);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, 0);
    }

    public async Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest req, Guid actor, CancellationToken ct = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, ct);
        if (col is null || !await HasAccessAsync(col.BoardId, actor, ct)) { return null; }
        col.Name = (req.Name ?? col.Name).Trim();
        col.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        col.IsDone = req.IsDone;
        col.UpdatedAt = DateTimeOffset.UtcNow;
        col.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);
        var count = await _db.TaskCards.CountAsync(c => c.ColumnId == col.Id && !c.IsArchived, ct);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, count);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, Guid actor, CancellationToken ct = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, ct);
        if (col is null || !await HasAccessAsync(col.BoardId, actor, ct)) { return false; }
        // No permitimos borrar columnas con tarjetas — hay que mover primero.
        if (await _db.TaskCards.AnyAsync(c => c.ColumnId == columnId && !c.IsArchived, ct)) { return false; }
        _db.TaskBoardColumns.Remove(col);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedIds, Guid actor, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(boardId, actor, ct)) { return false; }
        var columns = await _db.TaskBoardColumns.Where(c => c.BoardId == boardId).ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var col = columns.FirstOrDefault(c => c.Id == orderedIds[i]);
            if (col is not null) { col.SortOrder = i; }
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Etiquetas (catalogo por tablero)
    // =====================================================================

    public async Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskCardTagDto>(); }
        return await _db.TaskCardTags.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new TaskCardTagDto(t.Id, t.Name, t.Color)).ToListAsync(ct);
    }

    public async Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest req, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) { return null; }
        if (await _db.TaskCardTags.AnyAsync(t => t.BoardId == req.BoardId && t.Name == name, ct)) { return null; }
        var nextOrder = (await _db.TaskCardTags.Where(t => t.BoardId == req.BoardId)
            .Select(t => (int?)t.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var tag = new TaskCardTag
        {
            TenantId = tid, BoardId = req.BoardId, Name = name,
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            SortOrder = nextOrder, CreatedBy = actor,
        };
        _db.TaskCardTags.Add(tag);
        await _db.SaveChangesAsync(ct);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest req, Guid actor, CancellationToken ct = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null || !await HasAccessAsync(tag.BoardId, actor, ct)) { return null; }
        tag.Name = (req.Name ?? tag.Name).Trim();
        tag.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        tag.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actor, CancellationToken ct = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null || !await HasAccessAsync(tag.BoardId, actor, ct)) { return false; }
        _db.TaskCardTags.Remove(tag);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Tarjetas
    // =====================================================================

    public async Task<IReadOnlyList<TaskCardSummaryDto>> ListCardsAsync(Guid boardId, Guid actor, bool includeArchived = false, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskCardSummaryDto>(); }
        var cards = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt).ToListAsync(ct);
        if (cards.Count == 0) { return Array.Empty<TaskCardSummaryDto>(); }
        var cardIds = cards.Select(c => c.Id).ToList();

        var members = (await _db.TaskCardAssignments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .Select(a => new { a.TaskCardId, a.PlatformUserId, a.DisplayName }).ToListAsync(ct))
            .GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Select(x => new TaskCardMemberDto(x.PlatformUserId, Initials(x.DisplayName), x.DisplayName))
                .ToList().AsReadOnly());
        var tagAssignments = await _db.TaskCardTagAssignments.AsNoTracking()
            .Where(x => cardIds.Contains(x.TaskCardId))
            .Select(x => new { x.TaskCardId, x.TagId }).ToListAsync(ct);
        var tagIds = tagAssignments.Select(x => x.TagId).Distinct().ToList();
        var tagsById = (await _db.TaskCardTags.AsNoTracking()
            .Where(t => tagIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Color }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => new TaskCardTagDto(x.Id, x.Name, x.Color));
        var tagsByCard = tagAssignments.GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Where(x => tagsById.ContainsKey(x.TagId)).Select(x => tagsById[x.TagId]).ToList().AsReadOnly());
        var checklists = (await _db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => cardIds.Contains(i.TaskCardId))
            .Select(i => new { i.TaskCardId, i.IsCompleted }).ToListAsync(ct))
            .GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Done: g.Count(x => x.IsCompleted)));
        var comments = (await _db.TaskCardActivities.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId) && a.Type == TaskActivityType.Comment)
            .Select(a => a.TaskCardId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var attachments = (await _db.TaskCardAttachments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .Select(a => a.TaskCardId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return cards.Select(c =>
        {
            checklists.TryGetValue(c.Id, out var chk);
            return new TaskCardSummaryDto(
                c.Id, c.BoardId, c.ColumnId, c.Title, c.Description,
                c.DueAt, c.SortOrder, c.IsArchived, c.Color,
                members.TryGetValue(c.Id, out var m) ? m : (IReadOnlyList<TaskCardMemberDto>)Array.Empty<TaskCardMemberDto>(),
                tagsByCard.TryGetValue(c.Id, out var t) ? t : (IReadOnlyList<TaskCardTagDto>)Array.Empty<TaskCardTagDto>(),
                chk.Total, chk.Done,
                comments.TryGetValue(c.Id, out var cm) ? cm : 0,
                attachments.TryGetValue(c.Id, out var at) ? at : 0);
        }).ToList();
    }

    public async Task<TaskCardDetailDto?> GetCardAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        var boardId = await ResolveBoardIdFromCardAsync(cardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return null; }
        var summaries = await ListCardsAsync(boardId.Value, actor, includeArchived: true, ct);
        var summary = summaries.FirstOrDefault(s => s.Id == cardId);
        if (summary is null) { return null; }
        var checklist = await _db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => i.TaskCardId == cardId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new TaskCardChecklistItemDto(i.Id, i.Text, i.IsCompleted, i.SortOrder))
            .ToListAsync(ct);
        var activity = await _db.TaskCardActivities.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardActivityDto(a.Id, a.Type, a.ActorName, a.Text, a.CreatedAt))
            .ToListAsync(ct);
        var attachments = await _db.TaskCardAttachments.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardAttachmentDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.UploadedByName, a.CreatedAt))
            .ToListAsync(ct);
        return new TaskCardDetailDto(summary, checklist, activity, attachments);
    }

    public async Task<TaskCardSummaryDto?> CreateCardAsync(CreateTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) { return null; }
        // Valida que la columna sea del mismo tablero.
        var colOk = await _db.TaskBoardColumns.AnyAsync(c => c.Id == req.ColumnId && c.BoardId == req.BoardId, ct);
        if (!colOk) { return null; }
        var nextOrder = (await _db.TaskCards.Where(c => c.ColumnId == req.ColumnId)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var card = new TaskCard
        {
            TenantId = tid, BoardId = req.BoardId, ColumnId = req.ColumnId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            DueAt = req.DueAt, SortOrder = nextOrder, CreatedBy = actor,
        };
        _db.TaskCards.Add(card);
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = card.Id, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = "creo la tarjeta", CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return new TaskCardSummaryDto(card.Id, card.BoardId, card.ColumnId, card.Title, card.Description,
            card.DueAt, card.SortOrder, card.IsArchived, card.Color,
            Array.Empty<TaskCardMemberDto>(), Array.Empty<TaskCardTagDto>(), 0, 0, 0, 0);
    }

    public async Task<TaskCardSummaryDto?> UpdateCardAsync(Guid cardId, UpdateTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var titlePrev = card.Title;
        card.Title = (req.Title ?? card.Title).Trim();
        card.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        card.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        card.DueAt = req.DueAt;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        card.UpdatedBy = actor;
        if (!string.Equals(titlePrev, card.Title, StringComparison.Ordinal))
        {
            _db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = $"renombro la tarjeta a \"{card.Title}\"", CreatedBy = actor,
            });
        }
        await _db.SaveChangesAsync(ct);
        var summaries = await ListCardsAsync(card.BoardId, actor, includeArchived: true, ct);
        return summaries.FirstOrDefault(s => s.Id == cardId);
    }

    public async Task<bool> MoveCardAsync(Guid cardId, MoveTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        // Valida columna del mismo tablero.
        var destCol = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == req.ColumnId && c.BoardId == card.BoardId, ct);
        if (destCol is null) { return false; }
        var origColId = card.ColumnId;
        card.ColumnId = req.ColumnId;
        card.SortOrder = Math.Max(0, req.SortOrder);
        card.UpdatedAt = DateTimeOffset.UtcNow;
        card.UpdatedBy = actor;
        if (origColId != req.ColumnId)
        {
            _db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = $"movio la tarjeta a \"{destCol.Name}\"", CreatedBy = actor,
            });
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ArchiveCardAsync(Guid cardId, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        card.IsArchived = !card.IsArchived;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        card.UpdatedBy = actor;
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = card.IsArchived ? "archivo la tarjeta" : "desarchivo la tarjeta",
            CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteCardAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        _db.TaskCards.Remove(card);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Miembros de la tarjeta
    // =====================================================================

    public async Task<TaskCardMemberDto?> AssignMemberToCardAsync(AssignMemberRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        // El asignado debe ser miembro del tablero (owner o invitado).
        if (!await HasAccessAsync(card.BoardId, req.PlatformUserId, ct)) { return null; }
        // Nombre a mostrar: si ya hay asignacion previa, reusa; si no, busca el PlatformUser.
        var existing = await _db.TaskCardAssignments.FirstOrDefaultAsync(
            a => a.TaskCardId == req.TaskCardId && a.PlatformUserId == req.PlatformUserId, ct);
        if (existing is not null)
        {
            return new TaskCardMemberDto(existing.PlatformUserId, Initials(existing.DisplayName), existing.DisplayName);
        }
        var displayName = await _db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == req.PlatformUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct) ?? "Usuario";
        var assignment = new TaskCardAssignment
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            PlatformUserId = req.PlatformUserId, DisplayName = displayName, CreatedBy = actor,
        };
        _db.TaskCardAssignments.Add(assignment);
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"asigno a {displayName}", CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return new TaskCardMemberDto(assignment.PlatformUserId, Initials(displayName), displayName);
    }

    public async Task<bool> UnassignMemberFromCardAsync(Guid cardId, Guid platformUserId, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var a = await _db.TaskCardAssignments.FirstOrDefaultAsync(
            x => x.TaskCardId == cardId && x.PlatformUserId == platformUserId, ct);
        if (a is null) { return false; }
        _db.TaskCardAssignments.Remove(a);
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = card.TenantId, TaskCardId = cardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"quito a {a.DisplayName}", CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Etiquetas de la tarjeta
    // =====================================================================

    public async Task<bool> AttachTagToCardAsync(Guid cardId, Guid tagId, Guid actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return false; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId && t.BoardId == card.BoardId, ct);
        if (tag is null) { return false; }
        var exists = await _db.TaskCardTagAssignments.AnyAsync(x => x.TaskCardId == cardId && x.TagId == tagId, ct);
        if (exists) { return true; }
        _db.TaskCardTagAssignments.Add(new TaskCardTagAssignment
        {
            TenantId = tid, TaskCardId = cardId, TagId = tagId, CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DetachTagFromCardAsync(Guid cardId, Guid tagId, Guid actor, CancellationToken ct = default)
    {
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var x = await _db.TaskCardTagAssignments.FirstOrDefaultAsync(a => a.TaskCardId == cardId && a.TagId == tagId, ct);
        if (x is null) { return false; }
        _db.TaskCardTagAssignments.Remove(x);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Checklist
    // =====================================================================

    public async Task<TaskCardChecklistItemDto?> AddChecklistItemAsync(AddChecklistItemRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var next = (await _db.TaskCardChecklistItems.Where(i => i.TaskCardId == req.TaskCardId)
            .Select(i => (int?)i.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var item = new TaskCardChecklistItem
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            Text = text, SortOrder = next, CreatedBy = actor,
        };
        _db.TaskCardChecklistItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return new TaskCardChecklistItemDto(item.Id, item.Text, item.IsCompleted, item.SortOrder);
    }

    public async Task<bool> UpdateChecklistItemAsync(Guid itemId, UpdateChecklistItemRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        var item = await _db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(item.TaskCardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return false; }
        var wasCompleted = item.IsCompleted;
        item.Text = (req.Text ?? item.Text).Trim();
        item.IsCompleted = req.IsCompleted;
        item.CompletedAt = req.IsCompleted ? DateTimeOffset.UtcNow : null;
        item.CompletedBy = req.IsCompleted ? actor : null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedBy = actor;
        if (wasCompleted != item.IsCompleted)
        {
            _db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = item.TenantId, TaskCardId = item.TaskCardId, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = item.IsCompleted ? $"marco \"{item.Text}\" como completado" : $"desmarco \"{item.Text}\"",
                CreatedBy = actor,
            });
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteChecklistItemAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        var item = await _db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(item.TaskCardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return false; }
        _db.TaskCardChecklistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Comentarios / actividad
    // =====================================================================

    public async Task<TaskCardActivityDto?> AddCommentAsync(AddCommentRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var activity = new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Comment,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = text, CreatedBy = actor,
        };
        _db.TaskCardActivities.Add(activity);
        await _db.SaveChangesAsync(ct);
        return new TaskCardActivityDto(activity.Id, activity.Type, activity.ActorName, activity.Text, activity.CreatedAt);
    }

    // =====================================================================
    // Adjuntos
    // =====================================================================

    public async Task<TaskCardAttachmentDto?> AddAttachmentAsync(AddAttachmentRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var file = new TaskCardAttachment
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            FileName = req.FileName, Url = req.Url, MimeType = req.MimeType, SizeBytes = req.SizeBytes,
            UploadedBy = actor, UploadedByName = actorDisplayName, CreatedBy = actor,
        };
        _db.TaskCardAttachments.Add(file);
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"adjunto \"{req.FileName}\"", CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return new TaskCardAttachmentDto(file.Id, file.FileName, file.Url, file.MimeType, file.SizeBytes, file.UploadedByName, file.CreatedAt);
    }

    public async Task<TaskCardAttachmentDto?> UploadAttachmentAsync(Guid cardId, string fileName, string? mimeType, byte[] content, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (content is null || content.Length == 0) { return null; }
        var card = await _db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "archivo.bin" : Path.GetFileName(fileName);
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrWhiteSpace(ext)) { ext = ".bin"; }
        var storedName = $"card-{Guid.NewGuid():N}{ext}";
        var url = await _uploads.GuardarAsync("tableros", storedName, content, ct);

        var mime = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType;
        var file = new TaskCardAttachment
        {
            TenantId = tid, TaskCardId = cardId,
            FileName = safeName, Url = url, MimeType = mime, SizeBytes = content.LongLength,
            UploadedBy = actor, UploadedByName = actorDisplayName, CreatedBy = actor,
        };
        _db.TaskCardAttachments.Add(file);
        _db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = cardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"adjunto \"{safeName}\"", CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);
        return new TaskCardAttachmentDto(file.Id, file.FileName, file.Url, file.MimeType, file.SizeBytes, file.UploadedByName, file.CreatedAt);
    }

    public async Task<bool> DeleteAttachmentAsync(Guid attachmentId, Guid actor, CancellationToken ct = default)
    {
        var f = await _db.TaskCardAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (f is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(f.TaskCardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return false; }
        _db.TaskCardAttachments.Remove(f);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    /// <summary>Iniciales para el avatar-chip: primera letra de las 2 primeras palabras.</summary>
    internal static string Initials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) { return "?"; }
        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return "?"; }
        var first = char.ToUpperInvariant(parts[0][0]);
        if (parts.Length == 1) { return first.ToString(); }
        var second = char.ToUpperInvariant(parts[1][0]);
        return $"{first}{second}";
    }
}

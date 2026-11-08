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
// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
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

    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly IUploadStorage _uploads;

    public TaskBoardService(IApplicationDbContextFactory dbFactory, ITenantContext tenant, IAuditWriter audit, IUploadStorage uploads)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _audit = audit;
        _uploads = uploads;
    }

    // =====================================================================
    // Permisos (helpers privados y publicos)
    // =====================================================================

    public async Task<bool> HasAccessAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var isOwner = await db.TaskBoards.AsNoTracking()
            .AnyAsync(b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);
        if (isOwner) { return true; }
        return await db.TaskBoardMembers.AsNoTracking()
            .AnyAsync(m => m.BoardId == boardId && m.PlatformUserId == actor, ct);
    }

    public async Task<bool> IsOwnerAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        return await db.TaskBoards.AsNoTracking().AnyAsync(b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);
    }

    private async Task<Guid?> ResolveBoardIdFromCardAsync(IApplicationDbContext db, Guid cardId, CancellationToken ct)
    {
        return await db.TaskCards.AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => (Guid?)c.BoardId)
            .FirstOrDefaultAsync(ct);
    }

    // =====================================================================
    // Tableros
    // =====================================================================

    public async Task<IReadOnlyList<TaskBoardDto>> ListMyBoardsAsync(Guid actor, bool includeArchived = false, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        // "Mis tableros" = owner O miembro. Consulta en 2 pasos para no
        // pelear con EF Core traduciendo OR entre tablas distintas tras
        // los query filters.
        var ownedIds = await db.TaskBoards.AsNoTracking()
            .Where(b => b.OwnerPlatformUserId == actor && (includeArchived || !b.IsArchived))
            .Select(b => b.Id).ToListAsync(ct);
        var invitedIds = await db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.PlatformUserId == actor)
            .Select(m => m.BoardId).ToListAsync(ct);
        var accessibleIds = ownedIds.Concat(invitedIds).Distinct().ToList();
        if (accessibleIds.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boards = await db.TaskBoards.AsNoTracking()
            .Where(b => accessibleIds.Contains(b.Id) && (includeArchived || !b.IsArchived))
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .ToListAsync(ct);
        if (boards.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boardIds = boards.Select(b => b.Id).ToList();
        var doneColIds = await db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && c.IsDone)
            .Select(c => c.Id).ToListAsync(ct);
        var counts = (await db.TaskCards.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && !c.IsArchived)
            .Select(c => new { c.BoardId, c.ColumnId })
            .ToListAsync(ct))
            .GroupBy(c => c.BoardId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Done: g.Count(c => doneColIds.Contains(c.ColumnId))));
        var members = (await db.TaskBoardMembers.AsNoTracking()
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
        using var db = _dbFactory.Create();
        if (!await HasAccessAsync(boardId, actor, ct)) { return null; }
        var b = await db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, ct);
        if (b is null) { return null; }
        var doneColIds = await db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId && c.IsDone).Select(c => c.Id).ToListAsync(ct);
        var cards = await db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .Select(c => c.ColumnId).ToListAsync(ct);
        var total = cards.Count;
        var done = cards.Count(cid => doneColIds.Contains(cid));
        var mem = await db.TaskBoardMembers.AsNoTracking().CountAsync(m => m.BoardId == boardId, ct);
        return new TaskBoardDto(
            b.Id, b.Name, b.Description, b.Color, b.SortOrder, b.IsArchived,
            b.OwnerPlatformUserId, b.OwnerPlatformUserId == actor,
            total, done, 1 + mem);
    }

    public async Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        var name = (req.Name ?? "Tablero").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(ct) ?? -1) + 1;
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
        db.TaskBoards.Add(board);

        // Columnas por defecto — el tablero es usable desde el primer click.
        for (var i = 0; i < DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = DefaultColumns[i];
            db.TaskBoardColumns.Add(new TaskBoardColumn
            {
                TenantId = tid, BoardId = board.Id,
                Name = cname, Color = ccolor,
                SortOrder = i, IsDone = isDone,
                CreatedBy = actor,
            });
        }

        _audit.Write(db, actor, "task-board.create", nameof(TaskBoard), board.Id,
            previousValue: null, newValue: new { board.Name }, tenantId: tid);
        await db.SaveChangesAsync(ct);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color,
            board.SortOrder, board.IsArchived, actor, true, 0, 0, 1);
    }

    public async Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        // Solo miembros pueden editar metadatos del tablero (nombre/desc/color/archivar).
        if (!await HasAccessAsync(boardId, actor, ct)) { return null; }
        var b = await db.TaskBoards.FirstOrDefaultAsync(x => x.Id == boardId, ct);
        if (b is null) { return null; }
        b.Name = (req.Name ?? b.Name).Trim();
        b.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        b.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        b.IsArchived = req.IsArchived;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        b.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
        return await GetBoardAsync(boardId, actor, ct);
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        // Solo el owner puede borrar el tablero.
        var b = await db.TaskBoards.FirstOrDefaultAsync(x => x.Id == boardId && x.OwnerPlatformUserId == actor, ct);
        if (b is null) { return false; }
        db.TaskBoards.Remove(b);
        _audit.Write(db, actor, "task-board.delete", nameof(TaskBoard), b.Id,
            previousValue: new { b.Name }, newValue: null, tenantId: b.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Columnas
    // =====================================================================

    public async Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskBoardColumnDto>(); }
        var columns = await db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var counts = await db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .GroupBy(c => c.ColumnId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return columns.Select(c => new TaskBoardColumnDto(c.Id, c.BoardId, c.Name, c.Color, c.SortOrder, c.IsDone,
            counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var name = (req.Name ?? "Columna").Trim();
        if (name.Length == 0) { return null; }
        var nextOrder = (await db.TaskBoardColumns.Where(c => c.BoardId == req.BoardId)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var col = new TaskBoardColumn
        {
            TenantId = tid, BoardId = req.BoardId,
            Name = name, Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            IsDone = req.IsDone, SortOrder = nextOrder, CreatedBy = actor,
        };
        db.TaskBoardColumns.Add(col);
        await db.SaveChangesAsync(ct);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, 0);
    }

    public async Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var col = await db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, ct);
        if (col is null || !await HasAccessAsync(col.BoardId, actor, ct)) { return null; }
        col.Name = (req.Name ?? col.Name).Trim();
        col.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        col.IsDone = req.IsDone;
        col.UpdatedAt = DateTimeOffset.UtcNow;
        col.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
        var count = await db.TaskCards.CountAsync(c => c.ColumnId == col.Id && !c.IsArchived, ct);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, count);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var col = await db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, ct);
        if (col is null || !await HasAccessAsync(col.BoardId, actor, ct)) { return false; }
        // No permitimos borrar columnas con tarjetas — hay que mover primero.
        if (await db.TaskCards.AnyAsync(c => c.ColumnId == columnId && !c.IsArchived, ct)) { return false; }
        db.TaskBoardColumns.Remove(col);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedIds, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (!await HasAccessAsync(boardId, actor, ct)) { return false; }
        var columns = await db.TaskBoardColumns.Where(c => c.BoardId == boardId).ToListAsync(ct);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var col = columns.FirstOrDefault(c => c.Id == orderedIds[i]);
            if (col is not null) { col.SortOrder = i; }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Etiquetas (catalogo por tablero)
    // =====================================================================

    public async Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskCardTagDto>(); }
        return await db.TaskCardTags.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new TaskCardTagDto(t.Id, t.Name, t.Color)).ToListAsync(ct);
    }

    public async Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) { return null; }
        if (await db.TaskCardTags.AnyAsync(t => t.BoardId == req.BoardId && t.Name == name, ct)) { return null; }
        var nextOrder = (await db.TaskCardTags.Where(t => t.BoardId == req.BoardId)
            .Select(t => (int?)t.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var tag = new TaskCardTag
        {
            TenantId = tid, BoardId = req.BoardId, Name = name,
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            SortOrder = nextOrder, CreatedBy = actor,
        };
        db.TaskCardTags.Add(tag);
        await db.SaveChangesAsync(ct);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var tag = await db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null || !await HasAccessAsync(tag.BoardId, actor, ct)) { return null; }
        tag.Name = (req.Name ?? tag.Name).Trim();
        tag.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        tag.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var tag = await db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, ct);
        if (tag is null || !await HasAccessAsync(tag.BoardId, actor, ct)) { return false; }
        db.TaskCardTags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Tarjetas
    // =====================================================================

    public async Task<IReadOnlyList<TaskCardSummaryDto>> ListCardsAsync(Guid boardId, Guid actor, bool includeArchived = false, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (!await HasAccessAsync(boardId, actor, ct)) { return Array.Empty<TaskCardSummaryDto>(); }
        var cards = await db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && (includeArchived || !c.IsArchived))
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt).ToListAsync(ct);
        if (cards.Count == 0) { return Array.Empty<TaskCardSummaryDto>(); }
        var cardIds = cards.Select(c => c.Id).ToList();

        var members = (await db.TaskCardAssignments.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId))
            .Select(a => new { a.TaskCardId, a.PlatformUserId, a.DisplayName }).ToListAsync(ct))
            .GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Select(x => new TaskCardMemberDto(x.PlatformUserId, Initials(x.DisplayName), x.DisplayName))
                .ToList().AsReadOnly());
        var tagAssignments = await db.TaskCardTagAssignments.AsNoTracking()
            .Where(x => cardIds.Contains(x.TaskCardId))
            .Select(x => new { x.TaskCardId, x.TagId }).ToListAsync(ct);
        var tagIds = tagAssignments.Select(x => x.TagId).Distinct().ToList();
        var tagsById = (await db.TaskCardTags.AsNoTracking()
            .Where(t => tagIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Color }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => new TaskCardTagDto(x.Id, x.Name, x.Color));
        var tagsByCard = tagAssignments.GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => g.Where(x => tagsById.ContainsKey(x.TagId)).Select(x => tagsById[x.TagId]).ToList().AsReadOnly());
        var checklists = (await db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => cardIds.Contains(i.TaskCardId))
            .Select(i => new { i.TaskCardId, i.IsCompleted }).ToListAsync(ct))
            .GroupBy(x => x.TaskCardId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Done: g.Count(x => x.IsCompleted)));
        var comments = (await db.TaskCardActivities.AsNoTracking()
            .Where(a => cardIds.Contains(a.TaskCardId) && a.Type == TaskActivityType.Comment)
            .Select(a => a.TaskCardId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var attachments = (await db.TaskCardAttachments.AsNoTracking()
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
                attachments.TryGetValue(c.Id, out var at) ? at : 0,
                DeserializeFieldValues(c.FieldValuesJson),
                c.CreatorName,
                c.CreatedAt,
                c.ColumnEnteredAt ?? c.CreatedAt,
                c.StartAt);
        }).ToList();
    }

    public async Task<TaskCardDetailDto?> GetCardAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var boardId = await ResolveBoardIdFromCardAsync(db, cardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return null; }
        var summaries = await ListCardsAsync(boardId.Value, actor, includeArchived: true, ct);
        var summary = summaries.FirstOrDefault(s => s.Id == cardId);
        if (summary is null) { return null; }
        var checklist = await db.TaskCardChecklistItems.AsNoTracking()
            .Where(i => i.TaskCardId == cardId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new TaskCardChecklistItemDto(i.Id, i.Text, i.IsCompleted, i.SortOrder))
            .ToListAsync(ct);
        var activity = await db.TaskCardActivities.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardActivityDto(a.Id, a.Type, a.ActorName, a.Text, a.CreatedAt))
            .ToListAsync(ct);
        var attachments = await db.TaskCardAttachments.AsNoTracking()
            .Where(a => a.TaskCardId == cardId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskCardAttachmentDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.UploadedByName, a.CreatedAt))
            .ToListAsync(ct);
        return new TaskCardDetailDto(summary, checklist, activity, attachments);
    }

    public async Task<TaskCardSummaryDto?> CreateCardAsync(CreateTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (!await HasAccessAsync(req.BoardId, actor, ct)) { return null; }
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) { return null; }
        // Valida que la columna sea del mismo tablero.
        var colOk = await db.TaskBoardColumns.AnyAsync(c => c.Id == req.ColumnId && c.BoardId == req.BoardId, ct);
        if (!colOk) { return null; }
        var nextOrder = (await db.TaskCards.Where(c => c.ColumnId == req.ColumnId)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var card = new TaskCard
        {
            TenantId = tid, BoardId = req.BoardId, ColumnId = req.ColumnId,
            Title = title,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            DueAt = req.DueAt, StartAt = req.StartAt, SortOrder = nextOrder, CreatedBy = actor,
            CreatorName = actorDisplayName,
            ColumnEnteredAt = DateTimeOffset.UtcNow,
        };
        db.TaskCards.Add(card);
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = card.Id, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = "creo la tarjeta", CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return new TaskCardSummaryDto(card.Id, card.BoardId, card.ColumnId, card.Title, card.Description,
            card.DueAt, card.SortOrder, card.IsArchived, card.Color,
            Array.Empty<TaskCardMemberDto>(), Array.Empty<TaskCardTagDto>(), 0, 0, 0, 0,
            CreadorNombre: card.CreatorName, CreadoEn: card.CreatedAt,
            EnColumnaDesde: card.ColumnEnteredAt, StartAt: card.StartAt);
    }

    public async Task<TaskCardSummaryDto?> UpdateCardAsync(Guid cardId, UpdateTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var titlePrev = card.Title;
        card.Title = (req.Title ?? card.Title).Trim();
        card.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        card.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        card.DueAt = req.DueAt;
        card.StartAt = req.StartAt;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        card.UpdatedBy = actor;
        if (!string.Equals(titlePrev, card.Title, StringComparison.Ordinal))
        {
            db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = $"renombro la tarjeta a \"{card.Title}\"", CreatedBy = actor,
            });
        }
        await db.SaveChangesAsync(ct);
        var summaries = await ListCardsAsync(card.BoardId, actor, includeArchived: true, ct);
        return summaries.FirstOrDefault(s => s.Id == cardId);
    }

    public async Task<bool> MoveCardAsync(Guid cardId, MoveTaskCardRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        // Valida columna del mismo tablero.
        var destCol = await db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == req.ColumnId && c.BoardId == card.BoardId, ct);
        if (destCol is null) { return false; }
        var ahora = DateTimeOffset.UtcNow;
        var origColId = card.ColumnId;
        var cambiaColumna = origColId != req.ColumnId;

        // Tarjetas de la columna DESTINO (sin la que movemos), en el orden visible.
        var destino = await db.TaskCards
            .Where(c => c.ColumnId == req.ColumnId && !c.IsArchived && c.Id != cardId)
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // Inserta la tarjeta en la posicion PEDIDA (no al final) y re-numera la columna
        // destino 0..N. Esto arregla el bug "desplazar no funciona / afecta el registro":
        // antes solo se grababa el SortOrder de la movida (al final) sin normalizar el
        // resto, dejando huecos/empates y un orden inestable en el Kanban.
        card.ColumnId = req.ColumnId;
        var idx = Math.Clamp(req.SortOrder, 0, destino.Count);
        destino.Insert(idx, card);
        for (var i = 0; i < destino.Count; i++)
        {
            if (destino[i].SortOrder != i)
            {
                destino[i].SortOrder = i;
                destino[i].UpdatedAt = ahora;
                destino[i].UpdatedBy = actor;
            }
        }
        card.UpdatedAt = ahora;
        card.UpdatedBy = actor;

        if (cambiaColumna)
        {
            // Reinicia el cronometro de "tiempo en columna" al cambiar de columna.
            card.ColumnEnteredAt = ahora;
            // Re-numera la columna ORIGEN (quedo con un hueco al salir la tarjeta).
            var origen = await db.TaskCards
                .Where(c => c.ColumnId == origColId && !c.IsArchived && c.Id != cardId)
                .OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt)
                .ToListAsync(ct);
            for (var i = 0; i < origen.Count; i++)
            {
                if (origen[i].SortOrder != i)
                {
                    origen[i].SortOrder = i;
                    origen[i].UpdatedAt = ahora;
                    origen[i].UpdatedBy = actor;
                }
            }
            db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = $"movio la tarjeta a \"{destCol.Name}\"", CreatedBy = actor,
            });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Sube (dir=-1) o baja (dir=+1) la tarjeta una posicion dentro de su
    /// columna, reindexando el SortOrder de toda la columna a 0..N para evitar
    /// empates/huecos. Devuelve false si la tarjeta ya esta en el extremo.</summary>
    private async Task<bool> MoverVerticalAsync(Guid cardId, int dir, Guid actor, CancellationToken ct)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        // Mismo orden que muestra la parrilla (SortOrder, luego CreatedAt desc).
        var cards = await db.TaskCards
            .Where(c => c.ColumnId == card.ColumnId && !c.IsArchived)
            .OrderBy(c => c.SortOrder).ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
        var idx = cards.FindIndex(c => c.Id == cardId);
        var destino = idx + dir;
        if (idx < 0 || destino < 0 || destino >= cards.Count) { return false; }
        (cards[idx], cards[destino]) = (cards[destino], cards[idx]);
        var ahora = DateTimeOffset.UtcNow;
        for (var i = 0; i < cards.Count; i++)
        {
            if (cards[i].SortOrder != i)
            {
                cards[i].SortOrder = i;
                cards[i].UpdatedAt = ahora;
                cards[i].UpdatedBy = actor;
            }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> MoveCardUpAsync(Guid cardId, Guid actor, CancellationToken ct = default)
        => MoverVerticalAsync(cardId, -1, actor, ct);

    public Task<bool> MoveCardDownAsync(Guid cardId, Guid actor, CancellationToken ct = default)
        => MoverVerticalAsync(cardId, +1, actor, ct);

    public async Task<bool> ArchiveCardAsync(Guid cardId, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        card.IsArchived = !card.IsArchived;
        card.UpdatedAt = DateTimeOffset.UtcNow;
        card.UpdatedBy = actor;
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = card.TenantId, TaskCardId = card.Id, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = card.IsArchived ? "archivo la tarjeta" : "desarchivo la tarjeta",
            CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteCardAsync(Guid cardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        db.TaskCards.Remove(card);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Miembros de la tarjeta
    // =====================================================================

    public async Task<TaskCardMemberDto?> AssignMemberToCardAsync(AssignMemberRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        // El asignado debe ser miembro del tablero (owner o invitado).
        if (!await HasAccessAsync(card.BoardId, req.PlatformUserId, ct)) { return null; }
        // Nombre a mostrar: si ya hay asignacion previa, reusa; si no, busca el PlatformUser.
        var existing = await db.TaskCardAssignments.FirstOrDefaultAsync(
            a => a.TaskCardId == req.TaskCardId && a.PlatformUserId == req.PlatformUserId, ct);
        if (existing is not null)
        {
            return new TaskCardMemberDto(existing.PlatformUserId, Initials(existing.DisplayName), existing.DisplayName);
        }
        var displayName = await db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == req.PlatformUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct) ?? "Usuario";
        var assignment = new TaskCardAssignment
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            PlatformUserId = req.PlatformUserId, DisplayName = displayName, CreatedBy = actor,
        };
        db.TaskCardAssignments.Add(assignment);
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"asigno a {displayName}", CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return new TaskCardMemberDto(assignment.PlatformUserId, Initials(displayName), displayName);
    }

    public async Task<bool> UnassignMemberFromCardAsync(Guid cardId, Guid platformUserId, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var a = await db.TaskCardAssignments.FirstOrDefaultAsync(
            x => x.TaskCardId == cardId && x.PlatformUserId == platformUserId, ct);
        if (a is null) { return false; }
        db.TaskCardAssignments.Remove(a);
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = card.TenantId, TaskCardId = cardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"quito a {a.DisplayName}", CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Etiquetas de la tarjeta
    // =====================================================================

    public async Task<bool> AttachTagToCardAsync(Guid cardId, Guid tagId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return false; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var tag = await db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId && t.BoardId == card.BoardId, ct);
        if (tag is null) { return false; }
        var exists = await db.TaskCardTagAssignments.AnyAsync(x => x.TaskCardId == cardId && x.TagId == tagId, ct);
        if (exists) { return true; }
        db.TaskCardTagAssignments.Add(new TaskCardTagAssignment
        {
            TenantId = tid, TaskCardId = cardId, TagId = tagId, CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DetachTagFromCardAsync(Guid cardId, Guid tagId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return false; }
        var x = await db.TaskCardTagAssignments.FirstOrDefaultAsync(a => a.TaskCardId == cardId && a.TagId == tagId, ct);
        if (x is null) { return false; }
        db.TaskCardTagAssignments.Remove(x);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Checklist
    // =====================================================================

    public async Task<TaskCardChecklistItemDto?> AddChecklistItemAsync(AddChecklistItemRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var next = (await db.TaskCardChecklistItems.Where(i => i.TaskCardId == req.TaskCardId)
            .Select(i => (int?)i.SortOrder).MaxAsync(ct) ?? -1) + 1;
        var item = new TaskCardChecklistItem
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            Text = text, SortOrder = next, CreatedBy = actor,
        };
        db.TaskCardChecklistItems.Add(item);
        await db.SaveChangesAsync(ct);
        return new TaskCardChecklistItemDto(item.Id, item.Text, item.IsCompleted, item.SortOrder);
    }

    public async Task<bool> UpdateChecklistItemAsync(Guid itemId, UpdateChecklistItemRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var item = await db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(db, item.TaskCardId, ct);
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
            db.TaskCardActivities.Add(new TaskCardActivity
            {
                TenantId = item.TenantId, TaskCardId = item.TaskCardId, Type = TaskActivityType.Action,
                ActorUserId = actor, ActorName = actorDisplayName,
                Text = item.IsCompleted ? $"marco \"{item.Text}\" como completado" : $"desmarco \"{item.Text}\"",
                CreatedBy = actor,
            });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteChecklistItemAsync(Guid itemId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var item = await db.TaskCardChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(db, item.TaskCardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return false; }
        db.TaskCardChecklistItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // =====================================================================
    // Comentarios / actividad
    // =====================================================================

    public async Task<TaskCardActivityDto?> AddCommentAsync(AddCommentRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) { return null; }
        var activity = new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Comment,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = text, CreatedBy = actor,
        };
        db.TaskCardActivities.Add(activity);
        await db.SaveChangesAsync(ct);
        return new TaskCardActivityDto(activity.Id, activity.Type, activity.ActorName, activity.Text, activity.CreatedAt);
    }

    // =====================================================================
    // Adjuntos
    // =====================================================================

    public async Task<TaskCardAttachmentDto?> AddAttachmentAsync(AddAttachmentRequest req, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == req.TaskCardId, ct);
        if (card is null || !await HasAccessAsync(card.BoardId, actor, ct)) { return null; }
        var file = new TaskCardAttachment
        {
            TenantId = tid, TaskCardId = req.TaskCardId,
            FileName = req.FileName, Url = req.Url, MimeType = req.MimeType, SizeBytes = req.SizeBytes,
            UploadedBy = actor, UploadedByName = actorDisplayName, CreatedBy = actor,
        };
        db.TaskCardAttachments.Add(file);
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = req.TaskCardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"adjunto \"{req.FileName}\"", CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return new TaskCardAttachmentDto(file.Id, file.FileName, file.Url, file.MimeType, file.SizeBytes, file.UploadedByName, file.CreatedAt);
    }

    public async Task<TaskCardAttachmentDto?> UploadAttachmentAsync(Guid cardId, string fileName, string? mimeType, byte[] content, Guid actor, string actorDisplayName, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        if (content is null || content.Length == 0) { return null; }
        var card = await db.TaskCards.FirstOrDefaultAsync(c => c.Id == cardId, ct);
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
        db.TaskCardAttachments.Add(file);
        db.TaskCardActivities.Add(new TaskCardActivity
        {
            TenantId = tid, TaskCardId = cardId, Type = TaskActivityType.Action,
            ActorUserId = actor, ActorName = actorDisplayName,
            Text = $"adjunto \"{safeName}\"", CreatedBy = actor,
        });
        await db.SaveChangesAsync(ct);
        return new TaskCardAttachmentDto(file.Id, file.FileName, file.Url, file.MimeType, file.SizeBytes, file.UploadedByName, file.CreatedAt);
    }

    public async Task<bool> DeleteAttachmentAsync(Guid attachmentId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var f = await db.TaskCardAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (f is null) { return false; }
        var boardId = await ResolveBoardIdFromCardAsync(db, f.TaskCardId, ct);
        if (boardId is null || !await HasAccessAsync(boardId.Value, actor, ct)) { return false; }
        db.TaskCardAttachments.Remove(f);
        await db.SaveChangesAsync(ct);
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

    /// <summary>Deserializa el jsonb de valores dinamicos (FieldKey -> valor) para la vista tabla.
    /// Devuelve null si esta vacio para no inflar el DTO.</summary>
    private static IReadOnlyDictionary<string, string?>? DeserializeFieldValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            return dict is { Count: > 0 } ? dict : null;
        }
        catch { return null; }
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Implementacion de <see cref="ITaskBoardMemberService"/>. Todas las
/// operaciones que MUTAN requieren que el actor sea Owner del tablero
/// (no basta con ser miembro). Las de lectura requieren ser Owner o miembro.
/// </summary>
// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class TaskBoardMemberService : ITaskBoardMemberService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public TaskBoardMemberService(IApplicationDbContextFactory dbFactory, ITenantContext tenant, IAuditWriter audit)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
        _audit = audit;
    }

    private async Task<TaskBoard?> LoadBoardIfActorHasAccessAsync(IApplicationDbContext db, Guid boardId, Guid actor, CancellationToken ct)
    {
        var board = await db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null) { return null; }
        if (board.OwnerPlatformUserId == actor) { return board; }
        var isMember = await db.TaskBoardMembers.AsNoTracking()
            .AnyAsync(m => m.BoardId == boardId && m.PlatformUserId == actor, ct);
        return isMember ? board : null;
    }

    public async Task<IReadOnlyList<TaskBoardMemberDto>> ListMembersAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var board = await LoadBoardIfActorHasAccessAsync(db, boardId, actor, ct);
        if (board is null) { return Array.Empty<TaskBoardMemberDto>(); }
        var invited = await db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId)
            .OrderBy(m => m.DisplayName)
            .Select(m => new { m.Id, m.PlatformUserId, m.DisplayName, m.CreatedAt })
            .ToListAsync(ct);

        var ownerDisplay = await db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == board.OwnerPlatformUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct) ?? "Owner";

        // El owner va primero como fila sintetica con IsOwner=true.
        var result = new List<TaskBoardMemberDto>(1 + invited.Count)
        {
            new(Guid.Empty, board.OwnerPlatformUserId, ownerDisplay,
                TaskBoardService.Initials(ownerDisplay), true, board.CreatedAt),
        };
        result.AddRange(invited.Select(x => new TaskBoardMemberDto(
            x.Id, x.PlatformUserId, x.DisplayName,
            TaskBoardService.Initials(x.DisplayName), false, x.CreatedAt)));
        return result;
    }

    public async Task<IReadOnlyList<TaskBoardInviteCandidateDto>> ListInviteCandidatesAsync(
        Guid boardId, Guid actor, string? search = null, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        var board = await LoadBoardIfActorHasAccessAsync(db, boardId, actor, ct);
        if (board is null) { return Array.Empty<TaskBoardInviteCandidateDto>(); }
        if (_tenant.TenantId is not Guid tid) { return Array.Empty<TaskBoardInviteCandidateDto>(); }

        // PlatformUsers "del tenant activo" via TenantUser. Excluir owner + ya miembros.
        var currentMemberIds = await db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId).Select(m => m.PlatformUserId).ToListAsync(ct);
        var excluded = new HashSet<Guid>(currentMemberIds) { board.OwnerPlatformUserId };

        var candidatesRaw = await db.TenantUsers.AsNoTracking()
            .Where(tu => tu.TenantId == tid)
            .Join(db.PlatformUsers.AsNoTracking(), tu => tu.PlatformUserId, pu => pu.Id,
                (tu, pu) => new { pu.Id, pu.DisplayName, pu.Email })
            .Distinct()
            .ToListAsync(ct);

        var q = candidatesRaw
            .Where(u => !excluded.Contains(u.Id))
            .AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u =>
                (u.DisplayName != null && u.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase))
                || u.Email.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(u => u.DisplayName ?? u.Email)
            .Select(u =>
            {
                var name = u.DisplayName ?? u.Email;
                return new TaskBoardInviteCandidateDto(u.Id, name, u.Email, TaskBoardService.Initials(name));
            })
            .ToList();
    }

    public async Task<TaskBoardMemberDto?> InviteMemberAsync(InviteMemberRequest req, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        if (_tenant.TenantId is not Guid tid) { return null; }
        // Solo el owner puede invitar.
        var board = await db.TaskBoards.FirstOrDefaultAsync(
            b => b.Id == req.BoardId && b.OwnerPlatformUserId == actor, ct);
        if (board is null) { return null; }
        // El owner no puede "invitarse a si mismo" — ya es dueno.
        if (req.PlatformUserId == board.OwnerPlatformUserId) { return null; }
        // Idempotente: si ya es miembro, devuelve la fila existente.
        var existing = await db.TaskBoardMembers.FirstOrDefaultAsync(
            m => m.BoardId == req.BoardId && m.PlatformUserId == req.PlatformUserId, ct);
        if (existing is not null)
        {
            return new TaskBoardMemberDto(existing.Id, existing.PlatformUserId, existing.DisplayName,
                TaskBoardService.Initials(existing.DisplayName), false, existing.CreatedAt);
        }
        // Snapshot del displayName al invitar — sobrevive cambios de perfil.
        var displayName = await db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == req.PlatformUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct);
        if (displayName is null) { return null; }
        var member = new TaskBoardMember
        {
            TenantId = tid, BoardId = req.BoardId, PlatformUserId = req.PlatformUserId,
            DisplayName = displayName, InvitedByPlatformUserId = actor, CreatedBy = actor,
        };
        db.TaskBoardMembers.Add(member);
        _audit.Write(db, actor, "task-board.invite", nameof(TaskBoardMember), member.Id,
            previousValue: null,
            newValue: new { req.BoardId, req.PlatformUserId, displayName },
            tenantId: tid);
        await db.SaveChangesAsync(ct);
        return new TaskBoardMemberDto(member.Id, member.PlatformUserId, member.DisplayName,
            TaskBoardService.Initials(member.DisplayName), false, member.CreatedAt);
    }

    public async Task<bool> RemoveMemberAsync(Guid boardId, Guid platformUserIdToRemove, Guid actor, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create();
        // Solo el owner puede quitar miembros.
        var board = await db.TaskBoards.FirstOrDefaultAsync(
            b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);
        if (board is null) { return false; }
        // No se puede quitar al owner (ni siquiera el owner a si mismo — para
        // "salir" se archiva o borra el tablero).
        if (platformUserIdToRemove == board.OwnerPlatformUserId) { return false; }
        var member = await db.TaskBoardMembers.FirstOrDefaultAsync(
            m => m.BoardId == boardId && m.PlatformUserId == platformUserIdToRemove, ct);
        if (member is null) { return false; }
        db.TaskBoardMembers.Remove(member);
        // Al remover, tambien quitamos sus asignaciones en cards del tablero
        // (no queremos chips huerfanos).
        var cardIds = await db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId).Select(c => c.Id).ToListAsync(ct);
        if (cardIds.Count > 0)
        {
            var stale = await db.TaskCardAssignments
                .Where(a => cardIds.Contains(a.TaskCardId) && a.PlatformUserId == platformUserIdToRemove)
                .ToListAsync(ct);
            db.TaskCardAssignments.RemoveRange(stale);
        }
        _audit.Write(db, actor, "task-board.remove-member", nameof(TaskBoardMember), member.Id,
            previousValue: new { member.PlatformUserId, member.DisplayName },
            newValue: null, tenantId: board.TenantId);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

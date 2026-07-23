using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Implementacion de <see cref="ITaskBoardMemberService"/>. Todas las
/// operaciones que MUTAN requieren que el actor sea Owner del tablero
/// (no basta con ser miembro). Las de lectura requieren ser Owner o miembro.
/// </summary>
public sealed class TaskBoardMemberService : ITaskBoardMemberService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public TaskBoardMemberService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    private async Task<TaskBoard?> LoadBoardIfActorHasAccessAsync(Guid boardId, Guid actor, CancellationToken ct)
    {
        var board = await _db.TaskBoards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boardId, ct);
        if (board is null) { return null; }
        if (board.OwnerPlatformUserId == actor) { return board; }
        var isMember = await _db.TaskBoardMembers.AsNoTracking()
            .AnyAsync(m => m.BoardId == boardId && m.PlatformUserId == actor, ct);
        return isMember ? board : null;
    }

    public async Task<IReadOnlyList<TaskBoardMemberDto>> ListMembersAsync(Guid boardId, Guid actor, CancellationToken ct = default)
    {
        var board = await LoadBoardIfActorHasAccessAsync(boardId, actor, ct);
        if (board is null) { return Array.Empty<TaskBoardMemberDto>(); }
        var invited = await _db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId)
            .OrderBy(m => m.DisplayName)
            .Select(m => new { m.Id, m.PlatformUserId, m.DisplayName, m.CreatedAt })
            .ToListAsync(ct);

        var ownerDisplay = await _db.PlatformUsers.AsNoTracking()
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
        var board = await LoadBoardIfActorHasAccessAsync(boardId, actor, ct);
        if (board is null) { return Array.Empty<TaskBoardInviteCandidateDto>(); }
        if (_tenant.TenantId is not Guid tid) { return Array.Empty<TaskBoardInviteCandidateDto>(); }

        // PlatformUsers "del tenant activo" via TenantUser. Excluir owner + ya miembros.
        var currentMemberIds = await _db.TaskBoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId).Select(m => m.PlatformUserId).ToListAsync(ct);
        var excluded = new HashSet<Guid>(currentMemberIds) { board.OwnerPlatformUserId };

        var candidatesRaw = await _db.TenantUsers.AsNoTracking()
            .Where(tu => tu.TenantId == tid)
            .Join(_db.PlatformUsers.AsNoTracking(), tu => tu.PlatformUserId, pu => pu.Id,
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
        if (_tenant.TenantId is not Guid tid) { return null; }
        // Solo el owner puede invitar.
        var board = await _db.TaskBoards.FirstOrDefaultAsync(
            b => b.Id == req.BoardId && b.OwnerPlatformUserId == actor, ct);
        if (board is null) { return null; }
        // El owner no puede "invitarse a si mismo" — ya es dueno.
        if (req.PlatformUserId == board.OwnerPlatformUserId) { return null; }
        // Idempotente: si ya es miembro, devuelve la fila existente.
        var existing = await _db.TaskBoardMembers.FirstOrDefaultAsync(
            m => m.BoardId == req.BoardId && m.PlatformUserId == req.PlatformUserId, ct);
        if (existing is not null)
        {
            return new TaskBoardMemberDto(existing.Id, existing.PlatformUserId, existing.DisplayName,
                TaskBoardService.Initials(existing.DisplayName), false, existing.CreatedAt);
        }
        // Snapshot del displayName al invitar — sobrevive cambios de perfil.
        var displayName = await _db.PlatformUsers.AsNoTracking()
            .Where(u => u.Id == req.PlatformUserId)
            .Select(u => u.DisplayName ?? u.Email)
            .FirstOrDefaultAsync(ct);
        if (displayName is null) { return null; }
        var member = new TaskBoardMember
        {
            TenantId = tid, BoardId = req.BoardId, PlatformUserId = req.PlatformUserId,
            DisplayName = displayName, InvitedByPlatformUserId = actor, CreatedBy = actor,
        };
        _db.TaskBoardMembers.Add(member);
        _audit.Write(actor, "task-board.invite", nameof(TaskBoardMember), member.Id,
            previousValue: null,
            newValue: new { req.BoardId, req.PlatformUserId, displayName },
            tenantId: tid);
        await _db.SaveChangesAsync(ct);
        return new TaskBoardMemberDto(member.Id, member.PlatformUserId, member.DisplayName,
            TaskBoardService.Initials(member.DisplayName), false, member.CreatedAt);
    }

    public async Task<bool> RemoveMemberAsync(Guid boardId, Guid platformUserIdToRemove, Guid actor, CancellationToken ct = default)
    {
        // Solo el owner puede quitar miembros.
        var board = await _db.TaskBoards.FirstOrDefaultAsync(
            b => b.Id == boardId && b.OwnerPlatformUserId == actor, ct);
        if (board is null) { return false; }
        // No se puede quitar al owner (ni siquiera el owner a si mismo — para
        // "salir" se archiva o borra el tablero).
        if (platformUserIdToRemove == board.OwnerPlatformUserId) { return false; }
        var member = await _db.TaskBoardMembers.FirstOrDefaultAsync(
            m => m.BoardId == boardId && m.PlatformUserId == platformUserIdToRemove, ct);
        if (member is null) { return false; }
        _db.TaskBoardMembers.Remove(member);
        // Al remover, tambien quitamos sus asignaciones en cards del tablero
        // (no queremos chips huerfanos).
        var cardIds = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId).Select(c => c.Id).ToListAsync(ct);
        if (cardIds.Count > 0)
        {
            var stale = await _db.TaskCardAssignments
                .Where(a => cardIds.Contains(a.TaskCardId) && a.PlatformUserId == platformUserIdToRemove)
                .ToListAsync(ct);
            _db.TaskCardAssignments.RemoveRange(stale);
        }
        _audit.Write(actor, "task-board.remove-member", nameof(TaskBoardMember), member.Id,
            previousValue: new { member.PlatformUserId, member.DisplayName },
            newValue: null, tenantId: board.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

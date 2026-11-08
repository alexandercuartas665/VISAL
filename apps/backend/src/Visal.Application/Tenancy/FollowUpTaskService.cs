using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class FollowUpTaskService : IFollowUpTaskService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public FollowUpTaskService(IApplicationDbContextFactory dbFactory, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<FollowUpTaskDto>> ListAsync(Guid? leadId = null, FollowUpTaskStatus? status = null, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var query = db.FollowUpTasks.AsNoTracking();
        if (leadId is Guid l)
        {
            query = query.Where(t => t.LeadId == l);
        }

        if (status is FollowUpTaskStatus s)
        {
            query = query.Where(t => t.Status == s);
        }

        return await query
            .OrderBy(t => t.DueAt)
            .Select(t => Map(t))
            .ToListAsync(cancellationToken);
    }

    public async Task<FollowUpTaskDto?> CreateAsync(CreateFollowUpTaskRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        using var db = _dbFactory.Create();
        var leadExists = await db.Leads.AnyAsync(l => l.Id == request.LeadId, cancellationToken);
        if (!leadExists)
        {
            return null;
        }

        if (request.AssignedToTenantUserId is Guid assignee
            && !await db.TenantUsers.AnyAsync(tu => tu.Id == assignee, cancellationToken))
        {
            return null;
        }

        var task = new FollowUpTask
        {
            TenantId = tenantId,
            LeadId = request.LeadId,
            Title = request.Title.Trim(),
            Notes = request.Notes?.Trim(),
            DueAt = request.DueAt,
            Status = FollowUpTaskStatus.Pending,
            AssignedToTenantUserId = request.AssignedToTenantUserId
        };
        db.FollowUpTasks.Add(task);

        // Deja rastro en el historial del lead.
        db.LeadActivities.Add(new LeadActivity
        {
            TenantId = tenantId,
            LeadId = request.LeadId,
            ActivityType = "lead.followup.created",
            Description = $"Seguimiento '{task.Title}' para {task.DueAt:yyyy-MM-dd}"
        });

        await db.SaveChangesAsync(cancellationToken);
        return Map(task);
    }

    public Task<FollowUpTaskDto?> CompleteAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default)
        => TransitionAsync(taskId, FollowUpTaskStatus.Done, cancellationToken);

    public Task<FollowUpTaskDto?> CancelAsync(Guid taskId, Guid actorUserId, CancellationToken cancellationToken = default)
        => TransitionAsync(taskId, FollowUpTaskStatus.Cancelled, cancellationToken);

    private async Task<FollowUpTaskDto?> TransitionAsync(Guid taskId, FollowUpTaskStatus status, CancellationToken cancellationToken)
    {
        using var db = _dbFactory.Create();
        var task = await db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (task.Status != status)
        {
            task.Status = status;
            task.CompletedAt = status == FollowUpTaskStatus.Done ? _timeProvider.GetUtcNow() : null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Map(task);
    }

    private static FollowUpTaskDto Map(FollowUpTask t) =>
        new(t.Id, t.LeadId, t.Title, t.Notes, t.DueAt, t.Status, t.AssignedToTenantUserId, t.CompletedAt);
}

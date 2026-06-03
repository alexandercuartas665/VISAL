using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class AutomationService : IAutomationService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IChatService _chat;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public AutomationService(IApplicationDbContext db, ITenantContext tenantContext, IChatService chat, IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _chat = chat;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AutomationRuleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Join opcional con AiAgent para incluir el nombre del agente en la tarjeta.
        var rows = await _db.AutomationRules.AsNoTracking()
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .GroupJoin(_db.AiAgents.AsNoTracking(),
                r => r.AiAgentId, a => a.Id,
                (r, ags) => new { r, ags })
            .SelectMany(x => x.ags.DefaultIfEmpty(),
                (x, a) => new AutomationRuleDto(
                    x.r.Id, x.r.Name, x.r.Trigger, x.r.ThresholdMinutes, x.r.StageId,
                    x.r.TimeWindowStart, x.r.TimeWindowEnd, x.r.Action,
                    x.r.FollowUpTitle, x.r.TemplateCategory, x.r.ShiftName,
                    x.r.AiAgentId, a == null ? null : a.Name,
                    x.r.RevisarAlGuardarParcial, x.r.RevisarAlGuardarDefinitivo,
                    x.r.IsActive, x.r.ExecutionCount, x.r.LastRunAt))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<AutomationRuleDto?> CreateAsync(SaveAutomationRuleRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var nextOrder = (await _db.AutomationRules.Select(r => (int?)r.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var rule = new AutomationRule { TenantId = tenantId, SortOrder = nextOrder, IsActive = false };
        Apply(rule, request);
        _db.AutomationRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(rule);
    }

    public async Task<AutomationRuleDto?> UpdateAsync(Guid id, SaveAutomationRuleRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null) { return null; }
        Apply(rule, request);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(rule);
    }

    public async Task<AutomationRuleDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null) { return null; }
        rule.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(rule);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null) { return false; }
        _db.AutomationRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AutomationRunResult> RunNowAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new AutomationRunResult(0, 0); }

        var rules = await _db.AutomationRules
            .Where(r => r.IsActive && r.Trigger == AutomationTrigger.NoReply && r.Action == AutomationAction.CreateFollowUp)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0) { return new AutomationRunResult(0, 0); }

        var now = _timeProvider.GetUtcNow();
        var unanswered = await _chat.GetUnansweredByPhoneAsync(cancellationToken);

        var created = 0;
        if (unanswered.Count > 0)
        {
            var leads = await _db.Leads
                .Where(l => l.Status == LeadStatus.Open && l.ContactPhone != null)
                .Select(l => new { l.Id, l.ContactPhone, l.AssignedToTenantUserId })
                .ToListAsync(cancellationToken);

            foreach (var lead in leads)
            {
                var key = new string((lead.ContactPhone ?? "").Where(char.IsDigit).ToArray());
                if (!unanswered.TryGetValue(key, out var st)) { continue; }
                var minutesWaiting = (now - st.WaitingSince).TotalMinutes;
                var rule = rules.Where(r => minutesWaiting >= r.ThresholdMinutes).OrderBy(r => r.ThresholdMinutes).FirstOrDefault();
                if (rule is null) { continue; }

                var hasPending = await _db.FollowUpTasks.AnyAsync(t => t.LeadId == lead.Id && t.Status == FollowUpTaskStatus.Pending, cancellationToken);
                if (hasPending) { continue; }

                _db.FollowUpTasks.Add(new FollowUpTask
                {
                    TenantId = tenantId,
                    LeadId = lead.Id,
                    Title = string.IsNullOrWhiteSpace(rule.FollowUpTitle) ? "Cliente sin respuesta - dar seguimiento" : rule.FollowUpTitle!,
                    Notes = $"Generado por automatizacion '{rule.Name}'. Espera {Math.Round(minutesWaiting)} min sin respuesta.",
                    DueAt = now,
                    Status = FollowUpTaskStatus.Pending,
                    AssignedToTenantUserId = lead.AssignedToTenantUserId
                });
                rule.ExecutionCount++;
                created++;
            }
        }

        foreach (var r in rules) { r.LastRunAt = now; }
        _audit.Write(actorUserId, "automation.run", nameof(AutomationRule), Guid.Empty,
            previousValue: null, newValue: new { rules = rules.Count, created }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new AutomationRunResult(rules.Count, created);
    }

    public async Task<int> SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return 0; }
        if (await _db.AutomationRules.AnyAsync(cancellationToken)) { return 0; }

        var seed = new List<AutomationRule>
        {
            new() { TenantId = tenantId, SortOrder = 0, IsActive = true, Name = "Bienvenida nuevo lead",
                Trigger = AutomationTrigger.IncomingNoLead, Action = AutomationAction.CreateLeadAndReply, TemplateCategory = "saludo" },
            new() { TenantId = tenantId, SortOrder = 1, IsActive = true, Name = "Alerta sin respuesta 30min",
                Trigger = AutomationTrigger.NoReply, ThresholdMinutes = 30, Action = AutomationAction.NotifySupervisor },
            new() { TenantId = tenantId, SortOrder = 2, IsActive = true, Name = "Reasignacion nocturna",
                Trigger = AutomationTrigger.ChatInTimeWindow, TimeWindowStart = "22:00", TimeWindowEnd = "06:00",
                Action = AutomationAction.AssignToShift, ShiftName = "turno noche" },
            new() { TenantId = tenantId, SortOrder = 3, IsActive = false, Name = "Cotizacion aprobada -> pago",
                Trigger = AutomationTrigger.StageEntered, Action = AutomationAction.GenerateWompiLink },
            new() { TenantId = tenantId, SortOrder = 4, IsActive = false, Name = "Revisar notas medicas con IA",
                Trigger = AutomationTrigger.LeadCreated, Action = AutomationAction.ReviewMedicalNotesWithAi }
        };
        _db.AutomationRules.AddRange(seed);
        await _db.SaveChangesAsync(cancellationToken);
        return seed.Count;
    }

    private static void Apply(AutomationRule rule, SaveAutomationRuleRequest r)
    {
        rule.Name = (r.Name ?? "Regla").Trim();
        rule.Trigger = r.Trigger;
        rule.ThresholdMinutes = r.ThresholdMinutes <= 0 ? 30 : r.ThresholdMinutes;
        rule.StageId = r.StageId;
        rule.TimeWindowStart = r.TimeWindowStart;
        rule.TimeWindowEnd = r.TimeWindowEnd;
        rule.Action = r.Action;
        rule.FollowUpTitle = r.FollowUpTitle?.Trim();
        rule.TemplateCategory = r.TemplateCategory?.Trim();
        rule.ShiftName = r.ShiftName?.Trim();
        // Solo conservamos AiAgentId cuando la accion realmente lo usa, asi evitamos
        // referencias huerfanas si el usuario cambia de accion sin limpiar el dropdown.
        rule.AiAgentId = r.Action == AutomationAction.ReviewMedicalNotesWithAi ? r.AiAgentId : null;
        // Los flags de auto-revision solo aplican a la accion IA: si la accion cambia,
        // los apagamos para evitar configuracion fantasma.
        var esIa = r.Action == AutomationAction.ReviewMedicalNotesWithAi;
        rule.RevisarAlGuardarParcial = esIa && r.RevisarAlGuardarParcial;
        rule.RevisarAlGuardarDefinitivo = esIa && r.RevisarAlGuardarDefinitivo;
    }

    private static AutomationRuleDto Map(AutomationRule r) =>
        new(r.Id, r.Name, r.Trigger, r.ThresholdMinutes, r.StageId, r.TimeWindowStart, r.TimeWindowEnd,
            r.Action, r.FollowUpTitle, r.TemplateCategory, r.ShiftName, r.AiAgentId, null,
            r.RevisarAlGuardarParcial, r.RevisarAlGuardarDefinitivo,
            r.IsActive, r.ExecutionCount, r.LastRunAt);
}

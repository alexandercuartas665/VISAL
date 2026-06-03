using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record AutomationRuleDto(
    Guid Id,
    string Name,
    AutomationTrigger Trigger,
    int ThresholdMinutes,
    Guid? StageId,
    string? TimeWindowStart,
    string? TimeWindowEnd,
    AutomationAction Action,
    string? FollowUpTitle,
    string? TemplateCategory,
    string? ShiftName,
    Guid? AiAgentId,
    string? AiAgentName,
    bool RevisarAlGuardarParcial,
    bool RevisarAlGuardarDefinitivo,
    bool IsActive,
    int ExecutionCount,
    DateTimeOffset? LastRunAt);

public sealed record SaveAutomationRuleRequest(
    string Name,
    AutomationTrigger Trigger,
    int ThresholdMinutes,
    Guid? StageId,
    string? TimeWindowStart,
    string? TimeWindowEnd,
    AutomationAction Action,
    string? FollowUpTitle,
    string? TemplateCategory,
    string? ShiftName,
    Guid? AiAgentId,
    bool RevisarAlGuardarParcial,
    bool RevisarAlGuardarDefinitivo);

public sealed record AutomationRunResult(int RulesEvaluated, int FollowUpsCreated);

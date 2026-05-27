using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record AiAgentDto(
    Guid Id,
    string Name,
    string? Role,
    AiProvider Provider,
    string? Model,
    string SystemPrompt,
    bool IsActive,
    int SortOrder,
    int ResourceCount);

public sealed record AiAgentResourceDto(
    Guid Id,
    Guid AgentId,
    string Name,
    AgentResourceType ResourceType,
    string? Detail,
    string? FileUrl,
    string? FileName,
    int SortOrder);

public sealed record AiAgentPromptDto(Guid Id, Guid AgentId, string Name, string? Rule, string Body, int SortOrder);

public sealed record AiAgentDetailDto(AiAgentDto Agent, IReadOnlyList<AiAgentResourceDto> Resources, IReadOnlyList<AiAgentPromptDto> Prompts);

public sealed record CreateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt);
public sealed record UpdateAiAgentRequest(string Name, string? Role, AiProvider Provider, string? Model, string SystemPrompt);

public sealed record CreateAgentResourceRequest(Guid AgentId, string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName);
public sealed record UpdateAgentResourceRequest(string Name, AgentResourceType ResourceType, string? Detail, string? FileUrl, string? FileName);

public sealed record CreateAgentPromptRequest(Guid AgentId, string Name, string? Rule, string Body);
public sealed record UpdateAgentPromptRequest(string Name, string? Rule, string Body);

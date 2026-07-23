using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

// =========================================================================
// Tableros
// =========================================================================

public sealed record TaskBoardDto(
    Guid Id, string Name, string? Description, string? Color, int SortOrder, bool IsArchived,
    /// <summary>PlatformUserId del creador — dueno del tablero.</summary>
    Guid OwnerPlatformUserId,
    /// <summary>true si el usuario que consulta es el dueno (habilita invitar/borrar).</summary>
    bool IsOwner,
    int CardCount, int CompletedCount, int MemberCount);

public sealed record CreateTaskBoardRequest(string Name, string? Description, string? Color);
public sealed record UpdateTaskBoardRequest(string Name, string? Description, string? Color, bool IsArchived);

// =========================================================================
// Miembros del tablero (owner + invitados)
// =========================================================================

public sealed record TaskBoardMemberDto(
    Guid Id,
    Guid PlatformUserId,
    string DisplayName,
    string Initials,
    /// <summary>true si es el dueno del tablero (no se puede quitar).</summary>
    bool IsOwner,
    DateTimeOffset InvitedAt);

/// <summary>Candidato del picker para invitar — PlatformUsers del tenant que
/// no son ya miembros del tablero.</summary>
public sealed record TaskBoardInviteCandidateDto(
    Guid PlatformUserId,
    string DisplayName,
    string? Email,
    string Initials);

public sealed record InviteMemberRequest(Guid BoardId, Guid PlatformUserId);

// =========================================================================
// Columnas
// =========================================================================

public sealed record TaskBoardColumnDto(
    Guid Id, Guid BoardId, string Name, string? Color, int SortOrder, bool IsDone, int CardCount);

public sealed record CreateTaskColumnRequest(Guid BoardId, string Name, string? Color, bool IsDone);
public sealed record UpdateTaskColumnRequest(string Name, string? Color, bool IsDone);

// =========================================================================
// Tarjetas
// =========================================================================

public sealed record TaskCardMemberDto(Guid PlatformUserId, string Initials, string DisplayName);
public sealed record TaskCardTagDto(Guid Id, string Name, string? Color);
public sealed record TaskCardChecklistItemDto(Guid Id, string Text, bool IsCompleted, int SortOrder);
public sealed record TaskCardActivityDto(Guid Id, TaskActivityType Type, string ActorName, string Text, DateTimeOffset CreatedAt);
public sealed record TaskCardAttachmentDto(
    Guid Id, string FileName, string Url, string? MimeType, long SizeBytes,
    string? UploadedByName, DateTimeOffset CreatedAt);

public sealed record TaskCardSummaryDto(
    Guid Id, Guid BoardId, Guid ColumnId, string Title, string? Description,
    DateTimeOffset? DueAt, int SortOrder, bool IsArchived, string? Color,
    IReadOnlyList<TaskCardMemberDto> Members,
    IReadOnlyList<TaskCardTagDto> Tags,
    int ChecklistTotal, int ChecklistDone,
    int CommentsCount, int AttachmentsCount);

public sealed record TaskCardDetailDto(
    TaskCardSummaryDto Card,
    IReadOnlyList<TaskCardChecklistItemDto> Checklist,
    IReadOnlyList<TaskCardActivityDto> Activity,
    IReadOnlyList<TaskCardAttachmentDto> Attachments);

public sealed record CreateTaskCardRequest(
    Guid BoardId, Guid ColumnId, string Title, string? Description,
    string? Color = null, DateTimeOffset? DueAt = null);

public sealed record UpdateTaskCardRequest(string Title, string? Description, DateTimeOffset? DueAt, string? Color = null);

/// <summary>Movimiento de la tarjeta a otra columna y/o posicion vertical.</summary>
public sealed record MoveTaskCardRequest(Guid ColumnId, int SortOrder);

// =========================================================================
// Catalogo de etiquetas por tablero
// =========================================================================

public sealed record CreateBoardTagRequest(Guid BoardId, string Name, string? Color);
public sealed record UpdateBoardTagRequest(string Name, string? Color);

// =========================================================================
// Comentarios y checklist
// =========================================================================

public sealed record AddCommentRequest(Guid TaskCardId, string Text);
public sealed record AddChecklistItemRequest(Guid TaskCardId, string Text);
public sealed record UpdateChecklistItemRequest(string Text, bool IsCompleted);

// =========================================================================
// Adjuntos
// =========================================================================

public sealed record AddAttachmentRequest(
    Guid TaskCardId, string FileName, string Url, string? MimeType, long SizeBytes);

// =========================================================================
// Asignaciones de miembros a tarjetas
// =========================================================================

/// <summary>Asigna un miembro del tablero a una tarjeta. El PlatformUserId
/// debe ser miembro del tablero — de lo contrario el service falla.</summary>
public sealed record AssignMemberRequest(Guid TaskCardId, Guid PlatformUserId);

namespace Visal.Application.Tenancy;

/// <summary>
/// Gestion del modulo Tableros (Kanban colaborativo).
///
/// REGLA DE ACCESO (aplicada en TODAS las operaciones):
/// - Un usuario ve un tablero solo si es el <c>OwnerPlatformUserId</c> del
///   tablero O tiene una fila en <c>TaskBoardMember</c> para ese tablero.
/// - Sin membresia, cualquier operacion devuelve <c>null</c> / <c>false</c>
///   / lista vacia. NO se distingue entre "no existe" y "no tenes acceso"
///   para no filtrar existencia de tableros ajenos.
/// - Los <c>actorPlatformUserId</c> parametros son obligatorios en todos
///   los metodos que muten o lean por id — el service NO los infiere.
///
/// Las operaciones que solo el owner puede realizar (invitar, borrar tablero,
/// remover miembros) validan ademas <c>IsOwner</c>. Este metodo tambien
/// esta expuesto por si la UI quiere ocultar botones.
/// </summary>
public interface ITaskBoardService
{
    // ---- Tableros ----
    Task<IReadOnlyList<TaskBoardDto>> ListMyBoardsAsync(Guid actorPlatformUserId, bool includeArchived = false, CancellationToken ct = default);
    Task<TaskBoardDto?> GetBoardAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> DeleteBoardAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Columnas ----
    Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> DeleteColumnAsync(Guid columnId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Etiquetas del tablero ----
    Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Tarjetas ----
    Task<IReadOnlyList<TaskCardSummaryDto>> ListCardsAsync(Guid boardId, Guid actorPlatformUserId, bool includeArchived = false, CancellationToken ct = default);
    Task<TaskCardDetailDto?> GetCardAsync(Guid cardId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskCardSummaryDto?> CreateCardAsync(CreateTaskCardRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<TaskCardSummaryDto?> UpdateCardAsync(Guid cardId, UpdateTaskCardRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> MoveCardAsync(Guid cardId, MoveTaskCardRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> ArchiveCardAsync(Guid cardId, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> DeleteCardAsync(Guid cardId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Miembros asignados a la tarjeta ----
    Task<TaskCardMemberDto?> AssignMemberToCardAsync(AssignMemberRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> UnassignMemberFromCardAsync(Guid cardId, Guid platformUserId, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);

    // ---- Etiquetas asignadas a la tarjeta ----
    Task<bool> AttachTagToCardAsync(Guid cardId, Guid tagId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> DetachTagFromCardAsync(Guid cardId, Guid tagId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Checklist ----
    Task<TaskCardChecklistItemDto?> AddChecklistItemAsync(AddChecklistItemRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> UpdateChecklistItemAsync(Guid itemId, UpdateChecklistItemRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> DeleteChecklistItemAsync(Guid itemId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Comentarios y actividad ----
    Task<TaskCardActivityDto?> AddCommentAsync(AddCommentRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);

    // ---- Adjuntos ----
    Task<TaskCardAttachmentDto?> AddAttachmentAsync(AddAttachmentRequest request, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    /// <summary>Sube el archivo al almacenamiento local (wwwroot/uploads/tableros) y crea el TaskCardAttachment. Retorna el DTO o null.</summary>
    Task<TaskCardAttachmentDto?> UploadAttachmentAsync(Guid cardId, string fileName, string? mimeType, byte[] content, Guid actorPlatformUserId, string actorDisplayName, CancellationToken ct = default);
    Task<bool> DeleteAttachmentAsync(Guid attachmentId, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Permisos ----
    /// <summary>true si el usuario tiene acceso al tablero (owner o miembro).</summary>
    Task<bool> HasAccessAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
    /// <summary>true si el usuario es el dueno del tablero.</summary>
    Task<bool> IsOwnerAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
}

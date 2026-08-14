using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>DTO de una definicion de campo dinamico de un tablero.</summary>
public sealed record TaskFieldDto(
    Guid Id,
    Guid BoardId,
    string FieldKey,
    string Label,
    TaskFieldType FieldType,
    bool ShowInFilter,
    int Column,
    int SortOrder,
    string? Options,
    string? Description,
    bool AllowMultiple,
    bool MultiWithDetail,
    string? TotalSourceKeys,
    string? RepeatWithFieldKey);

public sealed record CreateTaskFieldRequest(
    Guid BoardId,
    string Label,
    TaskFieldType FieldType = TaskFieldType.Text,
    int Column = 1,
    string? Options = null,
    string? Description = null,
    bool ShowInFilter = false,
    bool AllowMultiple = false,
    bool MultiWithDetail = false,
    string? TotalSourceKeys = null,
    string? RepeatWithFieldKey = null);

public sealed record UpdateTaskFieldRequest(
    string Label,
    TaskFieldType FieldType,
    int Column,
    string? Options,
    string? Description,
    bool ShowInFilter,
    bool AllowMultiple,
    bool MultiWithDetail,
    string? TotalSourceKeys,
    string? RepeatWithFieldKey);

/// <summary>
/// Gestion de los campos dinamicos de un tablero (portado del pipeline de CUBOT.travels)
/// y de los valores por tarjeta. Aplica el mismo gate de acceso del tablero (owner o
/// miembro): sin acceso, toda operacion devuelve null/false/vacio.
/// </summary>
public interface ITaskFieldService
{
    // ---- Definiciones de campo (por tablero) ----
    Task<IReadOnlyList<TaskFieldDto>> ListFieldsAsync(Guid boardId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskFieldDto?> CreateFieldAsync(CreateTaskFieldRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<TaskFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTaskFieldRequest request, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorPlatformUserId, CancellationToken ct = default);
    Task<bool> ReorderFieldsAsync(Guid boardId, IReadOnlyList<Guid> orderedFieldIds, Guid actorPlatformUserId, CancellationToken ct = default);

    // ---- Valores por tarjeta ----
    /// <summary>Devuelve los valores dinamicos de la tarjeta (FieldKey -> valor). Vacio si no hay o sin acceso.</summary>
    Task<Dictionary<string, string?>> GetCardValuesAsync(Guid cardId, Guid actorPlatformUserId, CancellationToken ct = default);
    /// <summary>Guarda los valores dinamicos de la tarjeta. Ignora claves vacias. Devuelve false si sin acceso.</summary>
    Task<bool> SaveCardValuesAsync(Guid cardId, IReadOnlyDictionary<string, string?> values, Guid actorPlatformUserId, CancellationToken ct = default);
}

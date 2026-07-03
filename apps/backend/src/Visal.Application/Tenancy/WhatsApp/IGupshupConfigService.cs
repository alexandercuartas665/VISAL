namespace Visal.Application.Tenancy.WhatsApp;

/// <summary>
/// DTO plano de una App Gupshup registrada por el tenant. Nunca expone la
/// apikey en claro (solo mascarada) para que la UI la pueda mostrar sin
/// riesgo de filtracion via html/JS.
/// </summary>
public sealed record GupshupConfigDto(
    Guid Id,
    Guid AppId,
    string AppName,
    string? WabaId,
    string? PhoneNumber,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset? LastValidatedAt,
    /// <summary>Ej: "sk_****4a1b" o "(sin apikey)".</summary>
    string ApiKeyMasked,
    bool HasPartnerToken);

/// <summary>
/// Payload para dar de alta o editar una App Gupshup. La apikey se ingresa
/// en claro; el servicio la cifra con Data Protection y persiste el
/// blob. Si el llamador manda vacio, se conserva el valor previo (util
/// para editar sin re-tipearla).
/// </summary>
public sealed record SaveGupshupConfigRequest(
    Guid AppId,
    string AppName,
    string? WabaId,
    string? PhoneNumber,
    string? DisplayName,
    /// <summary>Vacio = mantener el valor actual.</summary>
    string? ApiKey,
    string? PartnerToken,
    bool IsActive = true);

/// <summary>Gestor CRUD tenant-scoped de Apps Gupshup. La UI de /lineas la
/// usa para el modal de credenciales cuando se agrega una linea Gupshup.</summary>
public interface IGupshupConfigService
{
    Task<IReadOnlyList<GupshupConfigDto>> ListAsync(CancellationToken ct = default);
    Task<GupshupConfigDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea si Id vacio, sino edita. Devuelve null si no hay
    /// tenant activo.</summary>
    Task<GupshupConfigDto?> UpsertAsync(Guid? id, SaveGupshupConfigRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Rota el InboundToken de una linea (regenera y persiste).
    /// Devuelve el token nuevo para que la UI actualice el URL. Null si
    /// la linea no existe o no es del tenant activo.</summary>
    Task<string?> RegenerateInboundTokenAsync(Guid lineId, Guid actorUserId, CancellationToken ct = default);
}

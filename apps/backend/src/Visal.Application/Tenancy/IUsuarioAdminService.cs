namespace Visal.Application.Tenancy;

public sealed record UsuarioDto(
    Guid Id, Guid PlatformUserId, string Email, string? DisplayName,
    Guid? RolId, string? RolNombre,
    IReadOnlyList<Guid> SucursalIds, IReadOnlyList<string> SucursalNombres,
    string Estado, bool EsGlobal);

public sealed record CrearUsuarioRequest(
    string Email, string? DisplayName, string Password, Guid? RolId,
    IReadOnlyList<Guid> SucursalIds, bool EsGlobal);

public interface IUsuarioAdminService
{
    Task<IReadOnlyList<UsuarioDto>> ListAsync(CancellationToken ct = default);
    Task<UsuarioDto?> CrearAsync(CrearUsuarioRequest req, Guid actor, CancellationToken ct = default);
    Task<UsuarioDto?> AsignarAsync(Guid tenantUserId, Guid? rolId, IReadOnlyList<Guid> sucursalIds, bool esGlobal, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid tenantUserId, Guid actor, CancellationToken ct = default);

    /// <summary>Reinicia la clave del PlatformUser asociado al TenantUser. Devuelve true si tuvo exito.</summary>
    Task<bool> ResetPasswordAsync(Guid tenantUserId, string nuevaClave, Guid actor, CancellationToken ct = default);
}

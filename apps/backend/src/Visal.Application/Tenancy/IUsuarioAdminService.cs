namespace Visal.Application.Tenancy;

public sealed record UsuarioDto(
    Guid Id, Guid PlatformUserId, string Email, string? DisplayName,
    Guid? RolId, string? RolNombre, Guid? SucursalId, string? SucursalNombre,
    string Estado, bool EsGlobal);

public sealed record CrearUsuarioRequest(
    string Email, string? DisplayName, string Password, Guid? RolId, Guid? SucursalId, bool EsGlobal);

public interface IUsuarioAdminService
{
    Task<IReadOnlyList<UsuarioDto>> ListAsync(CancellationToken ct = default);
    Task<UsuarioDto?> CrearAsync(CrearUsuarioRequest req, Guid actor, CancellationToken ct = default);
    Task<UsuarioDto?> AsignarAsync(Guid tenantUserId, Guid? rolId, Guid? sucursalId, bool esGlobal, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid tenantUserId, Guid actor, CancellationToken ct = default);
}

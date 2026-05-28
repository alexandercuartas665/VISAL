namespace Visal.Application.Tenancy;

public sealed record SucursalDto(Guid Id, string Codigo, string Nombre, string? Direccion, string? Ciudad, string? Telefono, bool Activo);

public sealed record SaveSucursalRequest(Guid? Id, string Codigo, string Nombre, string? Direccion, string? Ciudad, string? Telefono, bool Activo);

public interface ISucursalService
{
    Task<IReadOnlyList<SucursalDto>> ListAsync(bool soloActivas = false, CancellationToken ct = default);
    Task<SucursalDto?> SaveAsync(SaveSucursalRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
}

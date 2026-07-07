namespace Visal.Application.Tenancy;

public sealed record SucursalDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    bool Activo,
    bool MipresObligatorio);

public sealed record SaveSucursalRequest(
    Guid? Id,
    string Codigo,
    string Nombre,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    bool Activo,
    bool MipresObligatorio);

public interface ISucursalService
{
    Task<IReadOnlyList<SucursalDto>> ListAsync(bool soloActivas = false, CancellationToken ct = default);
    Task<SucursalDto?> SaveAsync(SaveSucursalRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Lee solo el flag MipresObligatorio de una sede. Devuelve false si la sede
    /// no existe o el flag esta apagado. La HC lo consulta para decidir si el codigo
    /// MIPRES es obligatorio al agregar medicamentos o insumos.</summary>
    Task<bool> GetMipresObligatorioAsync(Guid sucursalId, CancellationToken ct = default);
}

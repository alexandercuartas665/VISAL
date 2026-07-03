namespace Visal.Application.Tenancy;

public sealed record PaqueteDto(Guid Id, string Codigo, string Nombre, bool Activo);

public sealed record SavePaqueteRequest(Guid? Id, string Codigo, string Nombre, bool Activo);

/// <summary>Catalogo de paquetes comerciales. Tenant-scoped. Se usa para agrupar
/// servicios de un contrato de aseguradora bajo un paquete opcional.</summary>
public interface IPaqueteService
{
    Task<IReadOnlyList<PaqueteDto>> ListarAsync(string? filtro = null, bool soloActivos = false, CancellationToken ct = default);
    Task<PaqueteDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PaqueteDto?> SaveAsync(SavePaqueteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Inserta paquetes en bloque (usado por el seed desde el Excel NUEVO PAQUETE
    /// DOMICILIARIO). Idempotente por codigo: si ya existe se ignora. Devuelve la cantidad insertada.</summary>
    Task<int> SembrarAsync(IReadOnlyList<SavePaqueteRequest> items, Guid actor, CancellationToken ct = default);
}

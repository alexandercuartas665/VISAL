namespace Visal.Application.Tenancy;

public sealed record CuotaCopagoDto(Guid Id, string Tipo, string Categoria, decimal ValorSugerido, string? Descripcion, bool Activo);

public sealed record SaveCuotaCopagoRequest(Guid? Id, string Tipo, string Categoria, decimal ValorSugerido, string? Descripcion, bool Activo);

/// <summary>Catalogo de valores sugeridos para Cuota Moderadora y Copago. Tenant-scoped.</summary>
public interface ICuotaCopagoService
{
    Task<IReadOnlyList<CuotaCopagoDto>> ListarAsync(string? tipo = null, bool soloActivos = false, CancellationToken ct = default);
    Task<CuotaCopagoDto?> SaveAsync(SaveCuotaCopagoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Sembra los rangos SMMLV estandar si el tenant no tiene ninguno cargado.
    /// Devuelve cantidad insertada. Idempotente.</summary>
    Task<int> SeedDefaultsAsync(Guid actor, CancellationToken ct = default);
}

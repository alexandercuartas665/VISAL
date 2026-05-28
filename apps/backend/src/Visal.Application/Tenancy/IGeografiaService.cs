namespace Visal.Application.Tenancy;

public sealed record PaisDto(Guid Id, string Codigo, string Nombre);
public sealed record DepartamentoDto(Guid Id, Guid PaisId, string Nombre);
public sealed record MunicipioDto(Guid Id, Guid DepartamentoId, string Nombre);

/// <summary>
/// Catalogo global (no tenant-scoped) de paises, departamentos y municipios.
/// Alimenta los selects en cascada del formulario de pacientes.
/// </summary>
public interface IGeografiaService
{
    Task<IReadOnlyList<PaisDto>> ListPaisesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DepartamentoDto>> ListDepartamentosAsync(Guid paisId, CancellationToken ct = default);
    Task<IReadOnlyList<MunicipioDto>> ListMunicipiosAsync(Guid departamentoId, CancellationToken ct = default);
}

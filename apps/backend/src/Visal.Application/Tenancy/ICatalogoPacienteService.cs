using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record CatalogoPacienteDto(Guid Id, CatalogoPacienteTipo Tipo, string Codigo, string Nombre, string? Descripcion, bool Activo);

public sealed record SaveCatalogoPacienteRequest(Guid? Id, CatalogoPacienteTipo Tipo, string Codigo, string Nombre, string? Descripcion, bool Activo);

/// <summary>
/// CRUD para los catalogos del modulo Configuracion Pacientes. Una sola tabla con
/// discriminador Tipo: tipos de usuario, clasificacion paciente, clasificacion grupo
/// patologia, tipos de tutela y contratos.
/// </summary>
public interface ICatalogoPacienteService
{
    Task<IReadOnlyList<CatalogoPacienteDto>> ListAsync(CatalogoPacienteTipo? tipo, CancellationToken ct = default);
    Task<CatalogoPacienteDto?> SaveAsync(SaveCatalogoPacienteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
}

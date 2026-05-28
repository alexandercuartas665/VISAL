namespace Visal.Application.Tenancy;

public sealed record PacienteDto(Guid Id, string NumeroDocumento, string NombreCompleto, string? Ciudad, string? Telefono, string? Aseguradora);

public sealed record PacienteDetailDto(
    Guid Id, string NumeroDocumento, string TipoDocumento, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido, string NombreCompleto, DateOnly? FechaNacimiento,
    string? Sexo, string? EstadoCivil, string? Telefono, string? Email, string? Direccion, string? Ciudad,
    string? Zona, string? Ocupacion, string? Regimen, Guid? AseguradoraId,
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia, bool Activo);

public sealed record SavePacienteRequest(
    Guid? Id, string NumeroDocumento, string TipoDocumento, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido, string? NombreCompleto, DateOnly? FechaNacimiento,
    string? Sexo, string? EstadoCivil, string? Telefono, string? Email, string? Direccion, string? Ciudad,
    string? Zona, string? Ocupacion, string? Regimen, Guid? AseguradoraId,
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia, bool Activo);

public interface IPacienteService
{
    Task<IReadOnlyList<PacienteDto>> ListAsync(string? filtro, CancellationToken ct = default);
    Task<PacienteDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PacienteDetailDto?> SaveAsync(SavePacienteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
}

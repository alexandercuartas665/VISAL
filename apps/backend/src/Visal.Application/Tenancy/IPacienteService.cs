namespace Visal.Application.Tenancy;

public sealed record PacienteDto(
    Guid Id, string NumeroDocumento, string NombreCompleto, string? Ciudad, string? Telefono,
    string? Aseguradora, string? Sede, string? Estado, DateOnly? FechaIngresoPad);

public sealed record PacienteDetailDto(
    // Identificacion
    Guid Id, string NumeroDocumento, string TipoDocumento,
    string? PrimerNombre, string? SegundoNombre, string? PrimerApellido, string? SegundoApellido,
    string NombreCompleto, DateOnly? FechaNacimiento, int? Edad,
    // Admin PAD
    Guid? IpsComentaId, string? CodigoAceptacion, DateOnly? FechaComentan,
    Guid? AseguradoraId, DateOnly? FechaIngresoPad, DateOnly? FechaEgresoPad,
    int? DiasEstancia, int? OpIngresoDias,
    // Clasificaciones (FKs a catalogos_paciente)
    string? Incapacidad, string? GrupoRh, Guid? TipoUsuarioId, string? Estado,
    Guid? ClasificacionPacienteId, Guid? ClasificacionGrupoPatologiaId,
    string? EstratoSocial, string? Sexo, string? EstadoCivil, string? Zona,
    string? Ocupacion, string? Regimen,
    // Contratos
    Guid? Contrato1Id, Guid? Contrato2Id, Guid? Contrato3Id,
    // Diagnostico
    Guid? Cie10Id, string? Cie10Codigo, string? DiagnosticoPrincipal,
    // Tutela
    string? Tutela, Guid? TipoTutelaId, Guid? MedContratadoId,
    // Geografia
    Guid? PaisResidenciaId, Guid? PaisOrigenId, Guid? DepartamentoId, Guid? MunicipioId,
    string? Direccion, string? Barrio, string? Ciudad,
    // Contacto
    string? Telefono, string? Email,
    // Sede
    Guid? SedeAtencionId,
    // Emergencia
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia,
    bool Activo);

public sealed record SavePacienteRequest(
    Guid? Id,
    // Identificacion
    string NumeroDocumento, string TipoDocumento,
    string? PrimerNombre, string? SegundoNombre, string? PrimerApellido, string? SegundoApellido,
    string? NombreCompleto, DateOnly? FechaNacimiento, int? Edad,
    // Admin PAD
    Guid? IpsComentaId, string? CodigoAceptacion, DateOnly? FechaComentan,
    Guid? AseguradoraId, DateOnly? FechaIngresoPad, DateOnly? FechaEgresoPad,
    int? DiasEstancia, int? OpIngresoDias,
    // Clasificaciones (FKs)
    string? Incapacidad, string? GrupoRh, Guid? TipoUsuarioId, string? Estado,
    Guid? ClasificacionPacienteId, Guid? ClasificacionGrupoPatologiaId,
    string? EstratoSocial, string? Sexo, string? EstadoCivil, string? Zona,
    string? Ocupacion, string? Regimen,
    // Contratos
    Guid? Contrato1Id, Guid? Contrato2Id, Guid? Contrato3Id,
    // Diagnostico
    Guid? Cie10Id, string? Cie10Codigo, string? DiagnosticoPrincipal,
    // Tutela
    string? Tutela, Guid? TipoTutelaId, Guid? MedContratadoId,
    // Geografia
    Guid? PaisResidenciaId, Guid? PaisOrigenId, Guid? DepartamentoId, Guid? MunicipioId,
    string? Direccion, string? Barrio, string? Ciudad,
    // Contacto
    string? Telefono, string? Email,
    // Sede
    Guid? SedeAtencionId,
    // Emergencia
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia,
    bool Activo);

public interface IPacienteService
{
    Task<IReadOnlyList<PacienteDto>> ListAsync(string? filtro, CancellationToken ct = default);
    Task<PacienteDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PacienteDetailDto?> SaveAsync(SavePacienteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
}

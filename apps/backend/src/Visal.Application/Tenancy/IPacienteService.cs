namespace Visal.Application.Tenancy;

/// <summary>Un contacto de emergencia del paciente. El paciente puede tener varios.</summary>
public sealed record PacienteContactoEmergenciaDto(
    Guid? Id, string Nombre, string? Parentesco, string CodigoPais, string? Telefono, int Orden,
    // Firma del acompañante — data URL (image/png base64) capturada con canvas.
    // Opcional; null si el contacto no firmo.
    string? FirmaUrl);

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
    string? CodigoPaisTelefono, string? Telefono, string? Email,
    // Sede
    Guid? SedeAtencionId,
    // Emergencia legacy (primer contacto, mantenido por compat)
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia,
    // Lista completa de contactos de emergencia (puede tener 0..N)
    IReadOnlyList<PacienteContactoEmergenciaDto> ContactosEmergencia,
    bool Activo,
    // Estado de admision: "Abierto" (default) o "Cerrado". Un paciente Cerrado
    // pasa validacion de campos obligatorios y ya se puede usar en /asignacion.
    string EstadoAdmision = "Abierto");

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
    string? CodigoPaisTelefono, string? Telefono, string? Email,
    // Sede
    Guid? SedeAtencionId,
    // Emergencia legacy (primer contacto)
    string? ContactoEmergencia, string? Parentesco, string? TelefonoEmergencia,
    // Lista completa de contactos de emergencia (la UI envia 0..N)
    IReadOnlyList<PacienteContactoEmergenciaDto> ContactosEmergencia,
    bool Activo);

public interface IPacienteService
{
    Task<IReadOnlyList<PacienteDto>> ListAsync(string? filtro, CancellationToken ct = default);
    Task<PacienteDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PacienteDetailDto?> SaveAsync(SavePacienteRequest req, Guid actor, CancellationToken ct = default);
    /// <summary>Hard delete. Lanza InvalidOperationException con mensaje legible
    /// si el paciente tiene HC, notas o asignaciones (FK con ON DELETE RESTRICT).
    /// Para esos casos, usar DesactivarAsync.</summary>
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Soft delete: marca Activo=false sin borrar. Util cuando el
    /// paciente tiene datos clinicos que impiden el hard delete.</summary>
    Task<bool> DesactivarAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Actualiza solo el telefono del paciente (rapido desde el chat WhatsApp).
    /// Devuelve null si el paciente no existe, o el telefono ya normalizado a digitos.</summary>
    Task<string?> UpdateTelefonoAsync(Guid pacienteId, string telefono, Guid actor, CancellationToken ct = default);

    /// <summary>Lista los contactos de emergencia de un paciente, ordenados. Sirve
    /// para el modal "Solicitar firmas" del WhatsAppChatPanel sin tener que traer
    /// el PacienteDetailDto completo.</summary>
    Task<IReadOnlyList<PacienteContactoEmergenciaDto>> ListContactosEmergenciaAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Agrega un contacto de emergencia al paciente (o actualiza si viene
    /// con Id). Devuelve el DTO persistido con Id asignado. Usado desde el modal
    /// "Solicitar firmas" cuando el operador crea un pariente nuevo sobre la marcha.</summary>
    Task<PacienteContactoEmergenciaDto?> UpsertContactoEmergenciaAsync(Guid pacienteId, PacienteContactoEmergenciaDto contacto, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Valida los campos obligatorios y cambia el estado del paciente a Cerrado.
    /// Devuelve la lista de campos faltantes si la validacion falla — en ese
    /// caso el estado NO cambia. Los unicos campos NO obligatorios son
    /// DiasEstancia, OpIngresoDias, GrupoRh y Email; el resto de los datos
    /// basicos (identificacion, fechas, aseguradora, diagnostico, contacto y
    /// contactos de emergencia) deben estar diligenciados.
    /// </summary>
    Task<CerrarPacienteResult> CerrarAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Reabre un paciente Cerrado (vuelve a Abierto) permitiendo editarlo y
    /// eliminarlo. El caller debe validar el permiso administrativo del actor
    /// antes de invocar este metodo (mismo permiso "historias.reabrir" usado
    /// para reabrir HC).
    /// </summary>
    Task<bool> ReabrirAsync(Guid id, Guid actor, CancellationToken ct = default);
}

/// <summary>
/// Resultado de intentar cerrar un paciente. Si Ok=true el cierre se aplico;
/// si Ok=false, CamposFaltantes trae los campos que falta llenar (nombres
/// legibles para mostrar al usuario).
/// </summary>
public sealed record CerrarPacienteResult(bool Ok, IReadOnlyList<string> CamposFaltantes);

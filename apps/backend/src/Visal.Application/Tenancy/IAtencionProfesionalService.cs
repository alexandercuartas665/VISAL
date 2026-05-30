namespace Visal.Application.Tenancy;

/// <summary>
/// Fila del grid "Mis Servicios Asignados" del modulo de Atencion (Profesional).
/// Una fila por SESSION (no por turno): si el turno tiene Cantidad=3, se devuelven
/// 3 filas con SessionNo 1, 2 y 3.
/// </summary>
public sealed record MiServicioAsignadoDto(
    Guid AsignacionTurnoId, Guid AsignacionId,
    int SessionNo, int CantidadTotal,
    string TipoServicio, string NombreServicio, string CodigoAsignacionInterna, string CodigoAutorizacion,
    DateOnly FechaAsignacion, int Orden,
    string TipoDocPaciente, string NumeroDocPaciente, string NombrePaciente, Guid PacienteId,
    bool Completado, DateOnly? FechaAtencion);

/// <summary>Resultado del intento de registrar una nota / atender una sesion.</summary>
public sealed record RegistrarSesionResult(
    bool Ok, string? Mensaje, bool RequiereHistoriaClinica, bool RequiereSesionPrevia);

public interface IAtencionProfesionalService
{
    /// <summary>
    /// Servicios coordinados que el profesional logueado debe atender. Cada turno se
    /// expande en N filas segun su Cantidad (una fila por sesion). Marca cada session
    /// como Completado segun los registros en AsignacionTurnoSesion.
    /// </summary>
    Task<IReadOnlyList<MiServicioAsignadoDto>> GetMisServiciosAsync(Guid platformUserId, bool incluirCompletados = true, CancellationToken ct = default);

    /// <summary>
    /// Registra la atencion de una sesion (boton "Notas"). Valida:
    /// (a) que la sesion previa este completada (no permite saltarse),
    /// (b) que el paciente tenga historia clinica vigente segun la config del tenant
    ///     (proxy actual: al menos una sesion previa atendida en los ultimos N meses).
    /// </summary>
    Task<RegistrarSesionResult> RegistrarSesionAsync(Guid asignacionTurnoId, int sessionNo, string? nota, Guid actor, CancellationToken ct = default);
}

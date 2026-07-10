namespace Visal.Application.Tenancy;

/// <summary>
/// Fila del grid "Mis Servicios Asignados" del modulo de Atencion (Profesional).
/// Una fila por SESSION (no por turno): si el turno tiene Cantidad=3, se devuelven
/// 3 filas con SessionNo 1, 2 y 3.
/// </summary>
public sealed record MiServicioAsignadoDto(
    Guid AsignacionTurnoId, Guid AsignacionId,
    // SessionNo interno del turno (siempre 1..Cantidad). Se usa como clave para
    // guardar la sesion en AsignacionTurnoSesion — NO cambiar. Con el diseno
    // actual (task #147) cada turno tiene Cantidad=1 asi que SessionNo=1 siempre.
    int SessionNo, int CantidadTotal,
    string TipoServicio, string NombreServicio, string CodigoAsignacionInterna, string CodigoAutorizacion,
    DateOnly FechaAsignacion, int Orden,
    string TipoDocPaciente, string NumeroDocPaciente, string NombrePaciente, Guid PacienteId,
    bool Completado, DateOnly? FechaAtencion,
    // Codigo del formato de historia (FormDefinition.Codigo) que la aseguradora
    // configuro para este servicio - viaja desde ServicioContrato.Historia ->
    // Asignacion.FormatoHistoria. El profesional NO lo elige: lo usamos para
    // forzar el formato al iniciar la HC desde /atencion.
    string? FormatoHistoria = null,
    // Numero de sesion GLOBAL por asignacion (1..N). Solo para mostrar en la UI:
    // si una asignacion tiene 3 turnos, sus filas se ven como 1, 2, 3 aunque el
    // SessionNo interno de cada turno sea 1. Se calcula ordenando turnos por
    // CreatedAt asc y sumando Cantidad acumulada.
    int NumeroSesionMostrar = 1,
    int TotalSesionesAsignacion = 1,
    // Nombre del profesional asignado al turno. Vacio si el turno todavia no tiene
    // profesional asignado en Coordinacion.
    string NombreProfesional = "");

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

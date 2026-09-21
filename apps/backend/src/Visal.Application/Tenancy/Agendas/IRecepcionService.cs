namespace Visal.Application.Tenancy.Agendas;

/// <summary>Una cita del dia vista desde Recepcion (para marcar llegada / reprogramar).</summary>
public sealed record CitaRecepcionDto(
    Guid AsignacionTurnoId,
    Guid ProfesionalId,
    DateOnly Fecha,
    TimeOnly? Hora,
    string PacienteNombre,
    string PacienteDocumento,
    string DoctorNombre,
    string ServicioNombre,
    string Estado,
    bool Llego,
    DateTimeOffset? LlegoEn);

/// <summary>
/// Recepcion intramural: vista dia de las citas agendadas para marcar la llegada de
/// los pacientes (se refleja en verde en Atencion) y reprogramar citas a otro dia/hora.
/// </summary>
public interface IRecepcionService
{
    /// <summary>Citas agendadas (con hora) de un dia, opcionalmente de una sede, ordenadas por hora.</summary>
    Task<IReadOnlyList<CitaRecepcionDto>> ListarCitasDelDiaAsync(DateOnly fecha, Guid? sucursalId, CancellationToken ct = default);

    /// <summary>Marca (o desmarca) la llegada del paciente de una cita.</summary>
    Task<bool> MarcarLlegadaAsync(Guid asignacionTurnoId, bool llego, Guid actor, CancellationToken ct = default);

    /// <summary>Horas libres del doctor de la cita en una nueva fecha, para reprogramar.</summary>
    Task<IReadOnlyList<TimeOnly>> SlotsParaReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, CancellationToken ct = default);

    /// <summary>Reprograma la cita a una nueva fecha/hora (mismo doctor). Valida fecha futura,
    /// slot libre y que el paciente no tenga otra cita a esa hora. Limpia la llegada.</summary>
    Task ReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default);
}

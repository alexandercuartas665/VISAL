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

    /// <summary>Citas agendadas (con hora) en un rango de fechas [desde..hasta] (vista semana),
    /// opcionalmente de una sede, ordenadas por fecha y hora.</summary>
    Task<IReadOnlyList<CitaRecepcionDto>> ListarCitasRangoAsync(DateOnly desde, DateOnly hasta, Guid? sucursalId, CancellationToken ct = default);

    /// <summary>Marca (o desmarca) la llegada del paciente de una cita.</summary>
    Task<bool> MarcarLlegadaAsync(Guid asignacionTurnoId, bool llego, Guid actor, CancellationToken ct = default);

    /// <summary>Horas libres del doctor de la cita en una nueva fecha, para reprogramar.</summary>
    Task<IReadOnlyList<TimeOnly>> SlotsParaReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, CancellationToken ct = default);

    /// <summary>Reprograma la cita a una nueva fecha/hora (mismo doctor). Valida fecha futura,
    /// slot libre y que el paciente no tenga otra cita a esa hora. Limpia la llegada.</summary>
    Task ReprogramarAsync(Guid asignacionTurnoId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default);

    /// <summary>Reprograma la cita a una nueva fecha/hora, opcionalmente con OTRO doctor
    /// (nuevoProfesionalId; null = mismo doctor). Mismas validaciones que
    /// <see cref="ReprogramarAsync"/> pero el slot se valida para el doctor destino.</summary>
    Task ReprogramarConDoctorAsync(Guid asignacionTurnoId, Guid? nuevoProfesionalId, DateOnly nuevaFecha, TimeOnly nuevaHora, Guid actor, CancellationToken ct = default);

    /// <summary>Citas afectadas por una novedad de inasistencia: las de un doctor en una fecha,
    /// dentro de la franja [horaDesde..horaHasta] si se especifica (si no, todo el dia).
    /// Incluye telefonos y contactos del paciente para ubicarlo rapido.</summary>
    Task<IReadOnlyList<AfectadoRecepcionDto>> ListarAfectadosAsync(Guid profesionalId, DateOnly fecha, TimeOnly? horaDesde, TimeOnly? horaHasta, CancellationToken ct = default);

    /// <summary>Cancela (elimina) una cita. Passthrough al motor de agendas.</summary>
    Task<bool> CancelarCitaAsync(Guid asignacionTurnoId, Guid actor, CancellationToken ct = default);

    /// <summary>Horas libres de un doctor en una fecha (para el destino de la reprogramacion).</summary>
    Task<IReadOnlyList<TimeOnly>> SlotsDoctorAsync(Guid profesionalId, DateOnly fecha, CancellationToken ct = default);

    /// <summary>Doctores que tienen agenda configurada (para elegir doctor destino al reprogramar).</summary>
    Task<IReadOnlyList<DoctorSimpleDto>> ListarDoctoresConAgendaAsync(CancellationToken ct = default);

    /// <summary>Novedades recientes (ultimos 30 dias en adelante) con su conteo de citas
    /// aun afectadas, para retomar la reprogramacion tras recargar la pagina.</summary>
    Task<IReadOnlyList<NovedadResumenDto>> ListarNovedadesConAfectadosAsync(CancellationToken ct = default);
}

/// <summary>Resumen de una novedad registrada + cuantas citas siguen afectadas.</summary>
public sealed record NovedadResumenDto(
    Guid Id, Guid ProfesionalId, string DoctorNombre,
    DateOnly FechaDesde, DateOnly FechaHasta, TimeOnly? HoraDesde, TimeOnly? HoraHasta,
    string Tipo, int Afectados);

/// <summary>Doctor minimal para selects (id + nombre).</summary>
public sealed record DoctorSimpleDto(Guid Id, string Nombre);

/// <summary>Contacto de emergencia del paciente (para ubicarlo).</summary>
public sealed record ContactoAfectadoDto(string Nombre, string? Parentesco, string? Telefono);

/// <summary>Cita afectada por una novedad + datos de contacto del paciente (tarjeta de reprogramacion).</summary>
public sealed record AfectadoRecepcionDto(
    Guid AsignacionTurnoId,
    Guid ProfesionalId,
    Guid PacienteId,
    string PacienteNombre,
    string PacienteDocumento,
    DateOnly Fecha,
    TimeOnly? Hora,
    string DoctorNombre,
    string ServicioNombre,
    bool Llego,
    string? Telefono,
    string? TelefonoEmergencia,
    IReadOnlyList<ContactoAfectadoDto> Contactos);

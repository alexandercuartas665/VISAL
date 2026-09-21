namespace Visal.Application.Tenancy.Agendas;

/// <summary>Estado de un dia en el explorador de disponibilidad por agenda.</summary>
public enum EstadoDiaAgenda
{
    /// <summary>El profesional no tiene turno ese dia de la semana.</summary>
    SinTurno,
    /// <summary>Hay turno y el dia esta abierto: disponible con cupos.</summary>
    Disponible,
    /// <summary>Festivo nacional (cierra el dia).</summary>
    Festivo,
    /// <summary>Dia inactivo de la sede (cierra el dia).</summary>
    Inactivo,
    /// <summary>Novedad del profesional de dia completo (vacaciones/incapacidad/permiso).</summary>
    Novedad,
    /// <summary>Hay turno pero ya no quedan cupos libres (todos asignados).</summary>
    Completo
}

/// <summary>Un servicio (modulo) que tiene al menos un doctor con agenda.</summary>
public sealed record ServicioConAgendaDto(string Codigo, string Nombre, int Doctores);

/// <summary>Un doctor con agenda que atiende un servicio (modulo).</summary>
public sealed record DoctorConAgendaDto(Guid ProfesionalId, string NombreCompleto, string? TipoProfesional, int Turnos, int CuposSemana);

/// <summary>Un servicio del contrato del paciente que algun doctor con agenda presta.</summary>
public sealed record ServicioContratoAgendaDto(
    Guid ServicioContratoId, string? Codigo, string Descripcion, string? Modulo, string? Especialidad, int Doctores);

/// <summary>Disponibilidad de un dia concreto.</summary>
public sealed record DiaDisponibilidadDto(DateOnly Fecha, EstadoDiaAgenda Estado, int Cupos, string? Detalle);

/// <summary>Un mes del explorador (dias en orden).</summary>
public sealed record MesDisponibilidadDto(int Anio, int Mes, IReadOnlyList<DiaDisponibilidadDto> Dias);

/// <summary>Resultado del explorador: varios meses de disponibilidad de un doctor en una sede.</summary>
public sealed record DisponibilidadAgendaDto(Guid ProfesionalId, Guid SucursalId, IReadOnlyList<MesDisponibilidadDto> Meses);

/// <summary>
/// Explorador de asignacion por agendas (solo lectura, Ola 1): parte de los
/// servicios (modulos) que tienen doctores CON agenda, permite elegir servicio y
/// doctor, y muestra la disponibilidad del doctor combinando su agenda con los
/// festivos, los dias inactivos de la sede y sus novedades.
///
/// El vinculo servicio->doctor es el mismo criterio que Coordinacion:
/// <c>TipoProfesional.Nombre</c> ~ codigo de modulo (tolerante a plural/singular).
/// </summary>
public interface IAsignacionAgendasService
{
    /// <summary>Modulos (CatalogoTipoServicio activos) que tienen >=1 doctor con agenda.</summary>
    Task<IReadOnlyList<ServicioConAgendaDto>> ListarServiciosConAgendaAsync(CancellationToken ct = default);

    /// <summary>Doctores con agenda que atienden el modulo dado (match por TipoProfesional).</summary>
    Task<IReadOnlyList<DoctorConAgendaDto>> ListarDoctoresConAgendaAsync(string moduloCodigo, CancellationToken ct = default);

    /// <summary>Servicios del contrato del paciente que al menos un doctor con agenda presta
    /// (match por CUPS ProfesionalServicio.Codigo = ServicioContrato.CodigoServicio). Filtro
    /// opcional por descripcion o codigo.</summary>
    Task<IReadOnlyList<ServicioContratoAgendaDto>> ListarServiciosContratoConAgendaAsync(Guid contratoId, string? filtro, CancellationToken ct = default);

    /// <summary>Doctores con agenda que prestan el servicio de contrato indicado (por su CUPS).</summary>
    Task<IReadOnlyList<DoctorConAgendaDto>> ListarDoctoresPorServicioContratoAsync(Guid servicioContratoId, CancellationToken ct = default);

    /// <summary>Disponibilidad del doctor en la sede, desde (anioInicio, mesInicio) por N meses.
    /// Descuenta del cupo de cada dia los turnos ya asignados a ese doctor en esa fecha.</summary>
    Task<DisponibilidadAgendaDto> ObtenerDisponibilidadAsync(
        Guid profesionalId, Guid sucursalId, int anioInicio, int mesInicio, int meses = 2, CancellationToken ct = default);

    /// <summary>Horas de inicio de slot libres de un doctor en una fecha: los slots que
    /// generan los turnos de ese dia de la semana (paso = intervalo), menos los ya ocupados
    /// por turnos existentes con hora en esa fecha.</summary>
    Task<IReadOnlyList<TimeOnly>> SlotsDisponiblesAsync(Guid profesionalId, DateOnly fecha, CancellationToken ct = default);

    /// <summary>Agenda una cita en un dia/hora concreta reusando el flujo de Asignacion:
    /// crea la Asignacion (lote de 1) y su AsignacionTurno con el doctor, la fecha y la hora.
    /// Devuelve el Id de la Asignacion creada.</summary>
    Task<Guid> AgendarAsync(AgendarDesdeAgendaRequest req, Guid actor, CancellationToken ct = default);
}

/// <summary>Datos para agendar una cita desde un dia disponible del explorador.</summary>
public sealed record AgendarDesdeAgendaRequest(
    Guid ProfesionalId,
    Guid PacienteId,
    string ContratoCodigo,
    string ServicioContratoId,
    string NombreServicio,
    string TipoServicio,
    string? Modulo,
    string Sucursal,
    DateOnly Fecha,
    TimeOnly HoraInicio,
    string ViaIngresoCodigo,
    string ViaIngresoNombre,
    string? Observaciones);

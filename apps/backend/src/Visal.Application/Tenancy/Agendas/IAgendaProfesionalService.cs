namespace Visal.Application.Tenancy.Agendas;

/// <summary>Turno de la agenda propia de un profesional.</summary>
public sealed record AgendaProfesionalTurnoDto(
    Guid Id,
    DayOfWeek DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    int IntervaloMinutos)
{
    public int Cupos => PlantillaAgendaCalculos.Cupos(HoraInicio, HoraFin, IntervaloMinutos);
}

/// <summary>Agenda propia de un profesional (sus turnos + traza de plantilla origen).</summary>
public sealed record AgendaProfesionalDto(
    Guid ProfesionalId,
    Guid? PlantillaOrigenId,
    IReadOnlyList<AgendaProfesionalTurnoDto> Turnos);

/// <summary>Fila del listado maestro: profesional + resumen de su agenda.</summary>
public sealed record ProfesionalAgendaResumenDto(
    Guid ProfesionalId,
    string NombreCompleto,
    string? TipoProfesional,
    int Turnos,
    int Cupos);

/// <summary>Comando para agregar (Id null) o editar (Id != null) un turno de la agenda del profesional.</summary>
public sealed record GuardarAgendaProfesionalTurnoCmd(
    Guid? Id,
    DayOfWeek DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    int IntervaloMinutos);

/// <summary>
/// Agenda propia por profesional (Ola 2). Se arma importando (copiando) una
/// plantilla de agenda y luego se edita libremente. Cada profesional tiene la
/// suya; no esta ligada a servicios. La Ola futura de asignacion la consumira.
/// </summary>
public interface IAgendaProfesionalService
{
    /// <summary>Profesionales con el resumen de su agenda (numero de turnos y cupos).</summary>
    Task<IReadOnlyList<ProfesionalAgendaResumenDto>> ListarResumenAsync(string? filtro, CancellationToken ct = default);

    /// <summary>Agenda propia de un profesional (turnos ordenados por dia y hora).</summary>
    Task<AgendaProfesionalDto> ObtenerAsync(Guid profesionalId, CancellationToken ct = default);

    /// <summary>Importa una plantilla a un profesional COPIANDO sus turnos. Si
    /// <paramref name="reemplazar"/> es true (default) borra la agenda actual del
    /// profesional antes de copiar; si es false, agrega los turnos de la plantilla
    /// a los existentes. Devuelve el numero de turnos copiados.</summary>
    Task<int> ImportarPlantillaAsync(Guid profesionalId, Guid plantillaId, bool reemplazar, Guid actor, CancellationToken ct = default);

    /// <summary>Agrega o edita un turno de la agenda del profesional.</summary>
    Task<AgendaProfesionalTurnoDto> GuardarTurnoAsync(Guid profesionalId, GuardarAgendaProfesionalTurnoCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Quita un turno de la agenda del profesional. true si existia.</summary>
    Task<bool> EliminarTurnoAsync(Guid turnoId, Guid actor, CancellationToken ct = default);

    /// <summary>Vacia por completo la agenda del profesional. Devuelve cuantos turnos se borraron.</summary>
    Task<int> LimpiarAsync(Guid profesionalId, Guid actor, CancellationToken ct = default);
}

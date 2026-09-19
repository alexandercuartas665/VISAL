namespace Visal.Application.Tenancy.Agendas;

/// <summary>Turno de una plantilla de agenda para las vistas.</summary>
public sealed record PlantillaAgendaTurnoDto(
    Guid Id,
    DayOfWeek DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    int IntervaloMinutos)
{
    /// <summary>Cupos resultantes = (fin - inicio) / intervalo (0 si el rango o el intervalo no son validos).</summary>
    public int Cupos => PlantillaAgendaCalculos.Cupos(HoraInicio, HoraFin, IntervaloMinutos);
}

/// <summary>Plantilla de agenda con sus turnos (para el detalle) o sin ellos (para el listado).</summary>
public sealed record PlantillaAgendaDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    bool Activa,
    IReadOnlyList<PlantillaAgendaTurnoDto> Turnos);

/// <summary>Comando para crear (Id null) o actualizar (Id != null) los datos base de una plantilla.</summary>
public sealed record GuardarPlantillaAgendaCmd(
    Guid? Id,
    string Nombre,
    string? Descripcion,
    bool Activa);

/// <summary>Comando para agregar (Id null) o editar (Id != null) un turno de una plantilla.</summary>
public sealed record GuardarPlantillaAgendaTurnoCmd(
    Guid? Id,
    DayOfWeek DiaSemana,
    TimeOnly HoraInicio,
    TimeOnly HoraFin,
    int IntervaloMinutos);

/// <summary>Calculos compartidos de la plantilla de agenda.</summary>
public static class PlantillaAgendaCalculos
{
    /// <summary>Cupos = floor((fin - inicio) / intervalo). 0 si el rango o el intervalo no son validos.</summary>
    public static int Cupos(TimeOnly inicio, TimeOnly fin, int intervaloMinutos)
    {
        if (intervaloMinutos <= 0) { return 0; }
        var minutos = (int)(fin.ToTimeSpan() - inicio.ToTimeSpan()).TotalMinutes;
        return minutos > 0 ? minutos / intervaloMinutos : 0;
    }
}

/// <summary>
/// Parametrizacion de plantillas de agenda reutilizables (Ola 1 del modulo de
/// Agendas). Se definen aqui de forma independiente y luego se importan (copian)
/// a cada profesional en la Ola 2. No estan ligadas a servicios.
/// </summary>
public interface IPlantillaAgendaService
{
    /// <summary>Plantillas ordenadas por Nombre. Por default solo activas. No incluye turnos.</summary>
    Task<IReadOnlyList<PlantillaAgendaDto>> ListarAsync(bool incluirInactivas = false, CancellationToken ct = default);

    /// <summary>Una plantilla con sus turnos (ordenados por dia y hora). Null si no existe.</summary>
    Task<PlantillaAgendaDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea o actualiza los datos base de la plantilla. Devuelve el DTO (sin turnos).</summary>
    Task<PlantillaAgendaDto> GuardarAsync(GuardarPlantillaAgendaCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Borra la plantilla y sus turnos (cascade). true si existia.</summary>
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Agrega o edita un turno de la plantilla. Devuelve el turno persistido.</summary>
    Task<PlantillaAgendaTurnoDto> GuardarTurnoAsync(Guid plantillaId, GuardarPlantillaAgendaTurnoCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Quita un turno de la plantilla. true si existia.</summary>
    Task<bool> EliminarTurnoAsync(Guid turnoId, Guid actor, CancellationToken ct = default);
}

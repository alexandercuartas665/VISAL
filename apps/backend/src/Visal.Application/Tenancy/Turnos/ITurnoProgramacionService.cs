namespace Visal.Application.Tenancy.Turnos;

/// <summary>Fila para la lista de programaciones. Incluye conteo de turnos derivado
/// del GridDataJson para pintarlo sin cargar el JSON completo en la UI.</summary>
public sealed record TurnoProgramacionDto(
    Guid Id,
    IReadOnlyList<Guid> SucursalIds,
    IReadOnlyList<string> SucursalNombres,
    Guid? TipoServicioId,
    string? TipoServicioNombre,
    string Nombre,
    int Anio,
    int Mes,
    int CantidadTurnos,
    bool Activa);

/// <summary>Detalle completo para el editor: incluye el JSON de la grilla y la
/// descripcion opcional.</summary>
public sealed record TurnoProgramacionDetailDto(
    Guid Id,
    IReadOnlyList<Guid> SucursalIds,
    Guid? TipoServicioId,
    string Nombre,
    int Anio,
    int Mes,
    string? Descripcion,
    string GridDataJson,
    bool Activa);

public sealed record CrearTurnoProgramacionCmd(
    IReadOnlyList<Guid> SucursalIds,
    Guid? TipoServicioId,
    string Nombre,
    int Anio,
    int Mes,
    string? Descripcion,
    string GridDataJson);

public sealed record ActualizarTurnoProgramacionCmd(
    IReadOnlyList<Guid> SucursalIds,
    Guid? TipoServicioId,
    string Nombre,
    int Anio,
    int Mes,
    string? Descripcion,
    string GridDataJson,
    bool Activa);

/// <summary>
/// CRUD + duplicar de <see cref="Visal.Domain.Entities.TurnoProgramacion"/>. Reglas
/// duras (min/max turnos, unicidad de nombre, overload 24h/dia si el tenant lo
/// habilita) se aplican aca en el servicio, no en la UI. Ademas: se exige al menos
/// UNA sucursal vinculada al crear o actualizar.
/// </summary>
public interface ITurnoProgramacionService
{
    Task<IReadOnlyList<TurnoProgramacionDto>> ListarAsync(
        Guid? sucursalId, Guid? tipoServicioId, int? anio, int? mes, bool soloActivas,
        CancellationToken ct = default);

    Task<TurnoProgramacionDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    Task<Guid> CrearAsync(CrearTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default);

    Task ActualizarAsync(Guid id, ActualizarTurnoProgramacionCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Clona la programacion cambiando destino {anio, mes}. El nombre se
    /// preserva y las sedes vinculadas tambien.</summary>
    Task<Guid> DuplicarAsync(Guid id, int nuevoAnio, int nuevoMes, Guid actor, CancellationToken ct = default);

    /// <summary>Soft-disable. Se mantiene la fila para preservar historial.</summary>
    Task DesactivarAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Borrado fisico. En Fase 1 siempre permitido; a partir de la Fase de
    /// Asignacion validara que no haya profesionales asignados.</summary>
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);
}

namespace Visal.Application.Tenancy.Agendas;

/// <summary>Un dia del calendario de una sede: festivo (calculado) y/o inactivo (manual).</summary>
public sealed record DiaCalendarioDto(
    DateOnly Fecha,
    bool EsFestivo,
    string? FestivoNombre,
    bool EsInactivo,
    Guid? InactivoId,
    string? Motivo)
{
    /// <summary>El dia esta cerrado para la agenda (festivo o marcado inactivo).</summary>
    public bool Cerrado => EsFestivo || EsInactivo;
}

/// <summary>Un mes del calendario de una sede (dias del mes, en orden).</summary>
public sealed record MesCalendarioDto(int Anio, int Mes, IReadOnlyList<DiaCalendarioDto> Dias);

/// <summary>
/// Calendario por sede (Ola 3). Combina los festivos nacionales calculados
/// (<see cref="IFestivosColombiaService"/>) con los dias inactivos que el usuario
/// crea por sede. El festivo cierra el dia por defecto.
/// </summary>
public interface ICalendarioService
{
    /// <summary>Arma el mes de una sede con festivos + dias inactivos.</summary>
    Task<MesCalendarioDto> ObtenerMesAsync(Guid sucursalId, int anio, int mes, CancellationToken ct = default);

    /// <summary>Marca un dia como inactivo para la sede (idempotente por sede+fecha).
    /// Devuelve el Id del dia inactivo.</summary>
    Task<Guid> MarcarInactivoAsync(Guid sucursalId, DateOnly fecha, string? motivo, Guid actor, CancellationToken ct = default);

    /// <summary>Quita un dia inactivo por su Id. true si existia.</summary>
    Task<bool> QuitarInactivoAsync(Guid diaInactivoId, Guid actor, CancellationToken ct = default);

    /// <summary>Festivos del anio (para listados). Delegado del calculador.</summary>
    IReadOnlyList<FestivoColombiaDto> FestivosDeAnio(int anio);
}

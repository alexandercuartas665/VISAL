namespace Visal.Application.Tenancy.Turnos;

public sealed record TipoTurnoDto(
    Guid Id,
    string Codigo,
    string Etiqueta,
    decimal HorasDefault,
    string ColorFondo,
    string ColorTexto,
    string ColorBorde,
    int Orden,
    bool Activo);

/// <summary>
/// Catalogo de tipos de turno por tenant. En Fase 1 se expone solo lectura
/// (poblar el sidebar del editor). CRUD completo se agrega en una fase posterior
/// cuando se cree la pagina /config/tipos-turno.
/// </summary>
public interface ITipoTurnoService
{
    /// <summary>Tipos ordenados por Orden asc. Por default solo activos.</summary>
    Task<IReadOnlyList<TipoTurnoDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default);
}

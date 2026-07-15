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

/// <summary>Comando para crear (Id null) o actualizar (Id != null) un tipo de turno.
/// Codigo se normaliza a MAYUSCULAS + trim. Validaciones duras en el servicio:
/// codigo obligatorio + unico por tenant (respetando el propio Id en updates),
/// horas 0..24, colores en formato #RRGGBB o #RRGGBBAA.</summary>
public sealed record GuardarTipoTurnoCmd(
    Guid? Id,
    string Codigo,
    string Etiqueta,
    decimal HorasDefault,
    string ColorFondo,
    string ColorTexto,
    string ColorBorde,
    int Orden,
    bool Activo);

/// <summary>
/// Catalogo de tipos de turno por tenant. Alimenta el sidebar del editor de
/// programaciones (<c>/cfg-turnos</c>) y la pagina de administracion
/// <c>/cfg-tipos-turno</c>.
/// </summary>
public interface ITipoTurnoService
{
    /// <summary>Tipos ordenados por Orden asc. Por default solo activos.</summary>
    Task<IReadOnlyList<TipoTurnoDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default);

    /// <summary>Crea o actualiza un tipo. Devuelve el DTO persistido.</summary>
    Task<TipoTurnoDto> GuardarAsync(GuardarTipoTurnoCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Borrado fisico. Sin validacion de "en uso" en esta fase — el usuario
    /// es responsable de no eliminar un tipo que este siendo referenciado por
    /// programaciones existentes (rompe visualmente el grid, no datos).</summary>
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);
}

namespace Visal.Application.Tenancy;

public sealed record CatalogoTipoServicioDto(
    Guid Id,
    string Codigo,
    string Nombre,
    int Orden,
    bool Activo);

public sealed record GuardarTipoServicioRequest(
    Guid? Id,
    string Codigo,
    string Nombre,
    int Orden,
    bool Activo);

/// <summary>
/// Catalogo dinamico de modulos / tipos de servicio contratado. Es la fuente
/// unica de verdad para: dropdowns del wizard de asignacion, columnas de la
/// pantalla /config/menu-hc, permisos coordinables por usuario, y validacion
/// del import Excel de servicios de contrato.
/// </summary>
public interface ICatalogoTipoServicioService
{
    /// <summary>Lista los tipos del tenant activo ordenados por Orden asc.</summary>
    Task<IReadOnlyList<CatalogoTipoServicioDto>> ListarAsync(bool incluirInactivos = false, CancellationToken ct = default);

    Task<CatalogoTipoServicioDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<CatalogoTipoServicioDto> GuardarAsync(GuardarTipoServicioRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);
}

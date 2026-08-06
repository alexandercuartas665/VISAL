namespace Visal.Application.Tenancy;

public sealed record ReporteConfigDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden);

public sealed record ReporteConfigDetalleDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string QuerySql,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden,
    IReadOnlyList<Guid> UsuariosAsignados);

public sealed record GuardarReporteRequest(
    string Nombre,
    string? Descripcion,
    string QuerySql,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden,
    IReadOnlyList<Guid>? UsuariosAsignados);

public sealed record EjecutarReporteRequest(
    Guid? SedeId,
    DateOnly? Desde,
    DateOnly? Hasta);

public sealed record ReporteResultado(
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<object?>> Filas,
    int TotalFilas);

public interface IReporteService
{
    /// <summary>Reportes que el usuario logueado puede consultar.</summary>
    Task<IReadOnlyList<ReporteConfigDto>> ListarMisReportesAsync(CancellationToken ct = default);

    /// <summary>CRUD administrativo (todos los reportes del tenant).</summary>
    Task<IReadOnlyList<ReporteConfigDto>> ListarTodosAsync(CancellationToken ct = default);
    Task<ReporteConfigDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CrearAsync(GuardarReporteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> ActualizarAsync(Guid id, GuardarReporteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Ejecuta el SELECT parametrizado. Solo tokens whitelisted se inyectan como
    /// parametros de comando (nunca por concatenacion). Bloquea SQL con INSERT/
    /// UPDATE/DELETE/ALTER/DROP/CREATE/TRUNCATE/GRANT.
    /// </summary>
    Task<ReporteResultado> EjecutarAsync(Guid reporteId, EjecutarReporteRequest req, CancellationToken ct = default);
}

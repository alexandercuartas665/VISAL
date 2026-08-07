namespace Visal.Application.Tenancy;

// ---- DTOs runner (tenant final, /reportes) ------------------------------------
public sealed record ReporteDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    bool FiltraSede,
    bool FiltraFechas,
    int Orden);

// ---- DTOs galeria (admin del tenant, /config/reportes) ------------------------
public sealed record ReporteGaleriaDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    bool FiltraSede,
    bool FiltraFechas,
    int Orden,
    bool Activo,
    IReadOnlyList<Guid> UsuariosAsignados);

// ---- DTOs catalogo (Super Admin de plataforma) --------------------------------
public sealed record ReporteCatalogoDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden);

public sealed record ReporteCatalogoDetalleDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string? Categoria,
    string QuerySql,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden);

public sealed record GuardarCatalogoRequest(
    string Nombre,
    string? Descripcion,
    string? Categoria,
    string QuerySql,
    bool FiltraSede,
    bool FiltraFechas,
    bool Habilitado,
    int Orden);

// ---- Ejecucion ----------------------------------------------------------------
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
    // ---- Runner (usuario del tenant) ------------------------------------------
    /// <summary>Reportes que el usuario logueado puede consultar: del catalogo habilitado,
    /// activados por su tenant, y (sin asignacion o asignado a el).</summary>
    Task<IReadOnlyList<ReporteDto>> ListarMisReportesAsync(CancellationToken ct = default);

    // ---- Galeria (admin del tenant) -------------------------------------------
    /// <summary>Todos los reportes del catalogo habilitado, con su estado Activo y usuarios
    /// asignados PARA EL TENANT ACTUAL. El tenant no ve ni edita el SQL.</summary>
    Task<IReadOnlyList<ReporteGaleriaDto>> ListarGaleriaAsync(CancellationToken ct = default);

    /// <summary>Enciende/apaga un reporte del catalogo para el tenant actual.</summary>
    Task SetActivoAsync(Guid catalogoId, bool activo, Guid actor, CancellationToken ct = default);

    /// <summary>Reemplaza el conjunto de usuarios que pueden ver un reporte en el tenant actual.</summary>
    Task SetUsuariosAsync(Guid catalogoId, IReadOnlyList<Guid> usuarios, Guid actor, CancellationToken ct = default);

    // ---- Catalogo (Super Admin de plataforma) ---------------------------------
    Task<IReadOnlyList<ReporteCatalogoDto>> ListarCatalogoAsync(CancellationToken ct = default);
    Task<ReporteCatalogoDetalleDto?> ObtenerCatalogoAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CrearCatalogoAsync(GuardarCatalogoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> ActualizarCatalogoAsync(Guid id, GuardarCatalogoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> EliminarCatalogoAsync(Guid id, Guid actor, CancellationToken ct = default);

    // ---- Ejecucion ------------------------------------------------------------
    /// <summary>
    /// Ejecuta el SELECT del catalogo (el tenant nunca lo escribe). Valida que el reporte
    /// este activo para el tenant y que el usuario tenga permiso. Solo tokens whitelisted se
    /// inyectan como parametros de comando; bloquea SQL de escritura.
    /// </summary>
    Task<ReporteResultado> EjecutarAsync(Guid catalogoId, EjecutarReporteRequest req, CancellationToken ct = default);
}

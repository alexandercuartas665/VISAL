namespace Visal.Application.Tenancy;

/// <summary>Resultado de ejecutar una query en la consola SQL admin.
/// Si fue SELECT, llena Columnas + Filas. Si fue DML/DDL, llena RowsAffected.</summary>
public sealed record SqlConsoleExecutionDto(
    bool Success,
    string QueryType,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<string?>> Filas,
    int? RowsAffected,
    int? RowsReturned,
    long DurationMs,
    string? ErrorMessage);

public sealed record SqlConsoleLogDto(
    Guid Id,
    Guid? TenantId,
    Guid? UserId,
    string? UserName,
    string Query,
    string? QueryType,
    int? RowsAffected,
    int? RowsReturned,
    long DurationMs,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset ExecutedAt);

/// <summary>Resumen de una tabla en el schema public, para el panel de
/// tarjetas del SQL console. Filas es la estimacion de pg_stat_user_tables
/// (no un COUNT exacto — es rapido y suficiente para el explorador).</summary>
public sealed record SqlTableInfoDto(
    string Tabla,
    string Descripcion,
    long FilasEstimadas,
    string Grupo);

public interface ISqlConsoleService
{
    /// <summary>Ejecuta SQL crudo contra la BD. Si es SELECT devuelve filas
    /// (limite configurable). Si es DML/DDL devuelve filas afectadas.
    /// Siempre registra en sql_console_logs (exito o error).</summary>
    Task<SqlConsoleExecutionDto> EjecutarAsync(string sql, Guid actorUserId, string? actorUserName,
        int rowLimit = 1000, CancellationToken ct = default);

    Task<IReadOnlyList<SqlConsoleLogDto>> ListarHistorialAsync(int take = 50, CancellationToken ct = default);

    /// <summary>Lista todas las tablas del schema public con descripcion
    /// humana y conteo aproximado de filas. Alimenta el panel de tarjetas
    /// del explorador de la consola SQL.</summary>
    Task<IReadOnlyList<SqlTableInfoDto>> ListarTablasAsync(CancellationToken ct = default);
}

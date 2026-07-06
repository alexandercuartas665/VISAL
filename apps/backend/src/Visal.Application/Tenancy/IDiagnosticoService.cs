namespace Visal.Application.Tenancy;

public sealed record DiagnosticoDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    bool Habilitado,
    string? Fuente);

public sealed record SaveDiagnosticoRequest(
    Guid? Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    bool Habilitado,
    string? Fuente);

public sealed record DiagnosticoImportProgress(string Fase, int Procesados, int Total);

public sealed record DiagnosticoImportRow(
    string? Codigo, string? Nombre, string? Descripcion, string? Habilitado, string? Fuente);

/// <summary>
/// Servicio del modulo Base de Datos de Diagnosticos (reemplaza la consulta a la
/// WHO ICD-11 API en el modal del paciente y HC). Cada tenant mantiene su propia
/// BD, cargada tipicamente por import Excel.
/// </summary>
public interface IDiagnosticoService
{
    /// <summary>Busqueda paginada por termino (matches en Codigo o Nombre, case-insensitive).
    /// Solo devuelve registros con Habilitado=true cuando <paramref name="soloHabilitados"/>
    /// es true (uso desde el modal de busqueda del paciente).</summary>
    Task<(IReadOnlyList<DiagnosticoDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, bool soloHabilitados, CancellationToken ct = default);

    Task<DiagnosticoDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por codigo exacto (case-insensitive). Usado para autocompletar
    /// el nombre a partir del codigo digitado en el input rapido del paciente.</summary>
    Task<DiagnosticoDto?> GetByCodigoAsync(string codigo, CancellationToken ct = default);

    Task<DiagnosticoDto?> SaveAsync(SaveDiagnosticoRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Importa filas del Excel en lotes; reporta avance via progress.
    /// Merge por (TenantId, Codigo): si existe se actualizan Nombre / Descripcion /
    /// Habilitado / Fuente; si no existe se inserta. Devuelve (insertados, actualizados).</summary>
    Task<(int inserted, int updated)> ImportAsync(
        IReadOnlyList<DiagnosticoImportRow> rows,
        Guid actorUserId,
        IProgress<DiagnosticoImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Borra TODOS los diagnosticos del tenant. Devuelve la cantidad borrada.</summary>
    Task<int> ClearAllAsync(Guid actorUserId, CancellationToken ct = default);
}

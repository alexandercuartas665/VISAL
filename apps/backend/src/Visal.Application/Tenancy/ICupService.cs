namespace Visal.Application.Tenancy;

/// <summary>Resumen ligero para la grilla del modulo CUPS.</summary>
public sealed record CupDto(
    Guid Id,
    string? Codigo,
    string? Nombre,
    string? Descripcion,
    string? Habilitado,
    string? ExtraIV,
    string? ExtraV,
    string? IsStandardGEL,
    string? IsStandardMSPS);

/// <summary>Detalle completo (las 22 columnas del muestra oficial CUPS).</summary>
public sealed record CupDetailDto(
    Guid Id,
    string? Tabla, string? Codigo, string? Nombre, string? Descripcion,
    string? Habilitado, string? Aplicacion, string? IsStandardGEL, string? IsStandardMSPS,
    string? ExtraI, string? ExtraII, string? ExtraIII, string? ExtraIV, string? ExtraV,
    string? ExtraVI, string? ExtraVII, string? ExtraVIII, string? ExtraIX, string? ExtraX,
    string? ValorRegistro, string? UsuarioResponsable, DateTimeOffset? FechaActualizacion,
    string? IsPublicPrivate);

/// <summary>Payload de alta/edicion. Si Id es null se crea; si no, se actualiza.</summary>
public sealed record SaveCupRequest(
    Guid? Id,
    string? Tabla, string? Codigo, string? Nombre, string? Descripcion,
    string? Habilitado, string? Aplicacion, string? IsStandardGEL, string? IsStandardMSPS,
    string? ExtraI, string? ExtraII, string? ExtraIII, string? ExtraIV, string? ExtraV,
    string? ExtraVI, string? ExtraVII, string? ExtraVIII, string? ExtraIX, string? ExtraX,
    string? ValorRegistro, string? UsuarioResponsable, DateTimeOffset? FechaActualizacion,
    string? IsPublicPrivate);

/// <summary>Notificacion de avance durante la importacion (igual que Medicamentos).</summary>
public sealed record CupImportProgress(string Fase, int Procesados, int Total);

/// <summary>Fila tal como llega del Excel del MSPS - todas string para tolerar el formato real.</summary>
public sealed record CupImportRow(
    string? Tabla, string? Codigo, string? Nombre, string? Descripcion,
    string? Habilitado, string? Aplicacion, string? IsStandardGEL, string? IsStandardMSPS,
    string? ExtraI, string? ExtraII, string? ExtraIII, string? ExtraIV, string? ExtraV,
    string? ExtraVI, string? ExtraVII, string? ExtraVIII, string? ExtraIX, string? ExtraX,
    string? ValorRegistro, string? UsuarioResponsable, string? FechaActualizacion,
    string? IsPublicPrivate);

public interface ICupService
{
    /// <summary>Lista con paginacion + busqueda libre sobre codigo / nombre / descripcion / clasificacion.</summary>
    Task<(IReadOnlyList<CupDto> rows, int total)> SearchAsync(
        string? termino, int skip, int take, CancellationToken ct = default);

    Task<CupDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<CupDetailDto?> SaveAsync(SaveCupRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Importa filas del Excel CUPS en lotes, reportando avance via
    /// <paramref name="progress"/>. Devuelve cuantas se insertaron en total.
    /// </summary>
    Task<int> ImportAsync(
        IReadOnlyList<CupImportRow> rows,
        Guid actorUserId,
        IProgress<CupImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Borra TODA la BD de CUPS del tenant (para recargar limpio). Devuelve cuantas se borraron.</summary>
    Task<int> ClearAllAsync(Guid actorUserId, CancellationToken ct = default);
}

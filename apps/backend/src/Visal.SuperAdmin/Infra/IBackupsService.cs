namespace Visal.SuperAdmin.Infra;

/// <summary>Una copia de seguridad (archivo) de la carpeta de backups del servidor.</summary>
public sealed record BackupFileInfo(string Nombre, long Bytes, DateTimeOffset Fecha, string Tipo);

/// <summary>Totales del modulo de copias: cantidad y peso de las copias, peso de la
/// carpeta de archivos subidos (uploads) y espacio del disco donde viven.</summary>
public sealed record BackupResumen(
    int Cantidad,
    long TotalCopiasBytes,
    long UploadsBytes,
    long DiscoTotalBytes,
    long DiscoLibreBytes,
    string CarpetaCopias,
    string CarpetaUploads,
    bool CarpetaCopiasExiste);

/// <summary>
/// Inventario y mantenimiento de las copias de seguridad de la base de datos que el
/// script de deploy deja en la carpeta de backups del host (montada en el contenedor).
/// Listar/Resumen son de solo lectura; Eliminar/Rotar son acciones sensibles que SOLO
/// debe invocar el Super Admin de plataforma (una copia contiene datos de TODAS las
/// agencias). La descarga se sirve por endpoint con el mismo gate.
/// </summary>
public interface IBackupsService
{
    /// <summary>Archivos de la carpeta de copias, mas recientes primero.</summary>
    IReadOnlyList<BackupFileInfo> Listar();

    /// <summary>Totales (copias + uploads + disco).</summary>
    BackupResumen Resumen();

    /// <summary>Ruta absoluta validada del archivo (anti path-traversal) o null si el
    /// nombre no es un archivo valido dentro de la carpeta de copias.</summary>
    string? RutaSegura(string nombre);

    /// <summary>Borra una copia (auditado). Devuelve (false, motivo) si el nombre no es valido.</summary>
    Task<(bool ok, string? error)> EliminarAsync(string nombre, Guid actor, Guid? tenantId, CancellationToken ct = default);

    /// <summary>Conserva las <paramref name="conservar"/> copias mas recientes y borra el
    /// resto (auditado). Devuelve cuantas borro y cuantos bytes libero.</summary>
    Task<(int borradas, long bytesLiberados)> RotarAsync(int conservar, Guid actor, Guid? tenantId, CancellationToken ct = default);
}

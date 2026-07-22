using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

/// <summary>
/// Command para <c>GenerarAsync</c>. El servicio arma el nombre por defecto si
/// <see cref="Nombre"/> es null (patron "{Tipo} {Timestamp}").
/// </summary>
public sealed record GenerarSnapshotCmd(
    TipoSnapshot Tipo,
    string? Nombre,
    string FiltrosJson);

/// <summary>Filtros del listado de snapshots.</summary>
public sealed record FiltrosListaSnapshotDto(
    Guid? UsuarioId = null,
    DateOnly? FechaInicio = null,
    DateOnly? FechaFin = null,
    Guid? AseguradoraId = null);

/// <summary>Fila del listado.</summary>
public sealed record FacturacionSnapshotDto(
    Guid Id,
    string Nombre,
    TipoSnapshot Tipo,
    EstadoSnapshot Estado,
    DateTimeOffset FechaEjecucionInicio,
    DateTimeOffset? FechaEjecucionFin,
    int? DuracionMs,
    int TotalFilas,
    Guid? CreadoPor,
    Guid? ArchivadoPor,
    string? MotivoArchivado,
    DateTimeOffset? FechaArchivado,
    string? ErrorMensaje,
    Guid? AseguradoraId,
    string? AseguradoraNombre);

/// <summary>Vista detallada de un snapshot (metadata + columnas del builder).</summary>
public sealed record FacturacionSnapshotDetalleDto(
    FacturacionSnapshotDto Metadata,
    IReadOnlyList<string> Columnas,
    string FiltrosJson);

/// <summary>Resultado paginado de un ListarFilas.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Pagina,
    int TamanoPagina);

/// <summary>Entrada del historial de cambios de celdas de un snapshot.</summary>
public sealed record CambioCeldaDto(
    Guid Id,
    Guid FilaId,
    int NumeroFila,
    string ColumnaOriginal,
    string? ValorAntes,
    string? ValorDespues,
    Guid ActorUserId,
    DateTimeOffset Fecha,
    string? Motivo);

/// <summary>
/// Motor generico de snapshots de facturacion. Los tipos concretos aportan un
/// <see cref="ISnapshotBuilder"/>; el motor orquesta ciclo de vida, persistencia
/// paginada y archivado.
/// </summary>
public interface IFacturacionSnapshotService
{
    /// <summary>
    /// Genera un snapshot sincronicamente. Levanta la fila metadata, corre el
    /// builder y persiste filas. Devuelve el id del snapshot cualquiera sea el
    /// resultado (Vigente o Fallido) — el caller consulta <c>Obtener</c> para el
    /// estado final.
    /// </summary>
    Task<Guid> GenerarAsync(GenerarSnapshotCmd cmd, Guid actor, CancellationToken ct = default);

    /// <summary>Lista snapshots del tenant activo. <c>estado</c> filtra Vigentes o Archivados.</summary>
    Task<IReadOnlyList<FacturacionSnapshotDto>> ListarAsync(
        EstadoSnapshot estado,
        TipoSnapshot? tipo = null,
        FiltrosListaSnapshotDto? filtros = null,
        CancellationToken ct = default);

    /// <summary>Devuelve la metadata + columnas + filtros del snapshot.</summary>
    Task<FacturacionSnapshotDetalleDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Devuelve una pagina de filas del snapshot. <c>ordenColumna</c> es opcional; si es
    /// null se ordena por <c>NumeroFila</c>. <c>buscar</c> hace ILIKE sobre <c>DatosJson::text</c>.
    /// Cada fila del resultado es un diccionario columna -&gt; valor.
    /// </summary>
    Task<PagedResult<IReadOnlyDictionary<string, object?>>> ListarFilasAsync(
        Guid snapshotId,
        int pagina,
        int tamanoPagina,
        string? ordenColumna = null,
        bool ordenDesc = false,
        string? buscar = null,
        CancellationToken ct = default);

    /// <summary>
    /// Mueve el snapshot a Archivado. Requiere <paramref name="motivo"/> no vacio (min 10 chars).
    /// Falla si el snapshot no esta Vigente.
    /// </summary>
    Task ArchivarAsync(Guid id, string motivo, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Exporta el snapshot en xlsx. Los headers respetan EXACTAMENTE los nombres del
    /// builder (incluyendo tildes rotas del template original). Devuelve null si el
    /// snapshot no existe o no es del tenant activo.
    /// </summary>
    Task<ArchivoExportado?> ExportarExcelAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Exporta el snapshot en CSV con separador ; y encoding UTF-8 con BOM (para que
    /// Excel Colombia lo abra bien). Devuelve null si no existe o no es del tenant.
    /// </summary>
    Task<ArchivoExportado?> ExportarCsvAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Actualiza el valor de una celda especifica del snapshot y persiste el cambio
    /// en la tabla de trazabilidad. Solo permitido en snapshots Vigentes.
    /// <paramref name="valorNuevo"/> = null limpia la celda.
    /// <paramref name="motivo"/> es opcional pero recomendado.
    /// Devuelve true si hubo cambio, false si el valor era igual (no se registra).
    /// </summary>
    Task<bool> ActualizarValorCeldaAsync(
        Guid snapshotId,
        Guid filaId,
        string columna,
        string? valorNuevo,
        Guid actor,
        string? motivo = null,
        CancellationToken ct = default);

    /// <summary>Historial completo de cambios manuales sobre un snapshot, mas reciente primero.</summary>
    Task<IReadOnlyList<CambioCeldaDto>> ListarCambiosAsync(Guid snapshotId, CancellationToken ct = default);
}

/// <summary>
/// Archivo binario producto de un export. La capa de presentacion lo devuelve al cliente
/// via Results.File.
/// </summary>
public sealed record ArchivoExportado(byte[] Contenido, string MimeType, string NombreArchivo);

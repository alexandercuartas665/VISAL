using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

/// <summary>
/// Servicio para gestionar la preferencia del tenant sobre como aparece cada
/// columna del archivo de salida (Excel/CSV) de un snapshot de facturacion.
/// La lista canonica de columnas la publica el <see cref="ISnapshotBuilder"/>
/// del tipo; este servicio solo capa arriba con orden/visibilidad/alias.
/// </summary>
public interface ISnapshotColumnaConfigService
{
    /// <summary>
    /// Lista todas las columnas del builder para <paramref name="tipo"/>, con el
    /// override del tenant aplicado (o defaults si no hay). Orden final:
    /// primero las columnas con override por Orden ascendente, luego las
    /// columnas nuevas del builder que aun no tienen override (al final,
    /// visibles, sin alias). Nunca devuelve columnas que el builder ya no
    /// publica — asi purgamos overrides huerfanos automaticamente.
    /// </summary>
    Task<IReadOnlyList<ColumnaConfigItemDto>> ListarAsync(TipoSnapshot tipo, CancellationToken ct = default);

    /// <summary>
    /// Guarda la config completa para <paramref name="tipo"/>. Cualquier
    /// override viejo que no aparezca en <paramref name="items"/> se elimina
    /// — la lista es la fuente autoritativa. Upsert por columna.
    /// </summary>
    Task GuardarAsync(TipoSnapshot tipo, IReadOnlyList<ColumnaConfigItemDto> items, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Borra TODOS los overrides del tenant para <paramref name="tipo"/> —
    /// las siguientes exportaciones usan el orden natural del builder.
    /// </summary>
    Task ResetAsync(TipoSnapshot tipo, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Uso interno del exportador. Devuelve solo lo necesario para armar la
    /// primera fila del archivo de salida: por cada columna del builder,
    /// dice si es visible, en que orden va, y con que header (alias o
    /// original). Optimiza el caso comun de descarga sin cargar entities.
    /// </summary>
    Task<IReadOnlyList<ColumnaExportInfo>> ObtenerParaExportAsync(TipoSnapshot tipo, IReadOnlyList<string> columnasCanonicas, CancellationToken ct = default);
}

/// <summary>DTO expuesto a la UI para editar y persistir el override del tenant.</summary>
public sealed record ColumnaConfigItemDto(
    string ColumnaOriginal,
    int Orden,
    bool Visible,
    string? Alias,
    string? Descripcion,
    string? RutaOrigen);

/// <summary>Info compacta usada por el exportador (ya filtrada + ordenada).</summary>
public sealed record ColumnaExportInfo(string ColumnaOriginal, string HeaderExport);

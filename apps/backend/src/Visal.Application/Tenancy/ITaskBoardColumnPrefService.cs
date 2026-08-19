namespace Visal.Application.Tenancy;

/// <summary>Preferencia de una columna de la vista tabla del tablero, para un usuario.</summary>
public sealed record TaskColumnPrefDto(string ColumnKey, bool Visible, string? Alias, int? Orden, int? Ancho);

/// <summary>Alta/edicion de una preferencia de columna (parte de un lote).</summary>
public sealed record SaveTaskColumnPrefRequest(string ColumnKey, bool Visible, string? Alias, int? Orden, int? Ancho);

/// <summary>
/// Preferencias de columnas de la VISTA TABLA de un tablero, POR USUARIO: orden, mostrar/ocultar,
/// alias y ancho. Cada usuario ajusta su propia disposicion sin afectar a los demas.
/// </summary>
public interface ITaskBoardColumnPrefService
{
    /// <summary>Preferencias del usuario para el tablero. Vacio = todo en default.</summary>
    Task<IReadOnlyList<TaskColumnPrefDto>> ListAsync(Guid boardId, Guid userId, CancellationToken ct = default);

    /// <summary>Guarda el lote de preferencias del usuario para el tablero. Las filas que quedan en
    /// default puro (visible, sin alias/orden/ancho) se borran para no ensuciar la tabla.</summary>
    Task GuardarLoteAsync(Guid boardId, Guid userId, IReadOnlyList<SaveTaskColumnPrefRequest> items, CancellationToken ct = default);
}

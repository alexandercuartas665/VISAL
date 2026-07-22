using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

/// <summary>
/// Contrato que implementa cada tipo de snapshot para producir sus filas. El
/// motor generico (<see cref="IFacturacionSnapshotService"/>) resuelve el
/// builder correcto por <see cref="TipoAplicable"/> via DI e itera el
/// <see cref="ConstruirAsync"/> persistiendo cada diccionario como una fila.
/// </summary>
public interface ISnapshotBuilder
{
    /// <summary>Tipo de snapshot que este builder produce.</summary>
    TipoSnapshot TipoAplicable { get; }

    /// <summary>
    /// Nombres EXACTOS de las columnas del snapshot, en el orden en que deben
    /// aparecer en el Excel de salida. Se respeta el formato original impuesto
    /// por la EPS/MinSalud (incluyendo tildes rotas del template).
    /// </summary>
    IReadOnlyList<string> Columnas { get; }

    /// <summary>
    /// Descripciones intrínsecas de cada columna — de qué modulo/campo sale y
    /// para qué sirve. Se usan como valor por defecto en la UI de Configurar
    /// columnas cuando el tenant no ha definido una descripción propia.
    /// Clave = <see cref="Columnas"/> tal cual; columnas sin entrada devuelven
    /// null en el default. Vacío por defecto para builders que no las publican.
    /// </summary>
    IReadOnlyDictionary<string, string?> Descripciones =>
        System.Collections.Immutable.ImmutableDictionary<string, string?>.Empty;

    /// <summary>
    /// Construye las filas del snapshot para los filtros dados. Cada elemento
    /// del stream es un diccionario cuyos keys DEBEN coincidir con
    /// <see cref="Columnas"/> (los que falten se guardan como null).
    /// </summary>
    /// <param name="filtrosJson">Filtros serializados. Cada tipo interpreta su propia forma.</param>
    /// <param name="ct">Token de cancelacion — el builder debe respetarlo.</param>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ConstruirAsync(
        string filtrosJson,
        CancellationToken ct = default);
}

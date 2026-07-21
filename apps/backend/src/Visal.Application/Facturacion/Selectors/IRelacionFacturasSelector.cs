namespace Visal.Application.Facturacion.Selectors;

/// <summary>
/// Encuentra los "hechos facturables" que forman la Relacion de Facturas.
/// Esta interfaz vive APARTE del builder del snapshot: el builder solo mapea
/// hechos a columnas del template EPS. Si cambia el criterio de que cuenta
/// como facturable, solo se toca la implementacion del selector.
///
/// Contrato: cada hecho es una unidad facturable ya normalizada (los paquetes
/// aparecen como una sola fila-ancla, no como N sesiones). El orden de la
/// lista se respeta en la salida del snapshot.
/// </summary>
public interface IRelacionFacturasSelector
{
    Task<IReadOnlyList<RelacionFacturasHecho>> SelectAsync(
        RelacionFacturasFiltros filtros,
        CancellationToken ct = default);
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Una fila de datos dentro de un <see cref="FacturacionSnapshot"/>. El contenido
/// es <c>jsonb</c> porque la estructura varia por tipo — el motor no necesita
/// saber la forma para paginar, ordenar o descargar.
///
/// Ejemplo <see cref="DatosJson"/> para "Relacion de Facturas":
/// <code>
/// {
///   "Consecutivo Factura": null,
///   "Contrato": "TOL-004-26-P...",
///   "codigo habilitacion ": "730010353101",
///   ...
/// }
/// </code>
/// Los headers respetan el formato EXACTO del template EPS (incluyendo tildes rotas).
/// </summary>
public class FacturacionSnapshotFila : TenantEntity
{
    /// <summary>Snapshot al que pertenece esta fila.</summary>
    public Guid SnapshotId { get; set; }

    /// <summary>Snapshot navegacional.</summary>
    public FacturacionSnapshot Snapshot { get; set; } = null!;

    /// <summary>Numero de fila 1..N segun el orden en que el builder las emitio.</summary>
    public int NumeroFila { get; set; }

    /// <summary>Datos de la fila serializados como JSON. Se persiste como <c>jsonb</c>.</summary>
    public string DatosJson { get; set; } = "{}";

    /// <summary>
    /// Color de resaltado de la fila (hex, ej. "#fde68a") o null si no tiene. Es una
    /// marca manual persistente que el usuario pone en el detalle del snapshot para
    /// destacar filas; no afecta la facturacion ni los exports de datos.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>Observacion/nota manual de la fila (texto libre). Nota operativa para
    /// marcar problemas en ese registro; se muestra truncada con tooltip en el detalle
    /// y se puede filtrar/eliminar. No afecta la facturacion ni los exports de datos.</summary>
    public string? Observacion { get; set; }
}

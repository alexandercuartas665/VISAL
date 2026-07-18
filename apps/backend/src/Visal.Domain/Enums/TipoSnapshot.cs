namespace Visal.Domain.Enums;

/// <summary>
/// Tipos de snapshot transaccional soportados por el motor de facturacion.
/// Cada valor mapea a un <c>ISnapshotBuilder</c> distinto en la capa Application.
/// Nuevos tipos se agregan al enum + un builder — el motor no requiere cambios.
/// </summary>
public enum TipoSnapshot
{
    /// <summary>Instantanea de "Relacion de Facturas" que la EPS pide para radicar.</summary>
    RelacionFacturas = 1,

    /// <summary>Backlog: bundle RIPS oficial para MinSalud (sin datos de facturacion).</summary>
    RipsPuro = 2,

    /// <summary>Backlog: estado actual de glosas recibidas de la EPS + respuestas.</summary>
    Glosas = 3,

    /// <summary>Backlog: notas credito emitidas en el periodo.</summary>
    NotasCredito = 4
}

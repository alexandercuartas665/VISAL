using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de la "Orden de Insumos" de una Historia Clinica. Cada fila corresponde
/// a un insumo (panales, sondas, gasas, equipos descartables) entregado o
/// recomendado durante la atencion. No depende de catalogo — el profesional
/// escribe el nombre/descripcion del insumo y la cantidad.
/// </summary>
public class HistoriaClinicaInsumo : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>
    /// Numero de la orden (grupo) dentro de la misma HC. Permite tener 1..N
    /// "Ordenes de Insumos" en una misma historia: todos los items con el mismo
    /// (HistoriaClinicaId, NumeroOrden) forman una orden imprimible. Los items
    /// previos a esta columna quedan en la orden 1 (default de la migracion).
    /// El prefill hacia la HC agrega TODOS los insumos de la HC sin importar la orden.
    /// </summary>
    public int NumeroOrden { get; set; } = 1;

    public string? Codigo { get; set; }

    public string Descripcion { get; set; } = null!;

    public string? Cantidad { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>
    /// URL del formato MIPRES generado en la plataforma de MinSalud. Es un
    /// enlace opcional que el profesional pega despues de radicar el insumo
    /// no cubierto por el plan de beneficios. Sirve para trazabilidad — al
    /// abrir la orden impresa el auditor puede ver de donde viene.
    /// </summary>
    public string? MipresUrl { get; set; }

    public int Orden { get; set; }
}

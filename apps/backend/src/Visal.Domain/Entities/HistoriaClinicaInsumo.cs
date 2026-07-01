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

using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Item de una orden EXTERNA (a otra IPS/proveedor) dentro de una HC.
/// El discriminador <see cref="Tipo"/> define si es RX imagenologia,
/// laboratorio, servicio o insumo. El autocomplete usa CatalogoServicioReferencia
/// del mismo tipo.
/// </summary>
public class HistoriaClinicaOrdenExterna : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public TipoCatalogoServicio Tipo { get; set; }
    public int Orden { get; set; }
    public string? Codigo { get; set; }
    public string Descripcion { get; set; } = null!;
    public string? Cantidad { get; set; }
    public string? Observaciones { get; set; }
}

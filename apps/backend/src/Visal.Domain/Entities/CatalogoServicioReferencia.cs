using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Entrada de un catalogo de referencia (RX imagenologia, laboratorios,
/// servicios generales, insumos). Una fila por codigo dentro del tenant.
/// Cada tipo se maneja como un modulo independiente en la UI aunque
/// comparten esta tabla — el discriminador es <see cref="Tipo"/>.
/// </summary>
public class CatalogoServicioReferencia : TenantEntity
{
    public TipoCatalogoServicio Tipo { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
}

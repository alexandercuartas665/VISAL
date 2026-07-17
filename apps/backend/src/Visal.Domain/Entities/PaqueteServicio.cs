using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Detalle de un paquete comercial: los servicios que se materializan cuando
/// el paquete se aplica en /asignacion. Solo persiste <see cref="Codigo"/> y
/// <see cref="Cantidad"/> — el nombre se resuelve por JOIN a
/// <see cref="CatalogoServicioReferencia"/> via <see cref="CatalogoServicioReferenciaId"/>
/// para que renombrar el catalogo actualice tambien los paquetes.
///
/// Unicidad: (Tenant, PaqueteId, Codigo) — un mismo codigo no puede repetirse
/// en el mismo paquete (si necesitas mayor cantidad, subes Cantidad).
/// </summary>
public class PaqueteServicio : TenantEntity
{
    public Guid PaqueteId { get; set; }
    public Paquete? Paquete { get; set; }

    /// <summary>Codigo del servicio en el catalogo global. Se guarda como snapshot para
    /// no perder trazabilidad si el CatalogoServicioReferencia se desactiva o borra.</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Referencia al catalogo global. Nullable por si el servicio se agrego
    /// manualmente antes de existir en el catalogo (fallback). Al mostrar en UI, si es
    /// null se muestra "(codigo suelto)". <c>ON DELETE SET NULL</c>.</summary>
    public Guid? CatalogoServicioReferenciaId { get; set; }
    public CatalogoServicioReferencia? CatalogoServicioReferencia { get; set; }

    /// <summary>Cantidad default a materializar cuando el paquete se aplica. El
    /// coordinador puede editar esta cantidad al momento de agregar la asignacion
    /// (incluyendo bajarla a 0 para excluir un servicio opcional del paquete).</summary>
    public int Cantidad { get; set; } = 1;
}

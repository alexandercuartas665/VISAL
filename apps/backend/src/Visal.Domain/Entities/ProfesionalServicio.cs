using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Servicio del catalogo maestro (<see cref="CatalogoServicioReferencia"/>) que un
/// profesional PRESTA/atiende. Es el vinculo explicito profesional -> servicio que
/// antes se infería de forma aproximada por el tipo de profesional. Lo usa el modulo
/// de asignacion por agendas para saber que servicios del contrato de un paciente
/// puede atender un doctor con agenda (match por codigo CUPS contra
/// <c>servicios_contrato.codigo_servicio</c>).
///
/// Sigue el patron de <see cref="PaqueteServicio"/>: guarda el <see cref="Codigo"/>
/// (snapshot CUPS) y una referencia nullable al catalogo. Unicidad:
/// (Tenant, ProfesionalId, Codigo).
/// </summary>
public class ProfesionalServicio : TenantEntity
{
    public Guid ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }

    /// <summary>Codigo del servicio en el catalogo (CUPS/CUM). Snapshot para no perder
    /// trazabilidad si el catalogo se desactiva o borra. Es la llave de cruce con
    /// <c>servicios_contrato.codigo_servicio</c>.</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Nombre del servicio (snapshot para mostrar sin JOIN).</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Referencia al catalogo maestro. Nullable (ON DELETE SET NULL) por si el
    /// servicio del catalogo se borra; el Codigo/Nombre snapshot se conservan.</summary>
    public Guid? CatalogoServicioReferenciaId { get; set; }
    public CatalogoServicioReferencia? CatalogoServicioReferencia { get; set; }
}

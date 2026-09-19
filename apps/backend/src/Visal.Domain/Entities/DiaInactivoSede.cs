using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Dia marcado como INACTIVO para una sede (sucursal) concreta (Ola 3 del modulo
/// de Agendas — modulo Calendario). Una sede puede tener dias inactivos que otra
/// no (cierre por mantenimiento, jornada administrativa, etc.). Cierra el dia para
/// esa sede en la futura asignacion por agendas.
///
/// Los festivos NACIONALES no se guardan aqui: se calculan
/// (<see cref="Visal.Application"/> IFestivosColombiaService) y se muestran en el
/// calendario. Esta tabla es solo para los dias inactivos que crea el usuario.
///
/// Tenant-scoped. Unicidad: (TenantId, SucursalId, Fecha).
/// </summary>
public class DiaInactivoSede : TenantEntity
{
    /// <summary>Sede a la que aplica el dia inactivo.</summary>
    public Guid SucursalId { get; set; }

    /// <summary>Fecha inactiva.</summary>
    public DateOnly Fecha { get; set; }

    /// <summary>Motivo opcional (visible al operador). varchar(200).</summary>
    public string? Motivo { get; set; }
}

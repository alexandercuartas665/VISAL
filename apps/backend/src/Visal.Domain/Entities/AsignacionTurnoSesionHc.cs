using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Pivote muchos-a-muchos entre <see cref="AsignacionTurnoSesion"/> y
/// <see cref="HistoriaClinica"/>. Cada fila registra que una HC cubre una
/// sesion especifica de la programacion de un turno de atencion.
///
/// Reglas:
/// - Cardinalidad practica hoy: 1 HC = 1 sesion (una fila por sesion al abrir
///   la HC desde /atencion). La tabla M:N deja abierto que en el futuro una
///   misma HC pueda cubrir varias sesiones (visita larga que se factura como
///   dos turnos) sin cambiar el schema.
/// - El campo denormalizado <c>Completado</c> de <see cref="AsignacionTurnoSesion"/>
///   se recalcula en el HC service en Crear/Cerrar/Reabrir/Descartar sobre
///   las sesiones vinculadas por este pivote.
/// - Tenant-scoped: hereda TenantId de <see cref="TenantEntity"/> para respetar
///   el global query filter.
/// </summary>
public class AsignacionTurnoSesionHc : TenantEntity
{
    public Guid SesionId { get; set; }
    public AsignacionTurnoSesion? Sesion { get; set; }

    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }
}

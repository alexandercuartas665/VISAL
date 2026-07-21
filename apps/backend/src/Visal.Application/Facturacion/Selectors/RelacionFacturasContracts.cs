using Visal.Domain.Entities;

namespace Visal.Application.Facturacion.Selectors;

/// <summary>
/// Filtros aceptados por el selector de Relacion de Facturas. Aseguradora es
/// obligatoria; sucursal y rango de fecha son opcionales (si el rango viene
/// nulo se asume el mes calendario actual).
/// </summary>
public sealed record RelacionFacturasFiltros(
    Guid AseguradoraId,
    IReadOnlyList<Guid>? SucursalIds,
    DateOnly FechaInicio,
    DateOnly FechaFin);

/// <summary>
/// "Hecho facturable" resuelto por el selector — todo lo que el builder de
/// snapshot necesita para producir UNA fila del template EPS. Se compone
/// enteramente de entidades ya cargadas: el builder no vuelve a la BD.
///
/// Criterio actual (v2): 1 hecho = 1 sesion atendida (AsignacionTurnoSesion)
/// cuya asignacion apunta al contrato de la aseguradora filtrada, en el rango
/// de fecha, y con al menos una HC del paciente en estado Cerrada.
/// </summary>
public sealed record RelacionFacturasHecho(
    AsignacionTurnoSesion Sesion,
    AsignacionTurno Turno,
    Asignacion Asignacion,
    Paciente Paciente,
    ContratoAseguradora Contrato,
    Aseguradora Aseguradora,
    Sucursal? Sucursal,
    Profesional? Profesional,
    ServicioContrato? Servicio,
    Paquete? Paquete,
    /// <summary>CUPS a mostrar en la fila. Puede venir del ServicioContrato o
    /// del representativo del paquete cuando aplica.</summary>
    string? CupsCodigo,
    /// <summary>Descripcion del CUPS resuelta desde el catalogo de referencia.</summary>
    string? CupsDescripcion,
    string? DepartamentoNombre,
    string? MunicipioNombre,
    /// <summary>Nombre del pais de residencia (fallback COLOMBIA en el builder).</summary>
    string? NacionalidadNombre);

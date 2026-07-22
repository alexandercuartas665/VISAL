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
/// Criterio actual (v3): 1 hecho = 1 <see cref="HistoriaClinica"/> con
/// <c>Estado = Cerrada</c> cuya <c>fecha_cierre</c> cae en el rango. El
/// vinculo con la aseguradora se resuelve por el contrato principal del
/// paciente (<c>Paciente.Contrato1Id</c> / 2 / 3) o por <c>Paciente.AseguradoraId</c>
/// directo, en ese orden. Los campos de sesion/turno/asignacion/servicio/paquete
/// se dejan nullable — v3 no los usa, pero el record los mantiene para
/// backward-compat con builders/tests que puedan haberlos referenciado antes.
/// </summary>
public sealed record RelacionFacturasHecho(
    HistoriaClinica Hc,
    Paciente Paciente,
    ContratoAseguradora Contrato,
    Aseguradora Aseguradora,
    Sucursal? Sucursal,
    Profesional? Profesional,
    /// <summary>CUPS a mostrar en la fila. Puede venir del contrato o del
    /// FormDefinition; null si no se puede resolver.</summary>
    string? CupsCodigo,
    /// <summary>Descripcion del CUPS resuelta desde el catalogo de referencia.</summary>
    string? CupsDescripcion,
    string? DepartamentoNombre,
    string? MunicipioNombre,
    /// <summary>Nombre del pais de residencia (fallback COLOMBIA en el builder).</summary>
    string? NacionalidadNombre,
    /// <summary>Codigo de habilitacion REPS de la sede, resuelto desde
    /// InteroperabilidadCredencialSede (ambiente activo) con fallback a
    /// Sucursal.CodigoHabilitacion.</summary>
    string? CodigoHabilitacionResuelto,
    /// <summary>Tipo de archivo RIPS (AC/AP/AT/AM) tomado del catalogo de
    /// tipos de servicio segun el modulo de la asignacion asociada a la HC.</summary>
    string? TipoArchivoRips,
    /// <summary>Numero de autorizacion de la EPS, viene de la asignacion.</summary>
    string? CodigoAutorizacion);

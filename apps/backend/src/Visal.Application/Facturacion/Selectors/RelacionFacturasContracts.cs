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
    DateOnly FechaFin,
    // Opcional: filtra los pacientes cuyo nombre completo o identificacion CONTIENE
    // este texto (case-insensitive, sin tildes). Pensado para pruebas del sistema
    // — un solo paciente por snapshot. Vacio/null => no filtra.
    string? PacienteQuery = null);

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
    string? CodigoAutorizacion,
    /// <summary>Nombre del servicio prestado (Asignacion.NombreServicio).</summary>
    string? NombreServicio,
    /// <summary>Tarifa unitaria del servicio segun el contrato (ServicioContrato.Tarifa).</summary>
    decimal? ValorUnitario,
    /// <summary>Cuota moderadora pagada por el paciente (solo si Asignacion.TipoPago == CUOTA).</summary>
    decimal? ValorCuotaModeradora,
    /// <summary>Copago pagado por el paciente (solo si Asignacion.TipoPago == COPAGO).</summary>
    decimal? ValorCopago,
    /// <summary>Modalidad de atencion para facturacion (col 33), configurada en
    /// el servicio del contrato (ServicioContrato.ModalidadFacturacion).</summary>
    string? ModalidadFacturacion,
    /// <summary>Grupo de servicios para facturacion (col 35), configurado en
    /// el servicio del contrato (ServicioContrato.GrupoServicioFacturacion).</summary>
    string? GrupoServicioFacturacion,
    /// <summary>Codigo/categoria de servicio para facturacion (col 36),
    /// configurado en el servicio del contrato (ServicioContrato.ServicioFacturacion).</summary>
    string? ServicioFacturacion);

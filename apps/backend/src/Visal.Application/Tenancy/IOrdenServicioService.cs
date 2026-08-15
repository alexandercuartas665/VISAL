namespace Visal.Application.Tenancy;

/// <summary>Sugerencia del autocompletado al buscar en el catalogo de servicios
/// de los contratos de aseguradoras. Lo que se muestra al usuario es la
/// Descripcion; lo que viaja a la orden es el CodigoServicio.
///
/// Cuando el termino de busqueda coincide EXACTO con el codigo de un Paquete,
/// la busqueda antepone las N filas del detalle del paquete (marcadas con
/// <see cref="PaqueteCodigo"/> != null) para que el profesional pueda elegir
/// cuales de los servicios del paquete quiere ordenar. El resto de los
/// resultados son los del catalogo normal.</summary>
public sealed record ServicioSugerenciaDto(
    Guid Id,
    string? CodigoServicio,
    string Descripcion,
    string? Modulo,
    string? Especialidad,
    string? Contrato,
    string? Aseguradora,
    string? PaqueteCodigo = null);

/// <summary>Fila de la orden a servicios de una HC.</summary>
public sealed record OrdenServicioItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid? ServicioContratoId,
    string? CodigoServicio,
    string Descripcion,
    string? Cantidad,
    string? Observaciones,
    int Orden);

public sealed record AgregarServicioRequest(
    Guid? ServicioContratoId,
    string? CodigoServicio,
    string Descripcion,
    string? Cantidad,
    string? Observaciones);

public sealed record ActualizarServicioRequest(
    string? Cantidad,
    string? Observaciones);

/// <summary>Paquete disponible para un paciente: existe dentro de alguno de los
/// contratos del paciente (via ServicioContrato.PaqueteId). CantidadServicios es
/// cuantos servicios del paquete estan en esos contratos.</summary>
public sealed record PaquetePacienteDto(
    Guid PaqueteId,
    string Codigo,
    string Nombre,
    int CantidadServicios);

public interface IOrdenServicioService
{
    /// <summary>
    /// Busqueda case-insensitive sobre Descripcion / CodigoServicio del catalogo
    /// de servicios de contratos del tenant. Alimenta el autocompletado del
    /// input "Nombre del Servicio".
    /// </summary>
    Task<IReadOnlyList<ServicioSugerenciaDto>> BuscarSugerenciasAsync(
        string termino, int take = 12, Guid? pacienteId = null, CancellationToken ct = default);

    /// <summary>Paquetes disponibles para el paciente: los que existen dentro de
    /// sus contratos de aseguradora. Alimenta el selector de paquetes.</summary>
    Task<IReadOnlyList<PaquetePacienteDto>> ListarPaquetesDelPacienteAsync(
        Guid pacienteId, CancellationToken ct = default);

    /// <summary>Agrega a la orden TODOS los servicios de un paquete que existan en
    /// los contratos del paciente. Omite los que ya estan en la orden. Devuelve las
    /// filas nuevas creadas.</summary>
    Task<IReadOnlyList<OrdenServicioItemDto>> AgregarPaqueteAsync(
        Guid historiaId, Guid pacienteId, Guid paqueteId, Guid actor, CancellationToken ct = default);

    /// <summary>Items actuales de la orden a servicios de la historia (ordenados por Orden).</summary>
    Task<IReadOnlyList<OrdenServicioItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<OrdenServicioItemDto> AgregarAsync(
        Guid historiaId, AgregarServicioRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarServicioRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    /// <summary>Conteo rapido para el badge en la pestana del modulo.</summary>
    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

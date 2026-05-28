using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Datos del paciente seleccionado para alimentar la columna izquierda del wizard.</summary>
public sealed record PacienteAsignacionDto(
    Guid Id, string NumeroDocumento, string TipoDocumento, string NombreCompleto,
    string? Sede, string? Ciudad,
    IReadOnlyList<ContratoMiniDto> Contratos);

public sealed record ContratoMiniDto(Guid ContratoId, Guid AseguradoraId, string AseguradoraNombre, string CodigoContrato, string Estado);

/// <summary>Item del catalogo de servicios filtrado por contrato + tipo de servicio.</summary>
public sealed record ServicioCatalogoDto(
    Guid Id, string? Codigo, string Descripcion, string? Modulo, string? Especialidad, decimal? Tarifa);

/// <summary>Fila del historico (ultimos N) del paciente.</summary>
public sealed record AsignacionMiniDto(
    Guid Id, string NombreServicio, string TipoServicio, int Cantidad,
    DateOnly FechaInicio, DateOnly? FechaFinal, string Estado,
    string ContratoCodigo, DateTimeOffset CreadoEn);

/// <summary>Item del carrito que se envia al guardar el lote.</summary>
public sealed record AsignacionItemRequest(
    string ServicioId, string NombreServicio, string TipoServicio, string? Modulo,
    int Cantidad, string? CodigoAutorizacion,
    short? AnioServicio, short MesVigencia, short? MesFinal,
    DateOnly FechaInicio, DateOnly? FechaFinal,
    string? Observaciones, string? FormatoHistoria);

public sealed record CrearLoteRequest(
    Guid PacienteId, string ContratoCodigo, string Sucursal,
    IReadOnlyList<AsignacionItemRequest> Items);

public sealed record LoteCreadoDto(Guid LoteId, int CantidadServicios);

public interface IAsignacionService
{
    /// <summary>Datos del paciente + sus contratos (de su aseguradora). Devuelve null si no existe.</summary>
    Task<PacienteAsignacionDto?> GetPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Busca pacientes por documento/nombre/telefono para el modal de busqueda avanzada.</summary>
    Task<IReadOnlyList<PacienteAsignacionDto>> BuscarPacientesAsync(string? texto, Guid? contratoId, CancellationToken ct = default);

    /// <summary>Tipos de servicio disponibles para un contrato: DISTINCT de servicios_contrato.Modulo.</summary>
    Task<IReadOnlyList<string>> TiposServicioPorContratoAsync(Guid contratoId, CancellationToken ct = default);

    /// <summary>Servicios del contrato filtrados por tipo (Modulo).</summary>
    Task<IReadOnlyList<ServicioCatalogoDto>> ServiciosPorContratoAsync(Guid contratoId, string? tipo, CancellationToken ct = default);

    /// <summary>Ultimas N asignaciones del paciente (para la columna del centro).</summary>
    Task<IReadOnlyList<AsignacionMiniDto>> UltimasAsignacionesAsync(Guid pacienteId, int n, CancellationToken ct = default);

    /// <summary>Crea un lote + N asignaciones en una sola transaccion. Estado = Pendiente.</summary>
    Task<LoteCreadoDto> CrearLoteAsync(CrearLoteRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Elimina una asignacion del lote (caso "eliminar item" de la grilla).</summary>
    Task<bool> EliminarAsignacionAsync(Guid asignacionId, Guid actor, CancellationToken ct = default);
}

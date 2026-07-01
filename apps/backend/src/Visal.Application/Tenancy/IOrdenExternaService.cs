using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record OrdenExternaItemDto(
    Guid Id, int Orden, string? Codigo, string Descripcion, string? Cantidad, string? Observaciones);

public sealed record AgregarOrdenExternaRequest(
    string? Codigo, string Descripcion, string? Cantidad, string? Observaciones);

/// <summary>
/// Servicio de ordenes EXTERNAS de una HC. Un servicio para los 4 tipos
/// (RxImagenologia, Laboratorio, ServicioExterno, Insumo) — el tipo se pasa
/// como parametro en cada llamada. El autocomplete de codigo se cablea contra
/// <see cref="ICatalogoServicioService"/> con el mismo tipo.
/// </summary>
public interface IOrdenExternaService
{
    Task<IReadOnlyList<OrdenExternaItemDto>> ListarPorHistoriaAsync(
        Guid historiaClinicaId, TipoCatalogoServicio tipo, CancellationToken ct = default);

    Task<OrdenExternaItemDto> AgregarAsync(
        Guid historiaClinicaId, TipoCatalogoServicio tipo,
        AgregarOrdenExternaRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actorUserId, CancellationToken ct = default);
}

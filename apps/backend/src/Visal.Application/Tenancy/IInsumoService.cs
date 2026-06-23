namespace Visal.Application.Tenancy;

public sealed record InsumoItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    string? Codigo,
    string Descripcion,
    string? Cantidad,
    string? Observaciones,
    int Orden);

public sealed record AgregarInsumoRequest(
    string? Codigo,
    string Descripcion,
    string? Cantidad,
    string? Observaciones);

public sealed record ActualizarInsumoRequest(
    string? Cantidad,
    string? Observaciones);

public interface IInsumoService
{
    Task<IReadOnlyList<InsumoItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<InsumoItemDto> AgregarAsync(
        Guid historiaId, AgregarInsumoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarInsumoRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

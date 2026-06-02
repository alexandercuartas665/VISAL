namespace Visal.Application.Tenancy;

public sealed record IncapacidadItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    string Motivo,
    DateOnly? FechaDesde,
    DateOnly? FechaHasta,
    int? Dias,
    string? Tipo,
    int Orden);

public sealed record AgregarIncapacidadRequest(
    string Motivo,
    DateOnly? FechaDesde,
    DateOnly? FechaHasta,
    int? Dias,
    string? Tipo);

public sealed record ActualizarIncapacidadRequest(
    string Motivo,
    DateOnly? FechaDesde,
    DateOnly? FechaHasta,
    int? Dias,
    string? Tipo);

public interface IIncapacidadService
{
    Task<IReadOnlyList<IncapacidadItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<IncapacidadItemDto> AgregarAsync(
        Guid historiaId, AgregarIncapacidadRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarIncapacidadRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

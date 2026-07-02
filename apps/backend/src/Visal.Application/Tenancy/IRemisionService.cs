namespace Visal.Application.Tenancy;

public sealed record RemisionItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    string? EspecialidadCodigo,
    string EspecialidadNombre,
    string? Cantidad,
    string? Motivo,
    int Orden);

public sealed record AgregarRemisionRequest(
    string? EspecialidadCodigo,
    string EspecialidadNombre,
    string? Cantidad,
    string? Motivo);

public interface IRemisionService
{
    Task<IReadOnlyList<RemisionItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<RemisionItemDto> AgregarAsync(
        Guid historiaId, AgregarRemisionRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

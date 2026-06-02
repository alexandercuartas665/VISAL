namespace Visal.Application.Tenancy;

public sealed record CertificacionItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    string Titulo,
    string Contenido,
    int Orden);

public sealed record AgregarCertificacionRequest(
    string Titulo,
    string Contenido);

public sealed record ActualizarCertificacionRequest(
    string Titulo,
    string Contenido);

public interface ICertificacionService
{
    Task<IReadOnlyList<CertificacionItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<CertificacionItemDto> AgregarAsync(
        Guid historiaId, AgregarCertificacionRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> ActualizarAsync(
        Guid itemId, ActualizarCertificacionRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

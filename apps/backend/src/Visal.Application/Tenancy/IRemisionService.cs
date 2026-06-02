namespace Visal.Application.Tenancy;

public sealed record RemisionItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    string Capitulo,
    string? EspecialidadCodigo,
    string EspecialidadNombre,
    string? Motivo,
    int Orden);

public sealed record CapituloCupDto(string Nombre, int Total);

public sealed record EspecialidadCupDto(Guid CupId, string? Codigo, string Nombre);

public sealed record AgregarRemisionRequest(
    string Capitulo,
    string? EspecialidadCodigo,
    string EspecialidadNombre,
    string? Motivo);

public interface IRemisionService
{
    Task<IReadOnlyList<RemisionItemDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    Task<IReadOnlyList<CapituloCupDto>> ListarCapitulosAsync(CancellationToken ct = default);

    /// <summary>Devuelve hasta 'take' CUPS cuyo Nombre contiene el termino y cuyo capitulo
    /// (Cup.Descripcion) coincide con el filtro. Si capitulo es null/vacio, no filtra por capitulo.</summary>
    Task<IReadOnlyList<EspecialidadCupDto>> BuscarEspecialidadesAsync(
        string? capitulo, string? termino, int take = 30, CancellationToken ct = default);

    Task<RemisionItemDto> AgregarAsync(
        Guid historiaId, AgregarRemisionRequest req, Guid actor, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid itemId, Guid actor, CancellationToken ct = default);

    Task<int> ContarPorHistoriaAsync(Guid historiaId, CancellationToken ct = default);
}

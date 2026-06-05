namespace Visal.Application.Tenancy;

public sealed record TipologiaArchivoDto(
    Guid Id, string Nombre, string Color, bool Activo, DateTimeOffset CreatedAt);

public sealed record SaveTipologiaArchivoRequest(
    Guid? Id, string Nombre, string Color, bool Activo);

/// <summary>
/// Catalogo de tipologias (conceptos) que el tenant le da a los documentos que se
/// adjuntan a notas medicas. Configurable desde /cfg-tipologia-archivos. Lo consume
/// el dropdown "Categoria" del tab Documentos Externos del modulo Notas Medicas.
/// </summary>
public interface ITipologiaArchivoService
{
    Task<IReadOnlyList<TipologiaArchivoDto>> ListAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<TipologiaArchivoDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TipologiaArchivoDto?> SaveAsync(SaveTipologiaArchivoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Si el tenant no tiene ninguna tipologia siembra las 5 default
    /// (Lista de firmas, Escala, Formato, Examen, Otros) para no quedarse vacio.</summary>
    Task<int> SeedDefaultsAsync(CancellationToken ct = default);
}

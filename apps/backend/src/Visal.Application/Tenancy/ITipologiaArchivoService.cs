namespace Visal.Application.Tenancy;

/// <summary>Subcategoria (segundo nivel) de una tipologia.</summary>
public sealed record SubcategoriaArchivoDto(
    Guid Id, Guid TipologiaArchivoId, string Nombre, bool Activo);

public sealed record TipologiaArchivoDto(
    Guid Id, string Nombre, string Color, bool Activo, DateTimeOffset CreatedAt,
    IReadOnlyList<SubcategoriaArchivoDto> Subcategorias);

public sealed record SaveTipologiaArchivoRequest(
    Guid? Id, string Nombre, string Color, bool Activo);

public sealed record SaveSubcategoriaArchivoRequest(
    Guid? Id, Guid TipologiaArchivoId, string Nombre, bool Activo);

/// <summary>
/// Catalogo de tipologias (conceptos) que el tenant le da a los documentos que se
/// adjuntan a notas medicas, con sus subcategorias (segundo nivel). Configurable desde
/// /cfg-tipologia-archivos. Lo consume el dropdown "Categoria" (+ "Subcategoria") del tab
/// Documentos Externos del modulo Notas Medicas.
/// </summary>
public interface ITipologiaArchivoService
{
    /// <summary>Tipologias con sus subcategorias. Si <paramref name="soloActivos"/> es true,
    /// solo tipologias activas y, dentro de cada una, solo subcategorias activas.</summary>
    Task<IReadOnlyList<TipologiaArchivoDto>> ListAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<TipologiaArchivoDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TipologiaArchivoDto?> SaveAsync(SaveTipologiaArchivoRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Crea/actualiza una subcategoria de una tipologia.</summary>
    Task<SubcategoriaArchivoDto?> SaveSubcategoriaAsync(SaveSubcategoriaArchivoRequest req, Guid actor, CancellationToken ct = default);
    /// <summary>Borra una subcategoria por id.</summary>
    Task<bool> DeleteSubcategoriaAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Si el tenant no tiene ninguna tipologia siembra las default para no quedarse vacio.</summary>
    Task<int> SeedDefaultsAsync(CancellationToken ct = default);
}

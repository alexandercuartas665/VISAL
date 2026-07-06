namespace Visal.Application.Tenancy;

/// <summary>Item de catalogo simple (tipo profesional o subcategoria).</summary>
public sealed record CatalogItemDto(Guid Id, string Nombre, bool Activo, int Orden);

public sealed record ProfesionalDto(
    Guid Id, string NumeroDocumento, string NombreCompleto, string? TipoProfesional, string? Ciudad, string? RegistroMedico);

/// <summary>
/// Item plano para la vista tabla del modulo profesionales — incluye sedes,
/// rol asignado, si tiene firma y tipo profesional. No lleva la URL de la
/// firma (solo un boolean para la columna "Firma"), por lo que se puede
/// serializar sin exponer la ruta al PNG.
/// </summary>
public sealed record ProfesionalTablaDto(
    Guid Id,
    string NombreCompleto,
    string TipoDocumento,
    string NumeroDocumento,
    string? TipoProfesional,
    string? Rol,
    IReadOnlyList<string> Sedes,
    bool TieneFirma);

public sealed record ProfesionalDetailDto(
    Guid Id, string NumeroDocumento, string TipoDocumento, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido, string NombreCompleto, Guid? TipoProfesionalId,
    string? RegistroMedico, string? Ciudad, string? Celular, string? FirmaUrl,
    IReadOnlyList<Guid> SubCategoriaIds, IReadOnlyList<string> Agencias,
    /// <summary>Rol de acceso predeterminado. Al guardar el profesional se aplica
    /// al TenantUser vinculado (si existe). Null = no forzar.</summary>
    Guid? RolPredeterminadoId);

public sealed record SaveProfesionalRequest(
    Guid? Id, string NumeroDocumento, string TipoDocumento, string? PrimerNombre, string? SegundoNombre,
    string? PrimerApellido, string? SegundoApellido, string? NombreCompleto, Guid? TipoProfesionalId,
    string? RegistroMedico, string? Ciudad, string? Celular, string? FirmaUrl,
    IReadOnlyList<Guid> SubCategoriaIds, IReadOnlyList<string> Agencias,
    /// <summary>Rol de acceso predeterminado. Al guardar se persiste en el profesional
    /// y ademas se propaga al TenantUser vinculado (si existe) junto con las sedes.</summary>
    Guid? RolPredeterminadoId = null);

/// <summary>Configuracion de profesionales y sus catalogos (tipo profesional, subcategorias). Tenant-scoped.</summary>
public interface IProfesionalConfigService
{
    // Catalogos
    Task<IReadOnlyList<CatalogItemDto>> ListTiposAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<CatalogItemDto?> SaveTipoAsync(Guid? id, string nombre, bool activo, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteTipoAsync(Guid id, Guid actor, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogItemDto>> ListSubcategoriasAsync(bool soloActivos = false, CancellationToken ct = default);
    Task<CatalogItemDto?> SaveSubcategoriaAsync(Guid? id, string nombre, bool activo, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteSubcategoriaAsync(Guid id, Guid actor, CancellationToken ct = default);

    // Profesionales
    Task<IReadOnlyList<ProfesionalDto>> ListProfesionalesAsync(string? filtro, CancellationToken ct = default);

    /// <summary>Lista completa de profesionales con sedes + rol + tipo + si tiene firma. Para vista tabla.</summary>
    Task<IReadOnlyList<ProfesionalTablaDto>> ListProfesionalesTablaAsync(CancellationToken ct = default);
    Task<ProfesionalDetailDto?> GetProfesionalAsync(Guid id, CancellationToken ct = default);
    Task<ProfesionalDetailDto?> SaveProfesionalAsync(SaveProfesionalRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteProfesionalAsync(Guid id, Guid actor, CancellationToken ct = default);
}

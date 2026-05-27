namespace Visal.Application.Tenancy;

/// <summary>Resumen de un formulario para listados.</summary>
public sealed record FormDefinitionDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    DateTimeOffset? UpdatedAt);

/// <summary>Detalle completo (incluye el esquema JSON del disenador).</summary>
public sealed record FormDefinitionDetailDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    string SchemaJson);

/// <summary>Alta o actualizacion. Si <see cref="Id"/> es null se crea; si no, se actualiza.</summary>
public sealed record SaveFormDefinitionRequest(
    Guid? Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    string SchemaJson,
    bool Activo);

/// <summary>Gestion de definiciones de formularios (Motor de Formularios, 2.M10), tenant-scoped.</summary>
public interface IFormDefinitionService
{
    Task<IReadOnlyList<FormDefinitionDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<FormDefinitionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FormDefinitionDetailDto?> SaveAsync(SaveFormDefinitionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

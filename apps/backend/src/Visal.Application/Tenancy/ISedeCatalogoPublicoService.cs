namespace Visal.Application.Tenancy;

/// <summary>
/// Servicio sin autenticacion que lista las sedes activas (a traves de todos los tenants)
/// para alimentar el dropdown del login. No expone datos sensibles: solo id, nombre y ciudad.
/// </summary>
public interface ISedeCatalogoPublicoService
{
    Task<IReadOnlyList<SedePublicaDto>> ListAsync(CancellationToken ct = default);
}

public sealed record SedePublicaDto(Guid Id, string Nombre, string? Ciudad);

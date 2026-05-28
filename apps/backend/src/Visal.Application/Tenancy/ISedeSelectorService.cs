namespace Visal.Application.Tenancy;

/// <summary>
/// Servicio que lista las sedes a las que un usuario tiene acceso dentro de un tenant.
/// Usado en el flujo de login tras elegir empresa: si el usuario tiene varias sedes,
/// se le presenta el selector; si tiene una, entra directo a ella; si es global y no tiene
/// sedes asignadas, ve todas las sedes activas del tenant.
/// </summary>
public interface ISedeSelectorService
{
    Task<IReadOnlyList<SucursalDto>> GetSedesAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default);

    /// <summary>True si la sede pertenece al tenant activo y el usuario puede entrar (miembro o global).</summary>
    Task<bool> PuedeAccederAsync(Guid platformUserId, Guid tenantId, Guid sucursalId, CancellationToken ct = default);
}

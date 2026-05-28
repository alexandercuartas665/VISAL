namespace Visal.Application.Tenancy;

/// <summary>
/// Servicio que resuelve las empresas (tenants) disponibles para un usuario tras el login,
/// para que pueda escoger sobre cual operar. Devuelve memberships activos y, si el usuario
/// es global, todos los tenants activos del SaaS.
/// </summary>
public interface IEmpresaSelectorService
{
    Task<IReadOnlyList<EmpresaOpcionDto>> GetOpcionesAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el TenantRole con el que el usuario debe entrar al tenant indicado.
    /// Si tiene membership real -> usa ese rol. Si es global y NO tiene membership -> Owner (acceso total).
    /// Retorna null si el usuario no puede acceder a ese tenant.
    /// </summary>
    Task<EmpresaSeleccionResultado?> ResolverAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default);
}

public sealed record EmpresaOpcionDto(Guid TenantId, string Nombre, string? LegalName, bool EsMiembro, bool EsGlobalAccess);
public sealed record EmpresaSeleccionResultado(Guid TenantId, string TenantRole, bool EsGlobalAccess);

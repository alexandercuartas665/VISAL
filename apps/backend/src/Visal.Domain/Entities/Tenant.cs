using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>Agencia turistica cliente del SaaS. Entidad global administrada por el Super Admin.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }

    /// <summary>Ruta del logo de la agencia (subido por el cliente), p.ej. /uploads/tenant-{id}.png.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Lema corto de la agencia (p.ej. "Salud Domiciliaria"). Lo edita el admin del tenant
    /// desde Mi Cuenta y aparece bajo el nombre en el sidebar. Si es nulo el sidebar muestra
    /// el lema por defecto de la plataforma.
    /// </summary>
    public string? Slogan { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public TenantKind Kind { get; set; } = TenantKind.Standard;
}

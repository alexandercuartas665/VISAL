namespace Visal.Application.Tenancy;

/// <summary>
/// Servicio sin autenticacion que expone el branding (logo + nombre) del tenant
/// principal de la instancia. Se usa en la pantalla de /login para mostrar la
/// identidad de la agencia cuando el branding de plataforma no esta configurado.
///
/// Heuristica: si hay un unico tenant activo en la BD se devuelve su logo;
/// si no hay ninguno o hay multiples, devuelve null y el login cae al branding
/// de plataforma. Esto cubre el caso comun de instalacion on-premise donde
/// hay una sola agencia operando.
/// </summary>
public interface ITenantBrandingPublicoService
{
    Task<TenantBrandingPublicoDto?> GetDefaultAsync(CancellationToken ct = default);
}

public sealed record TenantBrandingPublicoDto(string Name, string? LogoUrl);

namespace Visal.Application.Tenancy;

/// <summary>
/// Resuelve las URLs de las firmas (paciente y profesional) que el motor de
/// formularios necesita para aplicar las rutas de prefill cuyo sourceModule
/// es "firmaPaciente" o "firmaProfesional". Se separa en un servicio propio
/// para no inflar PacientePrefillHelper (que es estatico) ni acoplar el motor
/// de formularios al DbContext directamente.
/// </summary>
public interface IFirmaResolverService
{
    /// <summary>Devuelve la URL servible del PNG de la firma mas reciente del paciente
    /// (capturada por WhatsApp o subida manualmente como documento externo con
    /// categoria "Firma del Paciente"). Null si no existe.</summary>
    Task<string?> ResolverFirmaPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Devuelve la URL de la firma del profesional logueado: lee
    /// TenantUser.ProfesionalId y luego Profesional.FirmaUrl. Null si el usuario
    /// no tiene profesional vinculado o el profesional no tiene firma cargada.</summary>
    Task<string?> ResolverFirmaProfesionalAsync(Guid tenantUserId, CancellationToken ct = default);

    /// <summary>Variante directa cuando el caller ya conoce el ProfesionalId
    /// (por ejemplo desde el claim "profesional_id" del usuario logueado).
    /// Evita el lookup adicional a TenantUser. Null si no hay firma cargada.</summary>
    Task<string?> ResolverFirmaPorProfesionalAsync(Guid profesionalId, CancellationToken ct = default);

    /// <summary>Resuelve la firma del usuario logueado a partir del
    /// PlatformUserId (claim NameIdentifier) + TenantId (claim "tenant_id").
    /// Util para administradores que no llevan el claim "profesional_id"
    /// pero igual tienen un Profesional vinculado en su TenantUser. Hace el
    /// join completo: TenantUser by (platform_user_id, tenant_id) ->
    /// ProfesionalId -> Profesional.FirmaUrl.</summary>
    Task<string?> ResolverFirmaProfesionalPorPlatformUserAsync(Guid platformUserId, Guid tenantId, CancellationToken ct = default);
}

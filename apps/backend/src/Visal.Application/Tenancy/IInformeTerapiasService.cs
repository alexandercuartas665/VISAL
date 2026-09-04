namespace Visal.Application.Tenancy;

/// <summary>Fila del informe de pacientes pendientes por terapias.</summary>
public sealed record TerapiaPendienteDto(
    string Paciente,
    string Documento,
    string Servicio,
    int SesionesPendientes,
    DateOnly? UltimaAtencion,
    string Profesional);

/// <summary>Informe listo para mostrar en la pagina publica del enlace.</summary>
public sealed record InformeTerapiasResult(
    string TenantNombre,
    string? LogoUrl,
    DateOnly Fecha,
    IReadOnlyList<TerapiaPendienteDto> Filas);

/// <summary>
/// Genera un enlace publico (token cifrado con vencimiento) que muestra un
/// pequeno informe de los pacientes con terapias pendientes del tenant. Se usa
/// desde el modulo de Alertas/Notificaciones: el enlace se comparte por
/// WhatsApp/correo y el destinatario lo abre sin iniciar sesion.
/// </summary>
public interface IInformeTerapiasService
{
    /// <summary>URL absoluta del informe. Si <paramref name="tenantId"/> es null usa el tenant activo.</summary>
    string GenerarEnlace(string baseUri, Guid? tenantId = null, int diasValidez = 30);

    /// <summary>Valida el token del enlace y devuelve el tenant si es valido y no vencido.</summary>
    Guid? ValidarToken(string token);

    /// <summary>Arma el informe para un tenant (via token, sin sesion). Null si el tenant no existe.</summary>
    Task<InformeTerapiasResult?> ObtenerAsync(Guid tenantId, CancellationToken ct = default);
}

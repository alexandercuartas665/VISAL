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

/// <summary>Datos decodificados del token del enlace: tenant y, opcionalmente, el
/// profesional al que se acota el informe (cuando el enlace se genero para un
/// profesional puntual, ej. la auto-respuesta de la alerta a ese profesional).</summary>
public sealed record InformeTokenInfo(Guid TenantId, Guid? ProfesionalId);

/// <summary>
/// Genera un enlace publico (token cifrado con vencimiento) que muestra un
/// pequeno informe de los pacientes con terapias pendientes del tenant. Se usa
/// desde el modulo de Alertas/Notificaciones: el enlace se comparte por
/// WhatsApp/correo y el destinatario lo abre sin iniciar sesion.
/// </summary>
public interface IInformeTerapiasService
{
    /// <summary>URL absoluta del informe. Si <paramref name="tenantId"/> es null usa el
    /// tenant activo. Si <paramref name="profesionalId"/> viene, el informe se acota a
    /// las terapias pendientes de ESE profesional.</summary>
    string GenerarEnlace(string baseUri, Guid? tenantId = null, Guid? profesionalId = null, int diasValidez = 30);

    /// <summary>Valida el token del enlace y devuelve tenant (+ profesional si aplica) si
    /// es valido y no vencido. Null si invalido/expirado.</summary>
    InformeTokenInfo? ValidarToken(string token);

    /// <summary>Arma el informe para un tenant (via token, sin sesion). Si
    /// <paramref name="profesionalId"/> viene, filtra a ese profesional. Null si el
    /// tenant no existe.</summary>
    Task<InformeTerapiasResult?> ObtenerAsync(Guid tenantId, Guid? profesionalId = null, CancellationToken ct = default);
}

namespace Visal.Application.Tenancy;

/// <summary>
/// Reintenta, para el tenant activo, los envios de RDA que fallaron por una causa
/// TRANSITORIA (red/5xx/timeout, o rechazo por servicio de MinSalud indisponible como
/// EVOL). Respeta el intervalo y el maximo de intentos configurados en
/// <c>InteroperabilidadConfig</c>. Lo invoca el worker de reintentos por cada tenant.
/// </summary>
public interface IRdaReintentoService
{
    /// <summary>Reintenta los eventos elegibles del tenant activo. Devuelve cuantos reintento.</summary>
    Task<int> ReintentarPendientesAsync(CancellationToken ct = default);
}

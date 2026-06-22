using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// DTO de configuracion global del API IHCE para mostrar en pantalla. Los secretos
/// nunca viajan en claro: se exponen flags <c>TieneApimSandbox</c> / <c>TieneApimProduccion</c>
/// que indican si ya hay un valor cifrado guardado.
/// </summary>
public sealed record InteroperabilidadConfigDto(
    string? EndpointSandbox,
    string? EndpointProduccion,
    string? AzureTenantId,
    string? Scope,
    bool TieneApimSandbox,
    bool TieneApimProduccion,
    AmbienteIhce AmbienteActivo,
    string PathEnvioRda,
    string PathEnvioRdaConsulta,
    string PathConsultarPaciente,
    string PathConsultarProfesional);

/// <summary>
/// Payload para guardar la config general. Los secretos son opcionales: si vienen
/// null/empty/igual a la mascara, no se tocan; si vienen con valor nuevo, se cifran y reemplazan.
/// </summary>
public sealed record InteroperabilidadConfigSaveRequest(
    string? EndpointSandbox,
    string? EndpointProduccion,
    string? AzureTenantId,
    string? Scope,
    string? ApimSubskeySandboxNueva,
    string? ApimSubskeyProduccionNueva,
    AmbienteIhce AmbienteActivo,
    string? PathEnvioRda,
    string? PathEnvioRdaConsulta,
    string? PathConsultarPaciente,
    string? PathConsultarProfesional);

/// <summary>
/// Fila del grid de credenciales por sede + ambiente. <c>TieneClientSecret</c> indica
/// si ya hay un secret cifrado guardado (para el UI mostrar "**** configurado").
/// </summary>
public sealed record InteroperabilidadCredencialSedeDto(
    Guid Id,
    Guid SucursalId,
    string SucursalNombre,
    AmbienteIhce Ambiente,
    string? CodigoHabilitacion,
    string? NombreLlave,
    string? ClientId,
    bool TieneClientSecret,
    DateTimeOffset? FechaExpiracion);

/// <summary>
/// Payload para crear/actualizar una credencial de sede. El <c>ClientSecretNuevo</c>
/// solo se aplica si llega con valor; si viene null/empty no se modifica el cifrado actual.
/// </summary>
public sealed record InteroperabilidadCredencialSedeSaveRequest(
    Guid SucursalId,
    AmbienteIhce Ambiente,
    string? CodigoHabilitacion,
    string? NombreLlave,
    string? ClientId,
    string? ClientSecretNuevo,
    DateTimeOffset? FechaExpiracion);

/// <summary>
/// Resultado de la prueba de conexion OAuth2 contra Azure AD para una credencial de sede.
/// El AccessToken NUNCA se devuelve completo — solo un fragmento (los primeros 16 chars)
/// para confirmar que el flujo funciono.
/// </summary>
public sealed record ProbarConexionResultado(
    bool Exito,
    string Mensaje,
    string? TokenFragment,
    int? ExpiraEnSegundos,
    string? TokenType,
    int? HttpStatus,
    string? AzureErrorCode);

public interface IInteroperabilidadConfigService
{
    /// <summary>Devuelve la config global del tenant (o null si nunca se configuro).</summary>
    Task<InteroperabilidadConfigDto?> GetConfigAsync(CancellationToken ct = default);

    /// <summary>Crea o actualiza la config global del tenant.</summary>
    Task<InteroperabilidadConfigDto> SaveConfigAsync(InteroperabilidadConfigSaveRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Lista las credenciales por sede + ambiente del tenant.</summary>
    Task<IReadOnlyList<InteroperabilidadCredencialSedeDto>> ListarCredencialesAsync(CancellationToken ct = default);

    /// <summary>Crea o actualiza la credencial para una (sede, ambiente).</summary>
    Task<InteroperabilidadCredencialSedeDto> GuardarCredencialAsync(InteroperabilidadCredencialSedeSaveRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Borra la credencial de una (sede, ambiente).</summary>
    Task<bool> EliminarCredencialAsync(Guid credencialId, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Prueba la conexion OAuth2 client_credentials contra Azure AD usando la credencial
    /// de la sede indicada. NO llama al API IHCE — solo obtiene un token Bearer del
    /// directorio Azure AD del MinSalud (login.microsoftonline.com). Devuelve un fragmento
    /// del token + tiempo de expiracion para confirmar exito, o el codigo de error de Azure.
    /// </summary>
    Task<ProbarConexionResultado> ProbarConexionAsync(Guid credencialId, CancellationToken ct = default);
}

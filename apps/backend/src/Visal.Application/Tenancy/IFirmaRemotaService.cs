using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Solicitud de firma remota lista para enviarse por WhatsApp.</summary>
public sealed record FirmaRequestDto(
    Guid Id,
    Guid PacienteId,
    /// <summary>Nota a la que pertenece la firma. NULL para firmas "libres" pedidas desde
    /// el panel WhatsApp del paciente (HC, /pacientes) sin nota especifica.</summary>
    Guid? NotaMedicaId,
    string Token,
    string Telefono,
    string? NombreContacto,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    FirmaRequestStatus Status,
    /// <summary>URL publica relativa, ej. /firma/abc123. El frontend la concatena con
    /// el origen actual para mostrarla al profesional o enviarla por WhatsApp.</summary>
    string PublicPath,
    /// <summary>Contacto de emergencia que firma. NULL cuando el firmante es el paciente.</summary>
    Guid? ContactoEmergenciaId = null);

/// <summary>Un destinatario del envio de firma: puede ser el paciente mismo o un
/// contacto de emergencia (pariente). Si ContactoEmergenciaId es null, el
/// firmante es el paciente y el telefono/nombre son los del paciente.</summary>
public sealed record FirmaDestinatarioSpec(
    Guid? ContactoEmergenciaId,
    string Telefono,
    string? NombreContacto);

/// <summary>Estado actual de una solicitud, usado para polling.</summary>
public sealed record FirmaRequestStateDto(
    Guid Id,
    FirmaRequestStatus Status,
    DateTimeOffset? CompletedAt,
    /// <summary>Si la firma ya llego, viene aqui para que el frontend la dibuje
    /// en el canvas del modulo de Notas Medicas.</summary>
    string? ImageDataUrl);

/// <summary>Vista de la solicitud que se sirve a la pagina publica /firma/{token}.</summary>
public sealed record FirmaRequestPublicDto(
    Guid Id,
    string Token,
    string NombrePaciente,
    string? NombreProfesional,
    string? NombreTenant,
    DateTimeOffset ExpiresAt,
    FirmaRequestStatus Status,
    /// <summary>Nombre del firmante real (paciente o pariente). Igual a
    /// NombrePaciente cuando ContactoEmergenciaId es null.</summary>
    string? NombreSignatario = null,
    /// <summary>Rol del firmante: "PACIENTE" o el parentesco del contacto
    /// (MADRE, PADRE, ACUDIENTE, etc). Se muestra como "Firmo como: {rol}".</summary>
    string? RolSignatario = null,
    /// <summary>URL del logo del tenant (tenants.logo_url). Si viene con valor,
    /// la pagina publica lo pinta arriba del nombre. Null si el tenant no cargo logo.</summary>
    string? LogoTenantUrl = null);

/// <summary>
/// Servicio de solicitud y captura de firma remota del paciente. Genera un token
/// publico, envia el link por WhatsApp via IChatService, y al recibir la firma la
/// persiste en NotaMedica.FirmaPacienteDataUrl. Politica acordada con el negocio:
/// link vence en 2 horas, solo una solicitud activa por nota.
/// </summary>
public interface IFirmaRemotaService
{
    /// <summary>Crea (o reutiliza) la solicitud activa de la nota. Si ya hay una
    /// pendiente sin expirar, la devuelve tal cual; si no, crea una nueva.</summary>
    Task<FirmaRequestDto?> CrearOReutilizarAsync(Guid notaMedicaId, Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default);

    /// <summary>Crea (o reutiliza) la solicitud activa "libre" para un paciente,
    /// sin asociar a una nota especifica. Usado por el boton "Solicitar firma"
    /// del panel WhatsApp en HC, Notas o Pacientes. La firma capturada se
    /// archiva en FirmaPacienteRequest.ImageDataUrl pero NO actualiza ninguna
    /// nota; el operador puede consultarla luego desde el historial del paciente.</summary>
    Task<FirmaRequestDto?> CrearLibreParaPacienteAsync(Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default);

    /// <summary>Crea (o reutiliza) solicitudes libres para varios destinatarios en
    /// un solo lote. Cada destinatario (paciente o pariente) obtiene su propio
    /// token/URL. Reutiliza solicitudes pendientes vigentes por destinatario si
    /// existen. Firmas de pariente se persisten luego en PacienteContactoEmergencia.FirmaUrl.</summary>
    Task<IReadOnlyList<FirmaRequestDto>> CrearMultipleParaPacienteAsync(
        Guid pacienteId,
        IReadOnlyList<FirmaDestinatarioSpec> destinatarios,
        Guid actorTenantUserId,
        CancellationToken ct = default);

    /// <summary>Devuelve la solicitud activa "libre" del paciente (sin nota asociada),
    /// para refrescar el estado del boton en el panel WhatsApp.</summary>
    Task<FirmaRequestDto?> ObtenerActivaLibrePorPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Devuelve TODAS las solicitudes activas (libres, sin nota) del paciente:
    /// las del paciente mismo Y las de sus contactos de emergencia. Sirve para
    /// pintar en el modal "Solicitar firmas" un badge por destinatario indicando
    /// si ya firmo, esta pendiente o esta expirada — el doctor entonces no reenvia
    /// solicitudes innecesarias.</summary>
    Task<IReadOnlyList<FirmaRequestDto>> ListarActivasLibresPorPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Envia el link de la solicitud al paciente por WhatsApp via la
    /// linea elegida. Devuelve el resultado del envio (Ok/Error + texto del mensaje).</summary>
    Task<ChatSendResult> EnviarPorWhatsAppAsync(Guid solicitudId, Guid lineaId, string urlAbsoluta, Guid actorTenantUserId, CancellationToken ct = default);

    /// <summary>Auto-responde con el link de firma cuando el destinatario responde al
    /// Quick Reply de la plantilla HSM. Se dispara desde la ingesta del webhook al
    /// recibir un Inbound tipo boton o con keyword afirmativo. Sin intervencion del
    /// operador: la ventana de sesion ya esta abierta (el paciente acaba de escribir),
    /// asi que solo mandamos el link como texto. Idempotente: no re-envia si acabamos
    /// de mandarlo en los ultimos 30 segundos.
    ///
    /// Devuelve el numero de solicitudes que dispararon envio (0 si no habia
    /// pendientes para ese telefono, o si fue idempotente).</summary>
    Task<int> AutoResponderConLinkAsync(
        Guid tenantId,
        string telefonoDigits,
        Guid lineaId,
        string baseUri,
        CancellationToken ct = default);

    /// <summary>Cancela la solicitud activa (el link queda invalido). El profesional
    /// puede crear otra despues.</summary>
    Task<bool> CancelarAsync(Guid solicitudId, Guid actorTenantUserId, CancellationToken ct = default);

    /// <summary>Estado actual de la solicitud para polling desde el modulo de Notas.</summary>
    Task<FirmaRequestStateDto?> ObtenerEstadoAsync(Guid solicitudId, CancellationToken ct = default);

    /// <summary>Devuelve la solicitud activa (Pendiente o Completada) para una nota,
    /// o null si no existe ninguna. Usado para mostrar el estado al cargar el tab Firma.</summary>
    Task<FirmaRequestDto?> ObtenerActivaPorNotaAsync(Guid notaMedicaId, CancellationToken ct = default);

    // ===== Operaciones publicas (sin tenant context, validan por token) =====

    /// <summary>Obtiene la solicitud por token publico para mostrar la pagina /firma/{token}.
    /// Si esta expirada la marca como tal antes de devolverla. Devuelve null si no existe.</summary>
    Task<FirmaRequestPublicDto?> ObtenerPorTokenPublicoAsync(string token, CancellationToken ct = default);

    /// <summary>Guarda la firma capturada por el paciente. Valida que la solicitud este
    /// Pendiente y no expirada. Persiste la firma en NotaMedica.FirmaPacienteDataUrl y
    /// marca la solicitud como Completada.</summary>
    Task<bool> GuardarFirmaPorTokenAsync(string token, string imageDataUrl, CancellationToken ct = default);
}

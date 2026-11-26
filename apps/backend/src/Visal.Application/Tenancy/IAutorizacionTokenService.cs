using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>Concepto/categoria configurable para los tokens.</summary>
public sealed record AutorizacionConceptoDto(Guid Id, string Nombre, int Orden, bool Activo);

/// <summary>Fila para el listado del modulo /autorizaciones.</summary>
public sealed record AutorizacionTokenDto(
    Guid Id,
    string Codigo,
    string? ConceptoNombre,
    string Observacion,
    string? GeneradoPorNombre,
    DateTimeOffset GeneradoEn,
    DateTimeOffset ExpiresAt,
    AutorizacionTokenEstado Estado,
    bool Vencido,
    DateTimeOffset? UsadoEn,
    string? UsadoPorNombre,
    Guid? HistoriaClinicaId,
    long? HistoriaClinicaConsecutivo,
    Guid? AsignacionId,
    long? AsignacionConsecutivo,
    string? MotivoUso,
    // IMP-71: la observacion tiene version enriquecida (se carga aparte con ObtenerObservacionHtmlAsync).
    bool ObservacionRica = false);

/// <summary>Fila del tab "Solicitudes" de /autorizaciones (solicitudes de cierre fuera del
/// patron que hacen los profesionales).</summary>
public sealed record AutorizacionSolicitudDto(
    Guid Id,
    AutorizacionSolicitudEstado Estado,
    DateTimeOffset SolicitadoEn,
    string? SolicitadoPorNombre,
    Guid HistoriaClinicaId,
    long? HistoriaClinicaConsecutivo,
    Guid? AsignacionId,
    long? AsignacionConsecutivo,
    string? PacienteNombre,
    string? PacienteDocumento,
    string? ServicioNombre,
    string? TipoServicio,
    DateTimeOffset FechaAtencion,
    string? Detalle,
    string? ConceptoNombre,
    string Motivo,
    DateTimeOffset? ResueltoEn,
    string? ResueltoPorNombre,
    string? Respuesta,
    AutorizacionSolicitudTipo Tipo = AutorizacionSolicitudTipo.FechaAtencion,
    // IMP-71: la respuesta tiene version enriquecida (se carga aparte con ObtenerObservacionHtmlAsync).
    bool RespuestaRica = false);

/// <summary>IMP-71: observacion que el autorizador dejo sobre una historia clinica (al aprobar o
/// rechazar una solicitud, o en el token que el profesional uso). <see cref="Html"/> es el HTML
/// saneado (null = solo texto plano en <see cref="Texto"/>).</summary>
public sealed record ObservacionAutorizacionDto(
    Guid Id,
    string Origen,          // "Solicitud" | "Token"
    string Estado,          // "Aprobada" | "Rechazada" | "Usado"
    string? Concepto,
    string? Texto,
    string? Html,
    string? AutorNombre,
    DateTimeOffset En);

/// <summary>Resultado de validar/consumir un token.</summary>
public sealed record UsarTokenResult(bool Ok, string? Mensaje, Guid? TokenId = null);

/// <summary>
/// Tokens de autorizacion de 6 caracteres (un solo uso, 30 min de vigencia) que permiten
/// saltar el control de fecha de atencion. Los genera un usuario de autorizacion en el
/// modulo /autorizaciones; se consumen desde el flujo de Atencion al capturar una fecha
/// fuera del limite del tipo de servicio.
/// </summary>
public interface IAutorizacionTokenService
{
    /// <summary>Lista los tokens del tenant (recientes primero) con su estado y datos de uso.</summary>
    Task<IReadOnlyList<AutorizacionTokenDto>> ListarAsync(int max = 200, CancellationToken ct = default);

    /// <summary>Genera un token nuevo con un concepto (opcional) y una observacion obligatoria.
    /// Devuelve el codigo de 6 chars.</summary>
    Task<string> GenerarAsync(string? conceptoNombre, string observacion, string? generadoPorNombre, Guid actor, CancellationToken ct = default);

    // ── Solicitudes de autorizacion (sin token) ─────────────────────────────────
    /// <summary>Lista solicitudes (pendientes primero, luego recientes).</summary>
    Task<IReadOnlyList<AutorizacionSolicitudDto>> ListarSolicitudesAsync(bool soloPendientes, int max = 200, CancellationToken ct = default);

    /// <summary>Cantidad de solicitudes pendientes del tenant (badge).</summary>
    Task<int> ContarSolicitudesPendientesAsync(CancellationToken ct = default);

    /// <summary>Aprueba una solicitud pendiente: la HC queda autorizada para la fecha solicitada y
    /// se registra un token "usado" (historial unico). Requiere permiso de autorizaciones.</summary>
    Task<UsarTokenResult> AprobarSolicitudAsync(Guid solicitudId, string? observacion, string? actorNombre, Guid actor, CancellationToken ct = default);

    /// <summary>Rechaza una solicitud pendiente con observacion obligatoria.</summary>
    Task<UsarTokenResult> RechazarSolicitudAsync(Guid solicitudId, string observacion, string? actorNombre, Guid actor, CancellationToken ct = default);

    // -- IMP-71: observaciones enriquecidas --------------------------------------
    /// <summary>HTML de la observacion de un token (<paramref name="esToken"/>) o de la respuesta de
    /// una solicitud. Null si no tiene version enriquecida.</summary>
    Task<string?> ObtenerObservacionHtmlAsync(Guid id, bool esToken, CancellationToken ct = default);

    /// <summary>Observaciones del autorizador sobre una HC (solicitudes resueltas con observacion y
    /// tokens usados en ella), recientes primero. Las ve el profesional en la historia.</summary>
    Task<IReadOnlyList<ObservacionAutorizacionDto>> ListarObservacionesDeHcAsync(Guid historiaClinicaId, CancellationToken ct = default);

    // ── Conceptos configurables ──────────────────────────────────────────────────
    /// <summary>Lista los conceptos del tenant (ordenados). Siembra ejemplos la primera vez.</summary>
    Task<IReadOnlyList<AutorizacionConceptoDto>> ListarConceptosAsync(bool incluirInactivos = false, CancellationToken ct = default);

    /// <summary>Crea (Id null) o actualiza un concepto. Nombre unico por tenant.</summary>
    Task<AutorizacionConceptoDto> GuardarConceptoAsync(Guid? id, string nombre, int orden, bool activo, Guid actor, CancellationToken ct = default);

    /// <summary>Elimina un concepto. Los tokens historicos conservan su nombre (snapshot).</summary>
    Task<bool> EliminarConceptoAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Valida un token (Activo, no vencido, no usado, del tenant) y, si es valido, lo MARCA como
    /// usado registrando HC/asignacion/usuario/motivo. NO hace SaveChanges: la persistencia la hace
    /// el orquestador (HistoriaClinicaService) en la misma transaccion que la HC, para atomicidad.
    /// </summary>
    Task<UsarTokenResult> ValidarYMarcarUsoAsync(
        string codigo, Guid historiaClinicaId, Guid? asignacionId,
        string? usadoPorNombre, Guid actor, string? motivo, CancellationToken ct = default);

    /// <summary>
    /// Igual que el overload sin <paramref name="db"/>, pero marca el token (y su auditoria) en el
    /// contexto CORTO que pasa el orquestador. Lo usa HistoriaClinicaService.AutorizarFechaConTokenAsync
    /// (Ola 4): abre su propio `using var db = factory.Create()` y debe marcar el token en ESE contexto
    /// para que su unico SaveChanges persista token + HC atomicamente. Con el overload clasico el token
    /// quedaria en el contexto scoped del servicio (otro contexto) y no se guardaria con la HC.
    /// </summary>
    Task<UsarTokenResult> ValidarYMarcarUsoAsync(
        IApplicationDbContext db, string codigo, Guid historiaClinicaId, Guid? asignacionId,
        string? usadoPorNombre, Guid actor, string? motivo, CancellationToken ct = default);
}

using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>DTO plano de la cabecera de revision para grids y JSON.</summary>
public sealed record RevisionClinicaDto(
    Guid Id,
    Guid TenantId,
    Guid HistoriaClinicaId,
    RevisionEstadoAgregado EstadoAgregado,
    RevisionResultado? EstadoAgente,
    DateTimeOffset SolicitadaEn,
    Guid? SolicitadaPor,
    DateTimeOffset UltimaAccionEn,
    int IteracionActual);

/// <summary>DTO plano de un evento de bitacora.</summary>
public sealed record RevisionEventoDto(
    Guid Id,
    Guid RevisionClinicaId,
    RevisionTipoEvento Tipo,
    RevisionResultado Resultado,
    RevisionActorTipo ActorTipo,
    Guid? ActorUsuarioId,
    string? ActorAgenteCodigo,
    int Iteracion,
    string? Motivo,
    string? Nota,
    string? PayloadJson,
    DateTimeOffset OcurridoEn);

/// <summary>Comando para abrir el ciclo. Si ya existe una revision viva para la HC, la devuelve.</summary>
public sealed record SolicitarRevisionCmd(Guid HistoriaClinicaId, Guid? SolicitadaPor, string? Nota);

/// <summary>Comando para asignar un revisor humano al ciclo. Actor = usuario o sistema.</summary>
public sealed record AsignarRevisorCmd(Guid RevisionClinicaId, Guid RevisorUsuarioId, bool Automatica, string? Nota);

/// <summary>Comando para emitir el veredicto del agente IA. Nunca cambia el estado agregado.</summary>
public sealed record VeredictoAgenteCmd(
    Guid RevisionClinicaId,
    string AgenteCodigo,
    RevisionResultado Resultado,
    string? Nota,
    string? PayloadJson);

/// <summary>Comando aprobar humano. <c>Nota</c> opcional. Cierra la iteracion.</summary>
public sealed record AprobarCmd(Guid RevisionClinicaId, Guid RevisorUsuarioId, string? Nota);

/// <summary>
/// Comando de adopcion automatica del veredicto del agente (Ola 6 RC6c). Actor
/// <see cref="RevisionActorTipo.Sistema"/>. <c>AgenteCodigo</c> identifica el
/// agente cuyo veredicto se esta adoptando; <c>Confianza</c> queda registrada
/// en el payload para auditoria.
/// </summary>
public sealed record AprobarPorSistemaCmd(
    Guid RevisionClinicaId,
    string AgenteCodigo,
    decimal Confianza,
    decimal UmbralConfianza,
    string? Nota);

/// <summary>Comando rechazar humano. <c>Motivo</c> obligatorio.</summary>
public sealed record RechazarCmd(Guid RevisionClinicaId, Guid RevisorUsuarioId, string Motivo, string? Nota);

/// <summary>Comando reenvio tras rechazo. Incrementa iteracion.</summary>
public sealed record ReenviarCmd(Guid RevisionClinicaId, Guid ProfesionalUsuarioId, string? Nota);

/// <summary>Comando archivar (estado terminal). Requiere permiso <c>historias.revisar.aprobar_final</c>.</summary>
public sealed record ArchivarOkCmd(Guid RevisionClinicaId, Guid RevisorUsuarioId, string? Nota);

/// <summary>Comando inactivar (baja logica). Requiere permiso <c>historias.revisar.aprobar_final</c>.</summary>
public sealed record InactivarCmd(Guid RevisionClinicaId, Guid RevisorUsuarioId, string Motivo);

/// <summary>
/// Casos de uso del ciclo de revision. Todos los comandos:
///   - Multi-tenant: implicitos por el global query filter.
///   - Append-only: escriben <see cref="RevisionClinicaEvento"/>, nunca UPDATE/DELETE.
///   - Validan transicion valida antes de aceptar. Ver
///     `2. Modelo de dominio (revision + bitacora).md` §2.b (mapeo columnas Kanban).
///   - Actualizan <see cref="RevisionClinica.EstadoAgregado"/> segun tabla de transiciones.
///   - Actualizan <see cref="RevisionClinica.UltimaAccionEn"/>.
/// </summary>
public interface IRevisionClinicaService
{
    Task<RevisionClinicaDto> SolicitarAsync(SolicitarRevisionCmd cmd, CancellationToken ct = default);
    Task<RevisionClinicaDto?> GetPorHistoriaAsync(Guid historiaClinicaId, CancellationToken ct = default);
    Task<IReadOnlyList<RevisionEventoDto>> ListarEventosAsync(Guid revisionClinicaId, CancellationToken ct = default);
    Task<RevisionClinicaDto> AsignarRevisorAsync(AsignarRevisorCmd cmd, CancellationToken ct = default);
    Task<RevisionClinicaDto> RegistrarVeredictoAgenteAsync(VeredictoAgenteCmd cmd, CancellationToken ct = default);
    Task<RevisionClinicaDto> AprobarAsync(AprobarCmd cmd, CancellationToken ct = default);

    /// <summary>
    /// Adopcion automatica del veredicto de un agente IA. Escribe un evento
    /// <c>Aprobado</c> con <c>ActorTipo=Sistema</c> y detalles del veredicto
    /// en <c>PayloadJson</c> para auditoria. Usa el mismo validador de
    /// transiciones que <see cref="AprobarAsync"/>.
    /// </summary>
    Task<RevisionClinicaDto> AprobarPorSistemaAsync(AprobarPorSistemaCmd cmd, CancellationToken ct = default);
    Task<RevisionClinicaDto> RechazarAsync(RechazarCmd cmd, CancellationToken ct = default);
    Task<RevisionClinicaDto> ReenviarAsync(ReenviarCmd cmd, CancellationToken ct = default);

    /// <summary>Cierra el ciclo en ArchivadaOk. <paramref name="tienePermisoFinal"/> debe venir true.</summary>
    Task<RevisionClinicaDto> ArchivarOkAsync(ArchivarOkCmd cmd, bool tienePermisoFinal, CancellationToken ct = default);

    /// <summary>Baja logica. <paramref name="tienePermisoFinal"/> debe venir true. Motivo obligatorio.</summary>
    Task<RevisionClinicaDto> InactivarAsync(InactivarCmd cmd, bool tienePermisoFinal, CancellationToken ct = default);
}

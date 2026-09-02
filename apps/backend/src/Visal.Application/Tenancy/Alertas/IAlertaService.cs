using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Alertas;

/// <summary>Fila de la parrilla + datos de edicion de una regla de alerta.</summary>
public sealed record AlertaReglaDto(
    Guid Id, string Nombre, bool Activa, int Orden,
    AlertaCondicion Condicion, string? FiltroModulo,
    AlertaDisparoTipo DisparoTipo, string? DiasDelMes, int? MesesDespues, AlertaAnclaRelativa? AnclaRelativa,
    AlertaDestinatario Destinatario, Guid? UsuarioSistemaId, string? UsuarioSistemaNombre,
    AlertaCanal Canal, string? Asunto, string? Cuerpo,
    Guid? HsmLineId, string? HsmTemplateId, string? HsmTemplateName, int HsmParameterCount,
    IReadOnlyList<string> HsmParametros);

/// <summary>Payload para crear/actualizar una regla. Id null = crear.</summary>
public sealed record AlertaReglaUpsertRequest(
    Guid? Id, string Nombre, bool Activa, int Orden,
    AlertaCondicion Condicion, string? FiltroModulo,
    AlertaDisparoTipo DisparoTipo, string? DiasDelMes, int? MesesDespues, AlertaAnclaRelativa? AnclaRelativa,
    AlertaDestinatario Destinatario, Guid? UsuarioSistemaId,
    AlertaCanal Canal, string? Asunto, string? Cuerpo,
    Guid? HsmLineId, string? HsmTemplateId, string? HsmTemplateName, int HsmParameterCount,
    IReadOnlyList<string>? HsmParametros);

/// <summary>Linea Gupshup disponible para el canal WhatsApp de una regla.</summary>
public sealed record AlertaLineaDto(Guid Id, string Nombre);

/// <summary>Usuario del sistema elegible como destinatario.</summary>
public sealed record AlertaUsuarioDto(Guid Id, string Nombre, string Email);

/// <summary>Resumen de una corrida de evaluacion/disparo.</summary>
public sealed record AlertaEvaluacionResult(int Enviadas, int Saltadas, int Errores, IReadOnlyList<string> Mensajes);

/// <summary>Tarjeta de una alerta emitida (bandeja del modulo Alertas).</summary>
public sealed record AlertaEnvioDto(
    Guid Id, string ReglaNombre, string PacienteNombre, string? Contacto,
    AlertaCanal Canal, AlertaDestinatario Destinatario,
    DateTimeOffset FechaEnvio, bool Exito, string? Error,
    AlertaGestion EstadoGestion, string Periodo);

/// <summary>
/// Motor de reglas de alerta por tenant: CRUD de reglas y el evaluador que el
/// worker (o el boton "Ejecutar ahora") invoca para disparar los envios.
/// </summary>
public interface IAlertaService
{
    Task<IReadOnlyList<AlertaReglaDto>> ListAsync(CancellationToken ct = default);
    Task<AlertaReglaDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> UpsertAsync(AlertaReglaUpsertRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
    Task<bool> ToggleActivaAsync(Guid id, bool activa, Guid actor, CancellationToken ct = default);

    /// <summary>Lineas Gupshup del tenant (para el selector del canal WhatsApp).</summary>
    Task<IReadOnlyList<AlertaLineaDto>> ListLineasGupshupAsync(CancellationToken ct = default);

    /// <summary>Usuarios del sistema activos del tenant (para destinatario UsuarioSistema).</summary>
    Task<IReadOnlyList<AlertaUsuarioDto>> ListUsuariosAsync(CancellationToken ct = default);

    /// <summary>
    /// Evalua todas las reglas activas del tenant activo para el dia indicado y
    /// dispara los envios que correspondan (respetando el outbox para no repetir).
    /// Si <paramref name="forzar"/> es true, ignora el filtro de dia del disparo
    /// (para el boton "Ejecutar ahora") pero mantiene la deduplicacion por periodo.
    /// </summary>
    Task<AlertaEvaluacionResult> EvaluarYDispararAsync(DateOnly hoy, bool forzar, Guid actor, CancellationToken ct = default);

    /// <summary>Bandeja: alertas emitidas mas recientes (tarjetas) con nombre de regla y paciente.</summary>
    Task<IReadOnlyList<AlertaEnvioDto>> ListEnviosRecientesAsync(int max = 200, CancellationToken ct = default);

    /// <summary>Marca la gestion de una tarjeta de alerta (Nueva/Atendida/Descartada).</summary>
    Task<bool> MarcarGestionAsync(Guid envioId, AlertaGestion estado, Guid actor, CancellationToken ct = default);
}

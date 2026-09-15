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
    IReadOnlyList<string> HsmParametros, string? HsmHeaderUrl = null, Guid? SeguimientoReglaId = null);

/// <summary>Payload para crear/actualizar una regla. Id null = crear.</summary>
public sealed record AlertaReglaUpsertRequest(
    Guid? Id, string Nombre, bool Activa, int Orden,
    AlertaCondicion Condicion, string? FiltroModulo,
    AlertaDisparoTipo DisparoTipo, string? DiasDelMes, int? MesesDespues, AlertaAnclaRelativa? AnclaRelativa,
    AlertaDestinatario Destinatario, Guid? UsuarioSistemaId,
    AlertaCanal Canal, string? Asunto, string? Cuerpo,
    Guid? HsmLineId, string? HsmTemplateId, string? HsmTemplateName, int HsmParameterCount,
    IReadOnlyList<string>? HsmParametros, string? HsmHeaderUrl = null, Guid? SeguimientoReglaId = null);

/// <summary>Linea Gupshup disponible para el canal WhatsApp de una regla.</summary>
public sealed record AlertaLineaDto(Guid Id, string Nombre);

/// <summary>Usuario del sistema elegible como destinatario.</summary>
public sealed record AlertaUsuarioDto(Guid Id, string Nombre, string Email);

/// <summary>Servicio de contrato (Entidades Aseguradoras) elegible como filtro de la regla.
/// <paramref name="Codigo"/> es el codigo base (sin sufijo d/f) usado para el match.</summary>
public sealed record AlertaServicioDto(string Codigo, string Descripcion);

/// <summary>Resumen de una corrida de evaluacion/disparo.</summary>
public sealed record AlertaEvaluacionResult(int Enviadas, int Saltadas, int Errores, IReadOnlyList<string> Mensajes);

/// <summary>Fila de la simulacion de una regla: un candidato (paciente/servicio) y a quien
/// se le emitiria, con el estado que tendria en la fecha simulada.</summary>
public sealed record AlertaSimulacionFila(
    string Paciente, string Documento, string Servicio, string CodigoServicio,
    string DestinatarioTipo, string? DestinatarioNombre,
    string? Correo, string? Telefono,
    AlertaCanal Canal, string? ContactoUsado,
    string Estado, bool Emitible, bool? EnvioOk, string? EnvioError,
    Guid AsignacionId = default, Guid? ProfesionalId = null, string? InformeUrl = null);

/// <summary>Resultado de simular una regla en una fecha: filas + totales. En modo emitir
/// (paso 2) tambien envia realmente y reporta Enviadas/Errores.</summary>
public sealed record AlertaSimulacionResult(
    DateOnly Fecha, string Periodo, bool Emitido,
    int Coinciden, int Emitibles, int SinContacto, int YaEnviadas, int Enviadas, int Errores,
    IReadOnlyList<AlertaSimulacionFila> Filas, string? Aviso);

/// <summary>Fila del control de lectura del informe: un doctor con cuantas alertas se le
/// enviaron en el periodo y cuantas veces abrio su enlace del informe.</summary>
public sealed record ControlLecturaDoctorDto(
    Guid ProfesionalId, string Nombre, string? Celular,
    int Enviados, int Aperturas, DateTimeOffset? UltimaApertura, DateTimeOffset? UltimoEnvio);

/// <summary>Control de lectura del informe para un periodo: doctores a los que se les
/// envio la alerta, separados entre los que abrieron el enlace (consumieron) y los que
/// no (no leyeron). Alimenta los dos mini-reportes y la alerta a gerencia.</summary>
public sealed record ControlLecturaResult(
    string Periodo, int TotalDoctores, int Consumieron, int NoLeyeron,
    IReadOnlyList<ControlLecturaDoctorDto> ListaConsumieron,
    IReadOnlyList<ControlLecturaDoctorDto> ListaNoLeyeron);

/// <summary>Tarjeta de la bandeja de alertas. Las alertas al doctor se agrupan por
/// profesional (una tarjeta con N pacientes); las de paciente/usuario van una por
/// destinatario. <paramref name="Titulo"/> es el destinatario (doctor/paciente/usuario),
/// <paramref name="Detalle"/> el subtexto (ej. "3 pacientes" o documento), y
/// <paramref name="EnvioIds"/> los envios que agrupa (para gestionar toda la tarjeta).</summary>
public sealed record AlertaEnvioDto(
    Guid Id, string ReglaNombre, string Titulo, string? Detalle, string? Contacto,
    AlertaCanal Canal, AlertaDestinatario Destinatario,
    DateTimeOffset FechaEnvio, bool Exito, string? Error,
    AlertaGestion EstadoGestion, string Periodo,
    int Cantidad, IReadOnlyList<Guid> EnvioIds);

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

    /// <summary>Servicios de los contratos (Entidades Aseguradoras) del tenant, distintos por
    /// codigo base (sin sufijo d/f), ordenados por codigo. Para el filtro de la regla.</summary>
    Task<IReadOnlyList<AlertaServicioDto>> ListServiciosContratoAsync(CancellationToken ct = default);

    /// <summary>
    /// Evalua todas las reglas activas del tenant activo para el dia indicado y
    /// dispara los envios que correspondan (respetando el outbox para no repetir).
    /// Si <paramref name="forzar"/> es true, ignora el filtro de dia del disparo
    /// (para el boton "Ejecutar ahora") pero mantiene la deduplicacion por periodo.
    /// </summary>
    Task<AlertaEvaluacionResult> EvaluarYDispararAsync(DateOnly hoy, bool forzar, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// Simula una regla (segun la config del modal, guardada o no) para la fecha indicada:
    /// muestra que candidatos coincidirian y a quien se emitiria (paciente, servicio,
    /// destinatario, correo y telefono), sin enviar. Si <paramref name="emitir"/> es true
    /// (paso 2) envia realmente y registra el outbox — requiere que la regla ya este guardada.
    /// </summary>
    /// <param name="telefonoOverride">Opcional: si se indica y el canal es WhatsApp,
    /// se usa ese numero como destino en vez del contacto resuelto (para pruebas).</param>
    /// <param name="forzarReenvio">Si es true, ignora la deduplicacion (reenvia aunque ya
    /// se haya enviado en el periodo). Util para reprobar desde la simulacion.</param>
    /// <param name="baseUri">Base publica de la app (ej. NavigationManager.BaseUri) para
    /// construir el enlace del informe por doctor que se muestra en cada fila. Null/vacio
    /// = no se genera el enlace.</param>
    /// <param name="soloAsignaciones">Opcional: cuando <paramref name="emitir"/> es true,
    /// limita el envio real a estas asignaciones (checkboxes de la tabla). Null/vacio =
    /// emite a todas las filas candidatas (comportamiento anterior).</param>
    Task<AlertaSimulacionResult> SimularReglaAsync(AlertaReglaUpsertRequest req, DateOnly fecha, bool emitir, string? telefonoOverride, bool forzarReenvio, Guid actor, string? baseUri = null, IReadOnlyCollection<Guid>? soloAsignaciones = null, CancellationToken ct = default);

    /// <summary>Bandeja: alertas emitidas mas recientes (tarjetas) con nombre de regla y paciente.</summary>
    Task<IReadOnlyList<AlertaEnvioDto>> ListEnviosRecientesAsync(int max = 200, CancellationToken ct = default);

    /// <summary>Marca la gestion de una tarjeta de alerta (Nueva/Atendida/Descartada).</summary>
    Task<bool> MarcarGestionAsync(Guid envioId, AlertaGestion estado, Guid actor, CancellationToken ct = default);

    /// <summary>Marca la gestion de varios envios de una vez (una tarjeta agrupada de la bandeja).</summary>
    Task<bool> MarcarGestionLoteAsync(IReadOnlyCollection<Guid> envioIds, AlertaGestion estado, Guid actor, CancellationToken ct = default);

    /// <summary>Control de lectura del informe para un periodo "yyyy-MM": cruza los doctores
    /// a los que se les envio la alerta contra los accesos registrados a su enlace, y los
    /// separa en "consumieron" y "no leyeron". Si <paramref name="reglaObjetivoId"/> viene,
    /// acota el control a esa regla de alerta; null = todas las alertas de doctor.</summary>
    Task<ControlLecturaResult> ObtenerControlLecturaAsync(string periodo, Guid? reglaObjetivoId = null, CancellationToken ct = default);

    /// <summary>Reglas de alerta dirigidas al doctor (para el selector "sobre cual alerta"
    /// del control de lectura). Excluye las propias reglas de control.</summary>
    Task<IReadOnlyList<AlertaReglaDto>> ListReglasDoctorAsync(CancellationToken ct = default);
}

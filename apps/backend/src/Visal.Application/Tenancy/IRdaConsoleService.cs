using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>Fila del grid de la consola RDA.</summary>
public sealed record RdaEventoRowDto(
    Guid Id,
    DateTimeOffset FechaGeneracion,
    string PacienteNombre,
    string PacienteDocumento,
    string ProfesionalNombre,
    string SucursalNombre,
    string? Servicio,
    ModalidadRdaIhce Modalidad,
    AmbienteIhce Ambiente,
    EstadoRdaEvento Estado,
    int Intentos,
    DateTimeOffset? UltimoIntento,
    DateTimeOffset? FechaEnvio,
    string? ReferenciaMinsalud,
    string BundleHash,
    TipoRdaIhce TipoRda,
    bool TieneCredencialSede,
    string? MotivoBloqueoEnvio,
    // Clave de agrupacion por caso: una historia + tipo puede tener VARIOS eventos
    // (cada "Reprocesar" inserta uno nuevo, no actualiza el viejo). Se usa para el
    // historial de reintentos y para deduplicar la parrilla al evento vigente.
    Guid HistoriaClinicaId);

/// <summary>Detalle expandido (incluye el JSON completo).</summary>
public sealed record RdaEventoDetailDto(
    Guid Id,
    string BundleJson,
    string BundleHash,
    EstadoRdaEvento Estado,
    int Intentos,
    string? ErroresJson,
    string? RespuestaMinsalud,
    string? ReferenciaMinsalud,
    DateTimeOffset FechaGeneracion,
    DateTimeOffset? FechaEnvio);

/// <summary>Un intento (evento) dentro del historial de un caso (HistoriaClinica + Tipo).
/// Cada "Reprocesar" o reenvio inserta un RdaEvento nuevo con bundle regenerado; este DTO
/// alimenta el modal que colapsa esos duplicados y muestra la cronologia real.</summary>
public sealed record RdaEventoHistorialItemDto(
    Guid Id,
    DateTimeOffset FechaGeneracion,
    EstadoRdaEvento Estado,
    int Intentos,
    DateTimeOffset? UltimoIntento,
    string BundleHash,
    string? CodigoError,   // code MinSalud del ultimo issue (6006/1028/MPI-001...), si lo hay
    bool EsVigente);       // true = es el evento mas reciente del caso (el que vale hoy)

/// <summary>Filtro para el grid.</summary>
public sealed record RdaConsoleFiltro(
    string? Documento = null,
    EstadoRdaEvento? Estado = null,
    AmbienteIhce? Ambiente = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    // Filtros adicionales de la consola.
    string? Paciente = null,       // nombre o documento (contiene)
    Guid? SucursalId = null,       // sede
    string? Profesional = null,    // doctor (contiene)
    string? Servicio = null,       // servicio de la asignacion (contiene)
    int? IntentosMin = null,       // intentos de sincronizacion >= N
    bool SoloRechazados = false,   // atajo: solo eventos Rechazado
    // Dedup: mostrar SOLO el evento vigente (el mas reciente) de cada (HistoriaClinica,
    // Tipo). Por defecto ON: evita que la parrilla acumule reintentos duplicados y
    // rechazos superados (cuando una HC por fin se acepta, su fila vieja Rechazado
    // sigue en la tabla). Apagado = historico completo, fila por intento.
    bool SoloVigentes = true);

/// <summary>Item para el reproceso masivo. Un evento del tenant que NO esta Aceptado,
/// con su HC y tipo, para poder REGENERAR el bundle con el codigo actual (aplicando los
/// fixes vigentes de los builders) y reenviarlo al IHCE. Trae la Hc porque el bundle
/// guardado esta congelado: reenviarlo tal cual no aplicaria ningun fix.</summary>
public sealed record RdaReprocesoItemDto(
    Guid Id,
    Guid HistoriaClinicaId,
    TipoRdaIhce TipoRda,
    ModalidadRdaIhce Modalidad,
    EstadoRdaEvento Estado,
    string PacienteNombre,
    string PacienteDocumento,
    bool TieneCredencialSede);

/// <summary>HC candidata a generar RDA (combo del modal Generar).</summary>
public sealed record HcCandidataRdaDto(
    Guid Id,
    string PacienteNombre,
    string PacienteDocumento,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? FormatoCodigo,
    string Estado);

/// <summary>Credenciales EXACTAS usadas por un envio (ya descifradas). Este DTO
/// sale del backend solo bajo un endpoint autenticado y esta pensado para que el
/// operador copie/compare al abrir un ticket con MinSalud. NO se persiste, NO se
/// loggea. Enmascarar en UI por defecto.</summary>
public sealed record RdaEventoCredencialesDto(
    string? CodigoHabilitacion,
    string? ClientId,
    string? ClientSecret,
    string? ApimSubskey,
    string? AzureTenantId,
    string? Scope,
    string? EndpointBase,
    string? PathEnvio,
    string Ambiente);

public interface IRdaConsoleService
{
    /// <summary>Lista paginada de RdaEventos del tenant activo, ordenada por fecha de generacion desc.</summary>
    Task<IReadOnlyList<RdaEventoRowDto>> ListarAsync(RdaConsoleFiltro filtro, CancellationToken ct = default);

    /// <summary>Detalle de un evento incluyendo el Bundle JSON completo.</summary>
    Task<RdaEventoDetailDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Historial de reintentos del caso al que pertenece el evento <paramref name="id"/>:
    /// todos los RdaEventos de su misma (HistoriaClinica, Tipo), del mas reciente al mas viejo,
    /// marcando cual es el vigente y extrayendo el codigo de error de cada uno. Alimenta el modal
    /// que colapsa los duplicados de la parrilla.</summary>
    Task<IReadOnlyList<RdaEventoHistorialItemDto>> ListarHistorialAsync(Guid id, CancellationToken ct = default);

    /// <summary>Credenciales y parametros de API que se usaron en el envio (client secret,
    /// apim subskey descifrados). Se usa para diagnosticar tickets con MinSalud sin
    /// tener que sacar los valores de la BD a mano. Devuelve null si no existe.</summary>
    Task<RdaEventoCredencialesDto?> ObtenerCredencialesUsadasAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lista HCs candidatas para generar RDA (cerradas o abiertas del tenant).</summary>
    Task<IReadOnlyList<HcCandidataRdaDto>> ListarHcCandidatasAsync(string? buscar, CancellationToken ct = default);

    /// <summary>
    /// Lista los eventos del tenant que NO estan Aceptado (Borrador/Validado/Enviado/
    /// Rechazado/Error), deduplicados por (HistoriaClinica, Tipo) quedandose con el mas
    /// reciente, y excluyendo HCs que ya tienen un evento Aceptado de ese mismo tipo (para
    /// no duplicar RDAs ya confirmados). Alimenta el boton "Reprocesar pendientes", que
    /// regenera el bundle con el codigo actual y lo reenvia. Ordenado por fecha asc.
    /// </summary>
    Task<IReadOnlyList<RdaReprocesoItemDto>> ListarPendientesReprocesoAsync(CancellationToken ct = default);

    /// <summary>Borra un evento. Solo permitido en estado Borrador (para evitar romper auditoria).</summary>
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);
}

using Visal.Application.Revision.Ia;

namespace Visal.Application.Revision.Bitacora;

/// <summary>Filtros de la bitacora. Las fechas son dias LOCALES (Colombia, UTC-5) sobre la fecha
/// de la corrida del agente.</summary>
public sealed record BitacoraFiltro(
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    string? Resultado = null,
    string? Origen = null,
    string? Profesional = null,
    string? Hc = null,
    string? Formato = null,
    bool SoloAlertas = false,
    bool SoloPulgarAbajo = false);

public sealed record BitacoraPagina<T>(IReadOnlyList<T> Items, int Total, int Page, int Size);

public sealed record BitacoraProfesionalDto(string Profesional, int Corridas, int Rechazadas, int Historias);

public sealed record BitacoraResumenDto(
    int Corridas,
    int Historias,
    int Aprobadas,
    int Rechazadas,
    int Neutrales,
    int Fallidas,
    int HistoriasConVariosIntentos,
    int HistoriasConAlertas,
    bool AlertasParciales,
    int PulgarArriba,
    int PulgarAbajo,
    long TokensIn,
    long TokensOut,
    IReadOnlyList<BitacoraProfesionalDto> Profesionales);

public sealed record BitacoraHcFilaDto(
    Guid HistoriaClinicaId,
    string Hc,
    string? Paciente,
    string? PacienteDocumento,
    string? FormatoCodigo,
    string? Formato,
    string? Profesional,
    string EstadoHc,
    int Corridas,
    int Fallidas,
    string? UltimoResultado,
    decimal? UltimaConfianza,
    DateTimeOffset PrimeraEn,
    DateTimeOffset UltimaEn,
    int PulgarArriba,
    int PulgarAbajo,
    BitacoraSenales Senales);

/// <summary>Una corrida sin los textos pesados (prompt, contexto, respuesta cruda).</summary>
public sealed record BitacoraCorridaDto(
    Guid Id,
    Guid HistoriaClinicaId,
    DateTimeOffset En,
    string Origen,
    string? DisparadoPor,
    bool EnCicloRevision,
    string AgenteCodigo,
    string? Modelo,
    bool Ok,
    string? Error,
    string? Resultado,
    decimal? Confianza,
    int TokensIn,
    int TokensOut,
    string? Nota,
    IReadOnlyList<string> Hallazgos,
    IReadOnlyList<SugerenciaTexto> Sugerencias,
    bool? FeedbackUtil,
    string? FeedbackComentario,
    string? FeedbackPor,
    DateTimeOffset? FeedbackEn)
{
    /// <summary>IMP-73: observaciones menores (no impiden aprobar); no entran al analisis.</summary>
    public IReadOnlyList<string> Observaciones { get; init; } = [];
}

public sealed record BitacoraHcDetalleDto(
    BitacoraHcFilaDto Historia,
    IReadOnlyList<BitacoraCorridaDto> Corridas,
    IReadOnlyList<BitacoraIntentoAnalisis> Analisis);

/// <summary>Corrida completa: lo que recibio el agente y lo que respondio, tal cual.</summary>
public sealed record BitacoraCorridaCompletaDto(
    BitacoraCorridaDto Corrida,
    string Hc,
    string? Proveedor,
    string? SystemPrompt,
    string? ContextoEnviado,
    string? CamposLegiblesJson,
    string? ToolsJson,
    string? RespuestaCruda);

public sealed record BitacoraApiConfigDto(bool HasKey, string? ApiKey, bool IsEnabled, DateTimeOffset? LastUsedAt);

/// <summary>
/// IMP-72: bitacora del auditor IA (revision_agente_logs) por historia clinica con sus intentos y
/// la comparacion entre intentos. Todos los metodos reciben el tenant EXPLICITO (la API de solo
/// lectura no tiene sesion) y filtran por el; nunca dependen del filtro global.
/// </summary>
public interface IBitacoraIaService
{
    public const int MaxSize = 200;

    Task<BitacoraResumenDto> ResumenAsync(Guid tenantId, BitacoraFiltro filtro, CancellationToken ct = default);

    Task<BitacoraPagina<BitacoraHcFilaDto>> ListarHistoriasAsync(Guid tenantId, BitacoraFiltro filtro, int page, int size, CancellationToken ct = default);

    /// <summary>Corridas sueltas (sin agrupar), mas recientes primero.</summary>
    Task<BitacoraPagina<BitacoraCorridaDto>> ListarCorridasAsync(Guid tenantId, BitacoraFiltro filtro, int page, int size, CancellationToken ct = default);

    /// <summary><paramref name="hcRef"/> = Id (GUID), "HC-033421" o "33421".</summary>
    Task<BitacoraHcDetalleDto?> DetalleHistoriaAsync(Guid tenantId, string hcRef, CancellationToken ct = default);

    Task<BitacoraCorridaCompletaDto?> CorridaAsync(Guid tenantId, Guid logId, CancellationToken ct = default);

    // -- API de solo lectura --
    Task<BitacoraApiConfigDto> GetApiAsync(Guid tenantId, CancellationToken ct = default);
    Task<BitacoraApiConfigDto> RegenerarApiAsync(Guid tenantId, Guid actor, CancellationToken ct = default);
    Task<BitacoraApiConfigDto> SetApiHabilitadaAsync(Guid tenantId, bool enabled, Guid actor, CancellationToken ct = default);

    /// <summary>Resuelve el tenant por la llave (habilitada), actualiza LastUsedAt y deja la consulta
    /// en auditoria (endpoint + parametros, sin datos). Null = llave invalida o apagada.</summary>
    Task<Guid?> ResolverLlaveAsync(string apiKey, string endpoint, string? query, CancellationToken ct = default);
}

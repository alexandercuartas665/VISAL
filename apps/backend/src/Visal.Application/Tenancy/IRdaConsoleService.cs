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
    ModalidadRdaIhce Modalidad,
    AmbienteIhce Ambiente,
    EstadoRdaEvento Estado,
    int Intentos,
    DateTimeOffset? FechaEnvio,
    string? ReferenciaMinsalud,
    string BundleHash,
    TipoRdaIhce TipoRda,
    bool TieneCredencialSede,
    string? MotivoBloqueoEnvio);

/// <summary>Detalle expandido (incluye el JSON completo).</summary>
public sealed record RdaEventoDetailDto(
    Guid Id,
    string BundleJson,
    string BundleHash,
    EstadoRdaEvento Estado,
    int Intentos,
    string? ErroresJson,
    string? ReferenciaMinsalud,
    DateTimeOffset FechaGeneracion,
    DateTimeOffset? FechaEnvio);

/// <summary>Filtro para el grid.</summary>
public sealed record RdaConsoleFiltro(
    string? Documento = null,
    EstadoRdaEvento? Estado = null,
    AmbienteIhce? Ambiente = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null);

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

    /// <summary>Credenciales y parametros de API que se usaron en el envio (client secret,
    /// apim subskey descifrados). Se usa para diagnosticar tickets con MinSalud sin
    /// tener que sacar los valores de la BD a mano. Devuelve null si no existe.</summary>
    Task<RdaEventoCredencialesDto?> ObtenerCredencialesUsadasAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lista HCs candidatas para generar RDA (cerradas o abiertas del tenant).</summary>
    Task<IReadOnlyList<HcCandidataRdaDto>> ListarHcCandidatasAsync(string? buscar, CancellationToken ct = default);

    /// <summary>Borra un evento. Solo permitido en estado Borrador (para evitar romper auditoria).</summary>
    Task<bool> EliminarAsync(Guid id, Guid actor, CancellationToken ct = default);
}

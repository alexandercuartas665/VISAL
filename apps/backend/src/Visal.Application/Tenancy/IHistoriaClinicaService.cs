namespace Visal.Application.Tenancy;

/// <summary>Resumen de una historia clinica para la lista lateral del modulo.</summary>
public sealed record HistoriaClinicaResumenDto(
    Guid Id,
    Guid FormDefinitionId,
    string FormatoCodigo,
    string FormatoNombre,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? EspecialistaNombre,
    string? MotivoInactivacion,
    Guid? ProfesionalId);

/// <summary>Detalle completo de una historia (incluye valores diligenciados).</summary>
public sealed record HistoriaClinicaDetailDto(
    Guid Id,
    Guid PacienteId,
    Guid FormDefinitionId,
    string FormatoCodigo,
    string FormatoNombre,
    string? FormatoVersion,
    string SchemaJson,
    string? PrefillRoutesJson,
    string ValoresJson,
    string Estado,
    DateTimeOffset FechaApertura,
    DateTimeOffset? FechaCierre,
    string? EspecialistaNombre,
    string? MotivoInactivacion,
    Guid? ProfesionalId,
    string? RipsViaIngresoCodigo = null,
    string? RipsViaIngresoNombre = null,
    string? RipsFinalidadCodigo = null,
    string? RipsFinalidadNombre = null,
    string? RipsCausaExternaCodigo = null,
    string? RipsCausaExternaNombre = null);

public sealed record CrearHistoriaRequest(
    Guid PacienteId,
    Guid FormDefinitionId,
    string ValoresJson,
    string? EspecialistaNombre,
    Guid? ProfesionalId = null,
    // Datos RIPS obligatorios (Res. 202/2021 MinSalud):
    string? RipsViaIngresoCodigo = null,
    string? RipsViaIngresoNombre = null,
    string? RipsFinalidadCodigo = null,
    string? RipsFinalidadNombre = null,
    string? RipsCausaExternaCodigo = null,
    string? RipsCausaExternaNombre = null);

public interface IHistoriaClinicaService
{
    /// <summary>Historias del paciente filtradas opcionalmente por rango de fechas y formato.</summary>
    Task<IReadOnlyList<HistoriaClinicaResumenDto>> ListarPorPacienteAsync(
        Guid pacienteId,
        DateOnly? desde = null, DateOnly? hasta = null,
        Guid? formDefinitionId = null,
        CancellationToken ct = default);

    /// <summary>Trae la historia completa con su schema y valores.</summary>
    Task<HistoriaClinicaDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea una historia con estado Abierta y los valores iniciales (prefill).</summary>
    Task<HistoriaClinicaDetailDto> CrearAsync(CrearHistoriaRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Sobrescribe los valores diligenciados de una historia abierta.</summary>
    Task<bool> GuardarValoresAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    /// <summary>Cambia el estado a Cerrada y registra FechaCierre. Persiste los valores que llegan.</summary>
    Task<bool> CerrarAsync(Guid id, string valoresJson, Guid actor, CancellationToken ct = default);

    /// <summary>Marca como Inactiva (descarte) con motivo opcional.</summary>
    Task<bool> DescartarAsync(Guid id, string? motivo, Guid actor, CancellationToken ct = default);

    /// <summary>Id de la HC abierta mas reciente del paciente, o null si no hay.</summary>
    Task<Guid?> BuscarUltimaAbiertaPorPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Busca la HC abierta del MISMO profesional para el paciente y el
    /// formato dado. Sirve para reanudar la HC en curso cuando el doctor vuelve
    /// al paciente (en vez de crear una nueva). Null si no hay.</summary>
    Task<Guid?> BuscarAbiertaDelProfesionalAsync(Guid pacienteId, Guid profesionalId, Guid formDefinitionId, CancellationToken ct = default);
}

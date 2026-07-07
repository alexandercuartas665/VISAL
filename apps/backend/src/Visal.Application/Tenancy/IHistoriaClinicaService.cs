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

    /// <summary>Reabre una HC previamente Cerrada volviendo su estado a Abierta y
    /// limpiando FechaCierre. Solo se permite si estaba Cerrada (no aplica a
    /// Inactiva). El caller debe validar el permiso administrativo del actor
    /// antes de invocar este metodo.</summary>
    Task<bool> ReabrirAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Marca como Inactiva (descarte) con motivo opcional.</summary>
    Task<bool> DescartarAsync(Guid id, string? motivo, Guid actor, CancellationToken ct = default);

    /// <summary>Id de la HC abierta mas reciente del paciente, o null si no hay.</summary>
    Task<Guid?> BuscarUltimaAbiertaPorPacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Busca la HC abierta del MISMO profesional para el paciente y el
    /// formato dado. Sirve para reanudar la HC en curso cuando el doctor vuelve
    /// al paciente (en vez de crear una nueva). Null si no hay.</summary>
    Task<Guid?> BuscarAbiertaDelProfesionalAsync(Guid pacienteId, Guid profesionalId, Guid formDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Duplica una HC existente: crea una nueva HC en estado Abierta, con el mismo
    /// FormDefinitionId y PacienteId, copiando el ValoresJson origen y todos los items
    /// clinicos (medicamentos, insumos, remisiones, incapacidades, certificaciones,
    /// ordenes de servicio, ordenes externas). El caller (frontend) es responsable de
    /// re-aplicar el prefill (paciente + sistema + firmas) sobre la HC nueva para que
    /// los campos volatiles (fecha, hora, medico logueado) se refresquen — el prefill
    /// sobrescribe sobre los valores copiados.
    /// </summary>
    Task<HistoriaClinicaDetailDto?> CopiarAsync(CopiarHistoriaRequest req, Guid actor, CancellationToken ct = default);
}

/// <summary>Peticion de duplicacion de HC. EspecialistaNombre y ProfesionalId
/// permiten reemplazar los datos del profesional origen por los del usuario que
/// esta creando la copia; si son null, se conservan los del origen.</summary>
public sealed record CopiarHistoriaRequest(
    Guid SourceHistoriaId,
    string? EspecialistaNombre,
    Guid? ProfesionalId);

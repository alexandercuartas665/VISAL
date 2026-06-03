namespace Visal.Application.Tenancy;

public sealed record NotaMedicaDto(
    Guid Id,
    Guid HistoriaClinicaId,
    Guid PacienteId,
    string CodigoUnico,
    DateOnly FechaNota,
    TimeOnly? HoraNota,
    int? SessionNo,
    string Contenido,
    string? EspecialistaNombre,
    string Estado,            // "Parcial" | "Definitivo"
    string Criticidad,        // "Estable" | "Vigilancia" | "Alerta" | "Critico"
    string? FirmaDataUrl,
    string? FirmaPacienteDataUrl,
    DateTimeOffset CreatedAt);

public sealed record NotaMedicaTarjetaDto(
    Guid Id,
    string CodigoUnico,
    DateOnly FechaNota,
    TimeOnly? HoraNota,
    int? SessionNo,
    string ContenidoPreview,  // primeros ~200 chars
    string? EspecialistaNombre,
    string Estado,
    string Criticidad,
    string? FormatoCodigo,    // del FormDefinition de la HC
    string? FormatoNombre);

public sealed record NotaConteoDto(int Definitivas, int Parciales);

public sealed record GuardarNotaRequest(
    Guid? Id,
    Guid HistoriaClinicaId,
    Guid PacienteId,
    Guid? AsignacionTurnoId,
    int? SessionNo,
    DateOnly FechaNota,
    TimeOnly? HoraNota,
    string Contenido,
    string Estado,
    string Criticidad,
    string? FirmaDataUrl,
    string? EspecialistaNombre = null,
    string? FirmaPacienteDataUrl = null);

public sealed record NotaDocumentoDto(
    Guid Id,
    Guid NotaMedicaId,
    string NombreOriginal,
    string RutaArchivo,
    string? TipoMime,
    long Tamano,
    string? Categoria,
    string? TipoTerapia,
    string? Mes,
    string? Anotaciones,
    DateTimeOffset CreatedAt);

public sealed record AdjuntarDocumentoRequest(
    Guid NotaMedicaId,
    string NombreOriginal,
    string RutaArchivo,
    string? TipoMime,
    long Tamano,
    string? Categoria,
    string? TipoTerapia,
    string? Mes,
    string? Anotaciones);

/// <summary>
/// Resultado de la validacion previa antes de abrir el modulo de notas.
/// Ok=false significa que el profesional debe primero crear / renovar la HC
/// del paciente para el formato que pide el servicio. HistoriaId es el id
/// de la HC vigente que satisface la regla (si Ok=true).
/// </summary>
public sealed record ValidarHcParaNotaResult(
    bool Ok, string Mensaje, Guid? HistoriaId);

public interface INotaMedicaService
{
    /// <summary>Notas de una HC (todas: parciales y definitivas).</summary>
    Task<IReadOnlyList<NotaMedicaDto>> ListarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    /// <summary>Conteo {definitivas, parciales} para el indicador X/Y de la pestana.</summary>
    Task<NotaConteoDto> ContarPorHistoriaAsync(
        Guid historiaId, CancellationToken ct = default);

    /// <summary>Notas anteriores del MISMO paciente (todas las HCs). Tarjetas.</summary>
    Task<IReadOnlyList<NotaMedicaTarjetaDto>> ListarHistorialPacienteAsync(
        Guid pacienteId, CancellationToken ct = default);

    Task<NotaMedicaDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Antes de abrir el editor de notas en /atencion, valida que exista una HC
    /// para el paciente con el formato exigido por el servicio (formatoCodigo),
    /// y que esa HC este dentro del rango de "Validez de Historia Clinica (Meses)"
    /// configurado en la empresa. Si no cumple devuelve Ok=false con el mensaje
    /// orientativo para el usuario.
    /// </summary>
    Task<ValidarHcParaNotaResult> ValidarHcParaNotaAsync(
        Guid pacienteId, string? formatoCodigo, CancellationToken ct = default);

    Task<NotaMedicaDto> GuardarAsync(
        GuardarNotaRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> EliminarAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Cambia solo la criticidad (usado por el drag&drop del kanban).</summary>
    Task<bool> ActualizarCriticidadAsync(
        Guid id, string criticidad, Guid actorUserId, CancellationToken ct = default);

    // ---- Documentos adjuntos ----
    Task<IReadOnlyList<NotaDocumentoDto>> ListarDocumentosAsync(
        Guid notaId, CancellationToken ct = default);

    Task<NotaDocumentoDto> AdjuntarDocumentoAsync(
        AdjuntarDocumentoRequest req, Guid actorUserId, CancellationToken ct = default);

    Task<bool> EliminarDocumentoAsync(Guid documentoId, Guid actorUserId, CancellationToken ct = default);
}

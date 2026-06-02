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
    string? EspecialistaNombre = null);

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

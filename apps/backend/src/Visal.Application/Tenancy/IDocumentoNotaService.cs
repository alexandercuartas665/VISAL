namespace Visal.Application.Tenancy;

/// <summary>DTO plano de una nota tomada sobre un documento. Incluye el email
/// del autor para pintarlo en la timeline sin JOIN extra en el UI.</summary>
public sealed record DocumentoNotaDto(
    Guid Id,
    Guid NotaMedicaDocumentoId,
    string Texto,
    Guid AutorUserId,
    string? AutorEmail,
    DateTimeOffset CreatedAt,
    bool Archivada,
    Guid? ArchivadaPorUserId,
    string? ArchivadaPorEmail,
    DateTimeOffset? ArchivadaEn);

/// <summary>
/// Notas de trabajo sobre documentos del paciente. Append-only por diseno
/// (ver <see cref="Visal.Domain.Entities.DocumentoNota"/>): no hay Eliminar,
/// solo Archivar/Desarchivar.
/// </summary>
public interface IDocumentoNotaService
{
    /// <summary>Lista las notas de un documento ordenadas por fecha desc. Filtra
    /// por Archivada segun <paramref name="incluirArchivadas"/>.</summary>
    Task<IReadOnlyList<DocumentoNotaDto>> ListarPorDocumentoAsync(
        Guid documentoId, bool incluirArchivadas, CancellationToken ct = default);

    /// <summary>Agrega una nota. El texto se trimea; si queda vacio se rechaza.</summary>
    Task<DocumentoNotaDto> AgregarAsync(
        Guid documentoId, string texto, Guid autorUserId, CancellationToken ct = default);

    /// <summary>Marca la nota como archivada. Idempotente si ya estaba archivada.</summary>
    Task<bool> ArchivarAsync(Guid notaId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Reversa el archivado. Idempotente si estaba activa.</summary>
    Task<bool> DesarchivarAsync(Guid notaId, Guid actorUserId, CancellationToken ct = default);
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Nota que un usuario toma sobre un archivo/documento adjunto del paciente
/// (task ADM2). Append-only: no se puede editar ni borrar. Si el autor se
/// arrepiente marca la nota como <see cref="Archivada"/> — la fila queda para
/// trazabilidad pero se oculta de la vista principal.
///
/// Aplica a cualquier <see cref="NotaMedicaDocumento"/>: los que subes libres
/// desde /admision, los adjuntos de una nota medica, y los archivos externos
/// de una HC. El modal visor los reune sin importar el origen.
/// </summary>
public class DocumentoNota : TenantEntity
{
    /// <summary>Documento sobre el que se toma la nota. FK a <see cref="NotaMedicaDocumento"/>.</summary>
    public Guid NotaMedicaDocumentoId { get; set; }

    /// <summary>Texto libre. No hay limite duro; el UI recomienda ser conciso.</summary>
    public string Texto { get; set; } = "";

    /// <summary>Usuario que escribio la nota (PlatformUser.Id). Se registra al crear.</summary>
    public Guid AutorUserId { get; set; }

    /// <summary>
    /// Nota archivada: sigue en BD pero no aparece en la lista principal del
    /// visor. Un usuario con permiso puede archivar/desarchivar; nunca eliminar.
    /// </summary>
    public bool Archivada { get; set; }

    /// <summary>Usuario que archivo la nota. Null si nunca se archivo.</summary>
    public Guid? ArchivadaPorUserId { get; set; }

    /// <summary>Timestamp del archivado. Null si nunca se archivo.</summary>
    public DateTimeOffset? ArchivadaEn { get; set; }
}

using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Bitacora de cada correo procesado por la ingesta de PQR. Sirve de auditoria y de dedup:
/// el par (ConfigId, MessageId) es unico, asi un correo nunca se clasifica ni se convierte en
/// tarjeta dos veces. TENANT-SCOPED. Append-only.
/// </summary>
public class EmailIngestLog : TenantEntity
{
    /// <summary>Buzon (TenantEmailIngestConfig) del que provino el correo.</summary>
    public Guid ConfigId { get; set; }

    /// <summary>Message-ID RFC del correo (clave de dedup).</summary>
    public string MessageId { get; set; } = null!;

    /// <summary>UID IMAP del mensaje en la carpeta (informativo).</summary>
    public long? ImapUid { get; set; }

    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? Subject { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }

    public EmailIngestResultTipo Resultado { get; set; }

    /// <summary>Tarjeta creada (si Resultado = CreadaPqr).</summary>
    public Guid? TaskCardId { get; set; }

    /// <summary>Tipo PQRS-F que asigno el agente (Peticion/Queja/Reclamo/...).</summary>
    public string? TipoPqrs { get; set; }

    /// <summary>JSON crudo devuelto por el agente clasificador (auditoria).</summary>
    public string? ClassificationJson { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Tokens de entrada que consumio el agente clasificador con este correo (0 si no aplico IA).</summary>
    public int InputTokens { get; set; }

    /// <summary>Tokens de salida que produjo el agente clasificador con este correo.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Cantidad de adjuntos del correo que se copiaron a la tarjeta PQR.</summary>
    public int AttachmentCount { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

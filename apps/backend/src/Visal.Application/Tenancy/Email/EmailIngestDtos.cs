using Visal.Domain.Enums;

namespace Visal.Application.Tenancy.Email;

/// <summary>Buzon de ingesta como se muestra en la UI (sin exponer la clave).</summary>
public sealed record EmailIngestConfigDto(
    Guid Id,
    string Nombre,
    string EmailAddress,
    string ImapHost,
    int ImapPort,
    bool ImapUseSsl,
    string Folder,
    Guid? ClassifierAgentId,
    string? ClassifierAgentName,
    Guid? TargetBoardId,
    string? TargetBoardName,
    int PollIntervalMinutes,
    int LookbackDays,
    int MaxPorCorrida,
    bool OnlyUnread,
    bool MarkAsRead,
    string? ProcessedLabel,
    bool IsEnabled,
    bool HasAppPassword,
    DateTimeOffset? LastPolledAt,
    string? LastError,
    string? LastResultSummary);

/// <summary>Alta/edicion de un buzon. AppPassword null = conservar la actual.</summary>
public sealed record SaveEmailIngestConfigRequest(
    Guid? Id,
    string Nombre,
    string EmailAddress,
    string? AppPassword,
    string ImapHost,
    int ImapPort,
    bool ImapUseSsl,
    string Folder,
    Guid? ClassifierAgentId,
    Guid? TargetBoardId,
    Guid? TargetColumnId,
    int PollIntervalMinutes,
    int LookbackDays,
    int MaxPorCorrida,
    bool OnlyUnread,
    bool MarkAsRead,
    string? ProcessedLabel,
    bool IsEnabled);

/// <summary>Renglon de la bitacora de correos procesados.</summary>
public sealed record EmailIngestLogDto(
    Guid Id,
    string? FromAddress,
    string? Subject,
    DateTimeOffset? ReceivedAt,
    EmailIngestResultTipo Resultado,
    string? TipoPqrs,
    Guid? TaskCardId,
    string? ErrorMessage,
    int InputTokens,
    int OutputTokens,
    int AttachmentCount,
    DateTimeOffset ProcessedAt);

/// <summary>Resumen de una corrida de procesamiento (para el boton "Procesar ahora").</summary>
public sealed record EmailIngestRunResult(
    bool Ok,
    int Leidos,
    int Creados,
    int Descartados,
    int Errores,
    int Duplicados,
    string? Error,
    int Adjuntos = 0,
    long Tokens = 0);

/// <summary>ABM de buzones de ingesta de PQR + bitacora + acciones (probar conexion, procesar ahora).</summary>
public interface IEmailIngestConfigService
{
    Task<IReadOnlyList<EmailIngestConfigDto>> ListAsync(CancellationToken ct = default);
    Task<EmailIngestConfigDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> SaveAsync(SaveEmailIngestConfigRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<(bool Ok, string? Error, int Total)> TestConnectionAsync(Guid id, CancellationToken ct = default);
    Task<EmailIngestRunResult> ProcessNowAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EmailIngestLogDto>> ListLogsAsync(Guid configId, int take = 100, CancellationToken ct = default);

    /// <summary>Lista las etiquetas EXISTENTES de la cuenta (para elegir cual marca "procesado").
    /// Usa la clave guardada del buzon; requiere que ya se haya guardado la App Password.</summary>
    Task<(bool Ok, string? Error, IReadOnlyList<string> Labels)> ListarEtiquetasAsync(Guid id, CancellationToken ct = default);
}

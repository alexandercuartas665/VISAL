namespace Visal.Application.Tenancy.Email;

/// <summary>Contexto para responder el correo de una tarjeta PQR: a quien, con que asunto y desde
/// que buzon. <c>Puede</c> es false cuando la tarjeta no tiene un correo asociado o el buzon no
/// tiene credencial para enviar.</summary>
public sealed record PqrReplyContextDto(
    Guid CardId,
    bool Puede,
    string? Motivo,
    string? ToAddress,
    string? ToName,
    string Subject,
    string? MailboxEmail,
    DateTimeOffset? OriginalRecibidoEn,
    string? OriginalAsunto);

/// <summary>Adjunto para la respuesta (nombre + tipo MIME + bytes).</summary>
public sealed record PqrReplyAttachment(string FileName, string? ContentType, byte[] Bytes);

/// <summary>Plantilla pregrabada de respuesta.</summary>
public sealed record PqrPlantillaDto(Guid Id, string Nombre, string? Asunto, string Cuerpo);

/// <summary>Alta/edicion de una plantilla de respuesta.</summary>
public sealed record SavePqrPlantillaRequest(Guid? Id, string Nombre, string? Asunto, string Cuerpo);

/// <summary>
/// Responder desde el sistema el correo de PQR que origino una tarjeta del tablero: resuelve el
/// correo original (via <c>EmailIngestLog</c>), envia por SMTP con el MISMO buzon (enhebrado como
/// "Re:"), registra la respuesta como actividad de la tarjeta, y gestiona plantillas pregrabadas.
/// </summary>
public interface IPqrEmailReplyService
{
    /// <summary>Resuelve a quien responder y desde que buzon. Puede=false si no hay correo asociado.</summary>
    Task<PqrReplyContextDto> GetReplyContextAsync(Guid cardId, CancellationToken ct = default);

    /// <summary>Envia la respuesta (texto + adjuntos), enhebrada, y la registra en la tarjeta.</summary>
    Task<(bool Ok, string? Error)> ResponderAsync(
        Guid cardId, string asunto, string cuerpo, IReadOnlyList<PqrReplyAttachment> adjuntos,
        Guid actor, string actorDisplayName, CancellationToken ct = default);

    // ---- Plantillas pregrabadas ----
    Task<IReadOnlyList<PqrPlantillaDto>> ListPlantillasAsync(CancellationToken ct = default);
    Task<Guid> GuardarPlantillaAsync(SavePqrPlantillaRequest req, CancellationToken ct = default);
    Task EliminarPlantillaAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Parametros de envio SMTP (transporte de bajo nivel, implementado en Infrastructure).</summary>
public sealed record SmtpReplyParams(
    string Host, int Port, bool UseSsl,
    string Username, string Password,
    string FromEmail, string? FromName,
    string ToEmail, string? ToName,
    string Subject, string BodyText,
    string? InReplyToMessageId,
    IReadOnlyList<PqrReplyAttachment> Attachments);

/// <summary>Cliente SMTP MailKit que envia una respuesta con el buzon del tenant. Nunca lanza:
/// devuelve (Ok, Error). No loggea la clave.</summary>
public interface IPqrEmailReplySender
{
    Task<(bool Ok, string? Error)> SendAsync(SmtpReplyParams p, CancellationToken ct = default);
}

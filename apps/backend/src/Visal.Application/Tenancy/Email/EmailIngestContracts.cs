namespace Visal.Application.Tenancy.Email;

/// <summary>Correo entrante leido de un buzon IMAP.</summary>
public sealed record IncomingEmail(
    string MessageId,
    long Uid,
    string? FromAddress,
    string? FromName,
    string Subject,
    DateTimeOffset ReceivedAt,
    string BodyText);

/// <summary>Parametros de conexion IMAP para una corrida de lectura. La clave llega descifrada.</summary>
public sealed record ImapConnectionParams(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string Password,
    string Folder,
    bool OnlyUnread,
    int MaxMessages,
    DateTimeOffset? Since);

/// <summary>
/// Lector IMAP: conecta a un buzon y trae los correos nuevos. La implementacion (MailKit) vive en
/// Infrastructure. No persiste ni loggea la clave.
/// </summary>
public interface IImapEmailReader
{
    /// <summary>Trae los correos que cumplen el filtro (no leidos / desde fecha), hasta MaxMessages.</summary>
    Task<IReadOnlyList<IncomingEmail>> FetchAsync(ImapConnectionParams p, CancellationToken ct = default);

    /// <summary>Marca como leidos (\Seen) los mensajes indicados por UID.</summary>
    Task MarkSeenAsync(ImapConnectionParams p, IReadOnlyList<long> uids, CancellationToken ct = default);

    /// <summary>Prueba de conexion/login. Devuelve ok + cuantos mensajes hay en la carpeta (o el error).</summary>
    Task<(bool Ok, string? Error, int Total)> TestConnectionAsync(ImapConnectionParams p, CancellationToken ct = default);

    /// <summary>Lista las etiquetas/carpetas EXISTENTES de la cuenta (para que el usuario elija cual usar
    /// como marca de "procesado"). Excluye carpetas de sistema (Enviados, Papelera, etc.).</summary>
    Task<IReadOnlyList<string>> ListLabelsAsync(ImapConnectionParams p, CancellationToken ct = default);

    /// <summary>Aplica la etiqueta (Gmail label) a los mensajes indicados por UID. En Gmail usa la
    /// extension X-GM-LABELS; si el servidor no la soporta, lanza un error claro.</summary>
    Task AddLabelAsync(ImapConnectionParams p, IReadOnlyList<long> uids, string label, CancellationToken ct = default);
}

/// <summary>Salida estructurada del agente clasificador sobre un correo.</summary>
public sealed record EmailPqrClassification(
    bool EsPqr,
    string? TipoPqrs,
    string? ServicioPqrs,
    string? DescripcionPqrsf,
    string? NombresUsuario,
    string? Identificacion,
    string? Celular,
    string? Email,
    string? AtributoCalidad,
    string RawJson);

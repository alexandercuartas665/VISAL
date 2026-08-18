using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Visal.Application.Tenancy.Email;

namespace Visal.Infrastructure.Email;

/// <summary>
/// Lector IMAP con MailKit. Conecta con SSL/TLS, autentica con la App Password (Gmail exige App
/// Password + 2FA), busca correos nuevos y los devuelve normalizados. No persiste ni loggea la clave.
/// </summary>
public sealed class MailKitImapReader : IImapEmailReader
{
    public async Task<IReadOnlyList<IncomingEmail>> FetchAsync(ImapConnectionParams p, CancellationToken ct = default)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(p.Host, p.Port, p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(p.Username, p.Password, ct);

        var folder = await OpenFolderAsync(client, p.Folder, FolderAccess.ReadOnly, ct);

        SearchQuery query = SearchQuery.All;
        if (p.OnlyUnread) { query = SearchQuery.NotSeen; }
        if (p.Since is { } since)
        {
            var delivered = SearchQuery.DeliveredAfter(since.UtcDateTime);
            query = ReferenceEquals(query, SearchQuery.All) ? delivered : query.And(delivered);
        }

        var uids = await folder.SearchAsync(query, ct);
        // Los mas recientes primero para respetar el tope por corrida.
        var take = uids.OrderByDescending(u => u.Id).Take(Math.Max(1, p.MaxMessages)).OrderBy(u => u.Id).ToList();

        var result = new List<IncomingEmail>(take.Count);
        foreach (var uid in take)
        {
            ct.ThrowIfCancellationRequested();
            var msg = await folder.GetMessageAsync(uid, ct);
            var from = msg.From.Mailboxes.FirstOrDefault();
            var body = msg.TextBody;
            if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(msg.HtmlBody)) { body = HtmlToText(msg.HtmlBody); }
            result.Add(new IncomingEmail(
                MessageId: string.IsNullOrWhiteSpace(msg.MessageId) ? $"uid-{uid.Id}@{p.Host}" : msg.MessageId,
                Uid: uid.Id,
                FromAddress: from?.Address,
                FromName: from?.Name,
                Subject: msg.Subject ?? "(sin asunto)",
                ReceivedAt: msg.Date,
                BodyText: body ?? ""));
        }

        await client.DisconnectAsync(true, ct);
        return result;
    }

    public async Task MarkSeenAsync(ImapConnectionParams p, IReadOnlyList<long> uids, CancellationToken ct = default)
    {
        if (uids.Count == 0) { return; }
        using var client = new ImapClient();
        await client.ConnectAsync(p.Host, p.Port, p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(p.Username, p.Password, ct);
        var folder = await OpenFolderAsync(client, p.Folder, FolderAccess.ReadWrite, ct);
        var ids = uids.Select(u => new UniqueId((uint)u)).ToList();
        await folder.AddFlagsAsync(ids, MessageFlags.Seen, true, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task<(bool Ok, string? Error, int Total)> TestConnectionAsync(ImapConnectionParams p, CancellationToken ct = default)
    {
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(p.Host, p.Port, p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(p.Username, p.Password, ct);
            var folder = await OpenFolderAsync(client, p.Folder, FolderAccess.ReadOnly, ct);
            var total = folder.Count;
            await client.DisconnectAsync(true, ct);
            return (true, null, total);
        }
        catch (AuthenticationException)
        {
            return (false, "Autenticacion rechazada. Verifica el correo y la App Password (requiere 2FA activo; la clave normal no sirve para IMAP).", 0);
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo conectar: {ex.Message}", 0);
        }
    }

    public async Task<IReadOnlyList<string>> ListLabelsAsync(ImapConnectionParams p, CancellationToken ct = default)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(p.Host, p.Port, p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(p.Username, p.Password, ct);

        // Excluir carpetas de sistema (Gmail las expone como carpetas especiales) para que el
        // usuario solo vea sus etiquetas reales.
        const FolderAttributes especiales = FolderAttributes.All | FolderAttributes.Trash | FolderAttributes.Junk
            | FolderAttributes.Sent | FolderAttributes.Drafts | FolderAttributes.Important
            | FolderAttributes.Flagged | FolderAttributes.Inbox | FolderAttributes.NoSelect
            | FolderAttributes.NonExistent;

        var labels = new List<string>();
        foreach (var ns in client.PersonalNamespaces)
        {
            foreach (var f in await client.GetFoldersAsync(ns, false, ct))
            {
                if ((f.Attributes & especiales) != 0) { continue; }
                if (string.IsNullOrWhiteSpace(f.FullName)) { continue; }
                labels.Add(f.FullName);
            }
        }
        await client.DisconnectAsync(true, ct);
        return labels.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task AddLabelAsync(ImapConnectionParams p, IReadOnlyList<long> uids, string label, CancellationToken ct = default)
    {
        if (uids.Count == 0 || string.IsNullOrWhiteSpace(label)) { return; }
        using var client = new ImapClient();
        await client.ConnectAsync(p.Host, p.Port, p.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(p.Username, p.Password, ct);
        if (!client.Capabilities.HasFlag(ImapCapabilities.GMailExt1))
        {
            await client.DisconnectAsync(true, ct);
            throw new NotSupportedException("El servidor no soporta etiquetas de Gmail (X-GM-LABELS). El etiquetado solo aplica a cuentas Gmail/Workspace.");
        }
        var folder = await OpenFolderAsync(client, p.Folder, FolderAccess.ReadWrite, ct);
        var ids = uids.Select(u => new UniqueId((uint)u)).ToList();
        await folder.AddLabelsAsync(ids, new[] { label }, true, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder> OpenFolderAsync(ImapClient client, string folderName, FolderAccess access, CancellationToken ct)
    {
        IMailFolder folder = string.IsNullOrWhiteSpace(folderName) || folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(folderName, ct);
        await folder.OpenAsync(access, ct);
        return folder;
    }

    private static string HtmlToText(string html)
    {
        var noTags = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        noTags = Regex.Replace(noTags, "<[^>]+>", " ");
        noTags = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(noTags, @"\s+", " ").Trim();
    }
}

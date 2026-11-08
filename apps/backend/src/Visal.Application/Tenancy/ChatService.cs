using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class ChatService : IChatService
{
    // Ola 4c estabilizacion (P0): contexto CORTO por operacion (using var db = _dbFactory.Create();)
    // en vez del IApplicationDbContext scoped. El chat renderiza y recibe pushes SignalR en vivo
    // (mensajes entrantes), donde una consulta en vuelo se solapaba con otra -> "second operation".
    // Los helpers privados (SendCoreAsync, ResolveSenderAsync) reciben el MISMO db por parametro
    // para mantener la Conversation trackeada y guardar todo en un solo contexto.
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly TimeProvider _timeProvider;

    public ChatService(IApplicationDbContextFactory dbFactory, ITenantContext tenantContext, IWhatsAppConnectorService connector, IChatBroadcaster broadcaster, TimeProvider timeProvider)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _connector = connector;
        _broadcaster = broadcaster;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        return await db.Conversations
            .AsNoTracking()
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new ConversationDto(c.Id, c.ContactPhone, c.ContactName, c.LeadId, c.LastMessageAt, c.WhatsAppLineId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        return await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.ConversationId, m.Direction, m.Body, m.MessageType, m.SentAt, m.MediaType, m.MediaUrl, m.MediaMimeType, m.SentByName))
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageDto?> SendAsync(Guid conversationId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var message = new Message
        {
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Body = body,
            MessageType = "text",
            SentAt = now
        };
        db.Messages.Add(message);
        conversation.LastMessageAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body, message.MessageType, message.SentAt);
    }

    public async Task<ConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null || string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            return null;
        }
        var phone = Digits(lead.ContactPhone);

        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.LeadId == leadId || c.ContactPhone == phone, cancellationToken);
        if (conv is null)
        {
            if (_tenantContext.TenantId is not Guid tenantId)
            {
                return null;
            }
            conv = new Conversation { TenantId = tenantId, ContactPhone = phone, ContactName = lead.ContactName, LeadId = leadId };
            db.Conversations.Add(conv);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (conv.LeadId is null)
        {
            conv.LeadId = leadId;
            await db.SaveChangesAsync(cancellationToken);
        }
        return new ConversationDto(conv.Id, conv.ContactPhone, conv.ContactName, conv.LeadId, conv.LastMessageAt, conv.WhatsAppLineId);
    }

    public async Task<ConversationDto?> GetOrCreateByPhoneAsync(string telefono, string? contactName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(telefono)) { return null; }
        var phone = Digits(telefono);
        if (phone.Length == 0) { return null; }

        using var db = _dbFactory.Create();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.ContactPhone == phone, cancellationToken);
        if (conv is null)
        {
            if (_tenantContext.TenantId is not Guid tenantId) { return null; }
            conv = new Conversation { TenantId = tenantId, ContactPhone = phone, ContactName = contactName };
            db.Conversations.Add(conv);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(contactName) && string.IsNullOrWhiteSpace(conv.ContactName))
        {
            conv.ContactName = contactName;
            await db.SaveChangesAsync(cancellationToken);
        }
        return new ConversationDto(conv.Id, conv.ContactPhone, conv.ContactName, conv.LeadId, conv.LastMessageAt, conv.WhatsAppLineId);
    }

    public async Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid lineId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatSendResult(false, null, "El mensaje esta vacio.");
        }
        using var db = _dbFactory.Create();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }
        return await SendCoreAsync(db, conv, lineId, body, actorUserId, cancellationToken);
    }

    /// <summary>Variante "trusted" para servicios que operan sin tenant scope
    /// (webhook Gupshup, cron). El caller ya valido a que tenant pertenece la
    /// conversacion (via el token de la linea), asi que aqui solo confirmamos
    /// la coincidencia por (Id + TenantId) para evitar fuga entre tenants.</summary>
    public async Task<ChatSendResult> SendViaLineTrustedAsync(Guid tenantId, Guid conversationId, Guid lineId, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatSendResult(false, null, "El mensaje esta vacio.");
        }
        using var db = _dbFactory.Create();
        var conv = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }
        // actorUserId = Guid.Empty: es un envio automatico del sistema, sin
        // profesional atras. SentByName queda como "Sistema" en el chat.
        return await SendCoreAsync(db, conv, lineId, body, Guid.Empty, cancellationToken);
    }

    /// <summary>Nucleo del envio outbound compartido por SendViaLineAsync y
    /// SendViaLineTrustedAsync. Recibe el contexto corto y la Conversation ya cargada
    /// (trackeada por ese mismo contexto, para que el SaveChanges persista sus cambios).</summary>
    private async Task<ChatSendResult> SendCoreAsync(IApplicationDbContext db, Conversation conv, Guid lineId, string body, Guid actorUserId, CancellationToken cancellationToken)
    {
        // Envio real por la linea elegida (Evolution).
        var send = await _connector.SendTestAsync(lineId, conv.ContactPhone, body, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(db, actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = body.Trim(),
            MessageType = "text",
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<ChatSendResult> SendMediaViaLineAsync(Guid conversationId, Guid lineId, MessageMediaType mediaType, string base64, string localUrl, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (mediaType == MessageMediaType.None || string.IsNullOrWhiteSpace(base64))
        {
            return new ChatSendResult(false, null, "Adjunto invalido.");
        }
        using var db = _dbFactory.Create();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        var send = await _connector.SendMediaAsync(lineId, conv.ContactPhone, mediaType, base64, mimeType, fileName, caption, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(db, actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = caption?.Trim() ?? "",
            MessageType = mediaType.ToString().ToLowerInvariant(),
            MediaType = mediaType,
            MediaUrl = localUrl,
            MediaMimeType = mimeType,
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<ChatSendResult> SendLocationViaLineAsync(Guid conversationId, Guid lineId, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        var send = await _connector.SendLocationAsync(lineId, conv.ContactPhone, latitude, longitude, name, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(db, actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = string.IsNullOrWhiteSpace(name) ? "Ubicacion" : name!.Trim(),
            MessageType = "location",
            MediaType = MessageMediaType.Location,
            MediaUrl = $"{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, null, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<MessageDto?> AddNoticeAsync(Guid conversationId, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        using var db = _dbFactory.Create();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return null;
        }
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = body.Trim(),
            MessageType = "notice",
            SentAt = now
        };
        db.Messages.Add(msg);
        conv.LastMessageAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyDictionary<string, LeadChatStateDto>> GetUnansweredByPhoneAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var convs = await db.Conversations.AsNoTracking()
            .Select(c => new { c.Id, c.ContactPhone })
            .ToListAsync(cancellationToken);
        if (convs.Count == 0) { return new Dictionary<string, LeadChatStateDto>(); }

        var msgs = await db.Messages.AsNoTracking()
            .Select(m => new { m.ConversationId, m.Direction, m.SentAt })
            .ToListAsync(cancellationToken);
        var byConv = msgs.GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<string, LeadChatStateDto>();
        foreach (var c in convs)
        {
            if (!byConv.TryGetValue(c.Id, out var list)) { continue; }
            var lastOut = list.Where(m => m.Direction == MessageDirection.Outbound)
                .Select(m => (DateTimeOffset?)m.SentAt).Max();
            var unanswered = list.Where(m => m.Direction == MessageDirection.Inbound && (lastOut is null || m.SentAt > lastOut)).ToList();
            if (unanswered.Count == 0) { continue; }

            var key = Digits(c.ContactPhone);
            if (string.IsNullOrEmpty(key)) { continue; }
            result[key] = new LeadChatStateDto(unanswered.Count, unanswered.Min(m => m.SentAt));
        }
        return result;
    }

    // Resuelve el asesor (TenantUser) a partir del PlatformUser autenticado, con nombre para mostrar.
    private async Task<(Guid? Id, string? Name)> ResolveSenderAsync(IApplicationDbContext db, Guid actorPlatformUserId, Guid tenantId, CancellationToken ct)
    {
        if (actorPlatformUserId == Guid.Empty) { return (null, null); }
        var tu = await db.TenantUsers
            .IgnoreQueryFilters()
            .Include(u => u.PlatformUser)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.PlatformUserId == actorPlatformUserId, ct);
        if (tu is null) { return (null, null); }
        var name = string.IsNullOrWhiteSpace(tu.PlatformUser?.DisplayName) ? tu.Email : tu.PlatformUser!.DisplayName;
        return (tu.Id, name);
    }

    // Extrae solo los digitos y trunca al ancho de la columna (varchar(40)) para
    // que ningun caller -- con telefonos concatenados, multiples numeros o codigos
    // de pais duplicados -- reviente el INSERT a conversations con "value too long
    // for type character varying(40)". Precedente: paciente con 4 celulares en un
    // solo campo separados por " - " (2026-07-24).
    private const int MaxPhoneLength = 40;
    private static string Digits(string s)
    {
        var d = new string(s.Where(char.IsDigit).ToArray());
        return d.Length > MaxPhoneLength ? d.Substring(0, MaxPhoneLength) : d;
    }
}

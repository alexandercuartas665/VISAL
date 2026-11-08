using System.Security.Cryptography;
using System.Text;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class ChatIngestService : IChatIngestService
{
    // Ola 4c estabilizacion (P0): contexto CORTO por operacion (using var db = _dbFactory.Create();)
    // en vez del IApplicationDbContext scoped.
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly TimeProvider _timeProvider;

    public ChatIngestService(IApplicationDbContextFactory dbFactory, ISecretProtector secretProtector, IChatBroadcaster broadcaster, TimeProvider timeProvider)
    {
        _dbFactory = dbFactory;
        _secretProtector = secretProtector;
        _broadcaster = broadcaster;
        _timeProvider = timeProvider;
    }

    public async Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        // Sin JWT: validamos el token del webhook contra la config Evolution del tenant.
        var config = await db.TenantEvolutionConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null || string.IsNullOrEmpty(config.ApiTokenEncrypted)
            || string.IsNullOrEmpty(providedToken) || !TokenMatches(config.ApiTokenEncrypted, providedToken))
        {
            return ChatIngestResult.Unauthorized;
        }

        return await IngestTrustedAsync(tenantId, payload, cancellationToken);
    }

    // Persiste un entrante ya autorizado por el llamador (webhook crudo de Evolution validado
    // con token global + instancia conocida). Mantiene idempotencia y difusion en tiempo real.
    public async Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        // Idempotencia por id externo.
        var duplicate = await db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ExternalId == payload.ExternalMessageId, cancellationToken);
        if (duplicate)
        {
            return ChatIngestResult.Duplicate;
        }

        var phone = payload.ContactPhone.Trim();
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ContactPhone == phone, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var sentAt = payload.SentAt ?? now;

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = phone,
                ContactName = payload.ContactName?.Trim(),
                LastMessageAt = sentAt
            };
            db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = sentAt;
            if (conversation.ContactName is null && payload.ContactName is not null)
            {
                conversation.ContactName = payload.ContactName.Trim();
            }
        }

        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            ExternalId = payload.ExternalMessageId,
            Body = payload.Body,
            MessageType = string.IsNullOrWhiteSpace(payload.MessageType) ? "text" : payload.MessageType!.Trim(),
            SentAt = sentAt
        };
        db.Messages.Add(message);

        await db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body,
            message.MessageType, message.SentAt, message.MediaType, message.MediaUrl, message.MediaMimeType, message.SentByName);
        await _broadcaster.MessageAddedAsync(tenantId, conversation.Id, dto, cancellationToken);

        return ChatIngestResult.Accepted;
    }

    private bool TokenMatches(string encrypted, string provided)
    {
        string actual;
        try
        {
            actual = _secretProtector.Unprotect(encrypted);
        }
        catch
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(provided));
    }
}

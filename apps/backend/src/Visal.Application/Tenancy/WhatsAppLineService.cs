using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class WhatsAppLineService : IWhatsAppLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppLineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _db.WhatsAppLines
            .AsNoTracking()
            .OrderBy(l => l.InstanceName)
            .Select(l => new WhatsAppLineDto(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt, l.Provider, l.GupshupAppId, l.InboundToken))
            .ToListAsync(cancellationToken);
    }

    public async Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var line = new WhatsAppLine
        {
            TenantId = tenantId,
            InstanceName = request.InstanceName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Status = WhatsAppLineStatus.Created,
            LastStatusAt = _timeProvider.GetUtcNow(),
            Provider = request.Provider,
            GupshupAppId = request.Provider == WhatsAppProvider.Gupshup ? request.GupshupAppId : null,
            // Lineas Gupshup nacen con un token de webhook opaco. Se genera
            // aca (base64url 32 bytes ~ 43 chars) para que la UI pueda mostrar
            // ya la URL a copiar sin viajes extra. Regenerable si se filtra.
            InboundToken = request.Provider == WhatsAppProvider.Gupshup ? GenerateInboundToken() : null,
        };
        _db.WhatsAppLines.Add(line);

        _audit.Write(actorUserId, "whatsapp-line.create", nameof(WhatsAppLine), line.Id,
            previousValue: null,
            newValue: new { line.InstanceName, line.PhoneNumber, provider = line.Provider.ToString() },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    /// <summary>Token opaco base64url ~43 chars, criptograficamente aleatorio.
    /// Sirve para el path del webhook Gupshup: /webhooks/gupshup/{token}.</summary>
    private static string GenerateInboundToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public async Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        var previous = line.Status;
        if (previous != status)
        {
            var now = _timeProvider.GetUtcNow();
            line.Status = status;
            line.LastStatusAt = now;
            if (status == WhatsAppLineStatus.Connected)
            {
                line.LastConnectedAt = now;
            }

            _audit.Write(actorUserId, "whatsapp-line.change-status", nameof(WhatsAppLine), line.Id,
                previousValue: new { Status = previous },
                newValue: new { Status = status },
                tenantId: line.TenantId);

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(line);
    }

    public async Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var line = await _db.WhatsAppLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return null;
        }

        if (tenantUserId is Guid userId)
        {
            // El filtro global garantiza que solo se valida contra usuarios del tenant activo.
            var belongs = await _db.TenantUsers.AnyAsync(tu => tu.Id == userId, cancellationToken);
            if (!belongs)
            {
                return null;
            }
        }

        line.AssignedToTenantUserId = tenantUserId;
        _audit.Write(actorUserId, "whatsapp-line.assign", nameof(WhatsAppLine), line.Id,
            previousValue: null,
            newValue: new { AssignedToTenantUserId = tenantUserId },
            tenantId: line.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(line);
    }

    private static WhatsAppLineDto Map(WhatsAppLine l) =>
        new(l.Id, l.InstanceName, l.PhoneNumber, l.Status, l.AssignedToTenantUserId, l.LastConnectedAt, l.LastStatusAt, l.Provider, l.GupshupAppId, l.InboundToken);
}

using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

public sealed record WhatsAppLineDto(
    Guid Id,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastStatusAt,
    // Multi-proveedor: la UI muestra badge y decide que panel de config abrir.
    WhatsAppProvider Provider = WhatsAppProvider.Evolution,
    Guid? GupshupAppId = null,
    string? InboundToken = null);

public sealed record CreateWhatsAppLineRequest(
    string InstanceName,
    string? PhoneNumber = null,
    WhatsAppProvider Provider = WhatsAppProvider.Evolution,
    Guid? GupshupAppId = null);

public sealed record ChangeLineStatusRequest(WhatsAppLineStatus Status);

public sealed record AssignLineRequest(Guid? TenantUserId);

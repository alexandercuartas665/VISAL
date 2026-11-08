using Visal.Application.Common;
using Visal.Application.Common.Auth;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

// Ola 4c (P0): contexto corto por operacion (using var db = ...Create();) en vez del IApplicationDbContext scoped.
public sealed class TenantUserService : ITenantUserService
{
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;

    public TenantUserService(
        IApplicationDbContextFactory dbFactory,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IAuditWriter audit)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        // El filtro global del DbContext limita por el tenant del contexto.
        return await db.TenantUsers
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new TenantUserDto(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantUserDto?> InviteAsync(InviteTenantUserRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var platformUser = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (platformUser is null)
        {
            platformUser = new PlatformUser
            {
                Email = email,
                DisplayName = request.DisplayName?.Trim(),
                EmailVerified = false,
                AuthProvider = "local",
                Status = string.IsNullOrEmpty(request.Password) ? PlatformUserStatus.Invited : PlatformUserStatus.Active,
                PasswordHash = string.IsNullOrEmpty(request.Password) ? null : _passwordHasher.Hash(request.Password)
            };
            db.PlatformUsers.Add(platformUser);
        }

        // Filtro global: solo ve miembros del tenant activo.
        var alreadyMember = await db.TenantUsers.AnyAsync(tu => tu.PlatformUserId == platformUser.Id, cancellationToken);
        if (alreadyMember)
        {
            return null;
        }

        var tenantUser = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = platformUser.Id,
            Email = email,
            TenantRole = request.Role,
            Status = PlatformUserStatus.Active
        };
        db.TenantUsers.Add(tenantUser);

        _audit.Write(db, actorUserId, "tenant-user.invite", nameof(TenantUser), tenantUser.Id,
            previousValue: null,
            newValue: new { email, request.Role },
            tenantId: tenantId);

        await db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser);
    }

    public async Task<TenantUserDto?> ChangeRoleAsync(Guid tenantUserId, TenantRole role, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var tenantUser = await db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var previous = tenantUser.TenantRole;
        if (previous != role)
        {
            tenantUser.TenantRole = role;
            _audit.Write(db, actorUserId, "tenant-user.change-role", nameof(TenantUser), tenantUser.Id,
                previousValue: new { Role = previous },
                newValue: new { Role = role },
                tenantId: tenantUser.TenantId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Map(tenantUser);
    }

    public async Task<TenantUserDto?> SetStatusAsync(Guid tenantUserId, PlatformUserStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.Create();
        var tenantUser = await db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var previous = tenantUser.Status;
        if (previous != status)
        {
            tenantUser.Status = status;
            _audit.Write(db, actorUserId, "tenant-user.set-status", nameof(TenantUser), tenantUser.Id,
                previousValue: new { Status = previous },
                newValue: new { Status = status },
                tenantId: tenantUser.TenantId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Map(tenantUser);
    }

    private static TenantUserDto Map(TenantUser u) =>
        new(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status);
}

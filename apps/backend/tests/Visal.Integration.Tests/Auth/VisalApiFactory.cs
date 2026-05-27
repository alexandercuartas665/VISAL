using Visal.Application.Common.Auth;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Visal.Integration.Tests.Auth;

/// <summary>
/// Levanta la Api real contra un PostgreSQL efimero (Testcontainers), aplica migraciones
/// y siembra datos para probar login, selector de tenant, politicas y aislamiento por JWT.
/// </summary>
public sealed class VisalApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public const string SigningKey = "visal-test-signing-key-must-be-long-enough-256bit-aaaa";
    public const string Password = "Secret123!";
    public const string SingleEmail = "single@visal.travels";
    public const string MultiEmail = "multi@visal.travels";
    public const string SuperEmail = "super@visal.travels";

    public Guid TenantAId { get; } = Guid.CreateVersion7();
    public Guid TenantBId { get; } = Guid.CreateVersion7();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Database:AutoMigrate", "false");
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        using var scope = Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        await ctx.Database.MigrateAsync();

        ctx.Tenants.AddRange(
            new Tenant { Id = TenantAId, Name = "Agencia A", Status = TenantStatus.Active },
            new Tenant { Id = TenantBId, Name = "Agencia B", Status = TenantStatus.Active });

        var single = new PlatformUser
        {
            Email = SingleEmail,
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            PasswordHash = hasher.Hash(Password)
        };
        var multi = new PlatformUser
        {
            Email = MultiEmail,
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            PasswordHash = hasher.Hash(Password)
        };
        var super = new PlatformUser
        {
            Email = SuperEmail,
            EmailVerified = true,
            Status = PlatformUserStatus.Active,
            PasswordHash = hasher.Hash(Password),
            PlatformRole = PlatformRole.SuperAdmin
        };
        ctx.PlatformUsers.AddRange(single, multi, super);

        ctx.TenantUsers.AddRange(
            new TenantUser { TenantId = TenantAId, PlatformUserId = single.Id, Email = SingleEmail, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active },
            new TenantUser { TenantId = TenantAId, PlatformUserId = multi.Id, Email = MultiEmail, TenantRole = TenantRole.Advisor, Status = PlatformUserStatus.Active },
            new TenantUser { TenantId = TenantBId, PlatformUserId = multi.Id, Email = MultiEmail, TenantRole = TenantRole.Admin, Status = PlatformUserStatus.Active });

        ctx.TenantConfigurations.AddRange(
            new TenantConfiguration { TenantId = TenantAId, ConfigKey = "tono", ConfigValue = "formal" },
            new TenantConfiguration { TenantId = TenantBId, ConfigKey = "tono", ConfigValue = "informal" },
            new TenantConfiguration { TenantId = TenantBId, ConfigKey = "horario", ConfigValue = "8-18" });

        await ctx.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}

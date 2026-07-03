using Visal.Application.Common;
using Visal.Application.Common.Auth;
using Visal.Infrastructure.Auth;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;
using Visal.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Visal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("VISAL_DB_CONNECTION");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Cadena de conexion 'Default' no configurada (usa ConnectionStrings:Default o VISAL_DB_CONNECTION).");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditableTenantInterceptor>();

        services.AddDbContext<VisalDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<VisalDbContext>());

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        // Llaves de Data Protection compartidas en la base de datos + nombre de aplicacion comun,
        // para que cualquier app (Api, SuperAdmin, Workers) descifre los secretos cifrados por otra.
        services.AddDataProtection()
            .SetApplicationName("Visal")
            .PersistKeysToDbContext<VisalDbContext>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        // Correo saliente via SMTP configurable por el Super Admin (clave cifrada).
        services.AddScoped<Application.Common.IEmailSender, Email.SmtpEmailSender>();
        services.AddHttpClient<Visal.Application.Admin.IWompiApiClient, Wompi.WompiApiClient>();
        services.AddHttpClient<Visal.Application.Admin.IEvolutionApiClient, Evolution.EvolutionApiClient>();
        services.AddHttpClient<Visal.Application.Admin.IGupshupApiClient, Gupshup.GupshupApiClient>();
        // Providers WhatsApp: resolver + implementaciones concretas (Evolution y
        // Gupshup). Scoped porque comparten DbContext scoped.
        services.AddScoped<WhatsApp.EvolutionWhatsAppProvider>();
        services.AddScoped<WhatsApp.GupshupWhatsAppProvider>();
        services.AddScoped<Application.Tenancy.WhatsApp.IWhatsAppProviderResolver, WhatsApp.WhatsAppProviderResolver>();
        services.AddHttpClient<Visal.Application.Tenancy.IAiProviderClient, Ai.AiProviderClient>();
        services.AddHttpClient<Visal.Application.Auth.IGoogleOAuthClient, Auth.GoogleOAuthClient>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<Visal.Application.Tenancy.ISqlConsoleService, Sql.SqlConsoleService>();
        services.AddHttpClient("api-colombia");
        services.AddScoped<Geo.ApiColombiaSeeder>();

        // Comprobantes PDF (QuestPDF). Licencia Community: gratis para empresas con ingresos < USD 1M/ano.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<Application.Common.IReceiptPdfRenderer, Pdf.QuestPdfReceiptRenderer>();
        // PDF de cotizaciones desde HTML libre (Chromium headless via PuppeteerSharp).
        services.AddScoped<Application.Common.IQuotePdfRenderer, Rendering.PuppeteerQuotePdfRenderer>();

        return services;
    }
}

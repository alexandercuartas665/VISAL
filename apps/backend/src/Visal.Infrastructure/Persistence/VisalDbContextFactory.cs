using Visal.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Visal.Infrastructure.Persistence;

/// <summary>
/// Factory para herramientas de diseno (dotnet ef). Permite crear el DbContext sin levantar
/// la aplicacion. La cadena real se toma de la variable de entorno VISAL_DB_CONNECTION;
/// el fallback es solo un placeholder local (sin secreto real) suficiente para generar migraciones.
/// </summary>
public sealed class VisalDbContextFactory : IDesignTimeDbContextFactory<VisalDbContext>
{
    public VisalDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("VISAL_DB_CONNECTION")
            ?? "Host=localhost;Port=5435;Database=visal_dev;Username=visal;Password=visal_local_2026";

        var options = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            // Mismo tratamiento que en runtime (ver Visal.Infrastructure/DependencyInjection):
            // EF 9 escala PendingModelChangesWarning a fatal cuando ve diferencias entre modelo
            // y snapshot. En dev a veces sembra manual y hay drift; bajarlo a warning para no
            // bloquear `dotnet ef database update`.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new VisalDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public Guid? SucursalId => null;
    }
}

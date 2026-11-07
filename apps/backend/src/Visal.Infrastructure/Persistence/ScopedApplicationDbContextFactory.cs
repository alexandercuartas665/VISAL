using Visal.Application.Common;
using Visal.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Visal.Infrastructure.Persistence;

/// <summary>
/// Configuracion compartida de las opciones de <see cref="VisalDbContext"/>, para que el
/// registro scoped (AddDbContext) y el factory por-operacion
/// (<see cref="ScopedApplicationDbContextFactory"/>) construyan el contexto IGUAL (mismo
/// proveedor, convencion de nombres, interceptor y warnings). Unica fuente de verdad.
/// </summary>
public static class VisalDbContextOptionsSetup
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString, AuditableTenantInterceptor interceptor)
    {
        options.UseNpgsql(connectionString)
               .UseSnakeCaseNamingConvention()
               .AddInterceptors(interceptor)
               // EF 9 lanza fatal PendingModelChangesWarning al iniciar MigrateAsync cuando cree
               // ver diferencias entre modelo compilado y snapshot; aqui a veces el snapshot va un
               // paso atras de la BD (migraciones marcadas a mano). Bajarlo a Warning.
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    public static DbContextOptions<VisalDbContext> Build(string connectionString, AuditableTenantInterceptor interceptor)
    {
        var builder = new DbContextOptionsBuilder<VisalDbContext>();
        Configure(builder, connectionString, interceptor);
        return builder.Options;
    }
}

/// <summary>
/// Factory SCOPED (una por circuito Blazor) que entrega un <see cref="VisalDbContext"/> NUEVO e
/// independiente por cada <see cref="Create"/>. Resuelve el <see cref="ITenantContext"/> y el
/// interceptor de auditoria del scope actual (el circuito), de modo que cada contexto creado
/// aplica el mismo filtro por TenantId y la misma auditoria que el contexto scoped clasico, pero
/// SIN compartir instancia entre operaciones que se solapan (cura del P0 "second operation").
///
/// Las opciones se construyen una sola vez por circuito (el interceptor y el tenant son los mismos
/// durante el circuito); EF cachea el modelo por la huella de las opciones, asi que crear contextos
/// es barato.
/// </summary>
internal sealed class ScopedApplicationDbContextFactory : IApplicationDbContextFactory
{
    private readonly DbContextOptions<VisalDbContext> _options;
    private readonly ITenantContext _tenant;

    public ScopedApplicationDbContextFactory(string connectionString, AuditableTenantInterceptor interceptor, ITenantContext tenant)
    {
        _tenant = tenant;
        _options = VisalDbContextOptionsSetup.Build(connectionString, interceptor);
    }

    public IApplicationDbContext Create() => new VisalDbContext(_options, _tenant);
}

using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Visal.Integration.Tests;

/// <summary>
/// Verifica el sistema de versionado automatico de form_definitions:
/// - Trigger Postgres dispara snapshot al UPDATE con campos relevantes.
/// - UPDATEs que solo tocan updated_at NO generan snapshot (IS DISTINCT FROM).
/// - Rotacion in-trigger conserva solo los 20 mas recientes.
/// - DELETE de la fila viva cascade-borra sus snapshots.
/// - RestaurarAsync copia el snapshot a la fila viva y genera otro snapshot
///   del estado reemplazado (la restauracion es reversible).
/// </summary>
public sealed class FormDefinitionSnapshotTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = CreateContext(tenantId: null);
        await ctx.Database.MigrateAsync();
        // Necesitamos un Tenant real para que el filtro global y el FK funcionen.
        _tenantId = Guid.CreateVersion7();
        ctx.Tenants.Add(new Tenant { Id = _tenantId, Name = "Agencia Test" });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Update_DeCampoRelevante_GeneraSnapshot()
    {
        var formId = await CrearFormularioAsync();

        await using (var ctx = CreateContext(_tenantId))
        {
            var f = await ctx.FormDefinitions.FirstAsync(x => x.Id == formId);
            f.Nombre = "HC Modificado";
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            var snaps = await ctx.FormDefinitionSnapshots
                .Where(s => s.FormDefinitionId == formId)
                .ToListAsync();
            Assert.Single(snaps);
            // El snapshot debe contener el ESTADO ANTERIOR, no el nuevo.
            Assert.Equal("HC Original", snaps[0].Nombre);
            Assert.Equal("auto-trigger", snaps[0].Motivo);
        }
    }

    [Fact]
    public async Task Update_SoloDeUpdatedAt_NoGeneraSnapshot()
    {
        var formId = await CrearFormularioAsync();

        // Hacemos un UPDATE puro de updated_at via SQL directo, simulando una
        // operacion que el interceptor o cualquier consumer haga sin cambiar
        // ningun campo "de contenido". El trigger debe filtrar este UPDATE.
        await using (var ctx = CreateContext(_tenantId))
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE form_definitions SET updated_at = NOW() WHERE id = {0}",
                formId);
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            var count = await ctx.FormDefinitionSnapshots
                .CountAsync(s => s.FormDefinitionId == formId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task Update_25Veces_RetieneSolo20()
    {
        var formId = await CrearFormularioAsync();

        for (var i = 1; i <= 25; i++)
        {
            await using var ctx = CreateContext(_tenantId);
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE form_definitions SET prefill_routes_json = {0}::jsonb WHERE id = {1}",
                $"{{\"rev\":{i}}}", formId);
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            var snaps = await ctx.FormDefinitionSnapshots
                .Where(s => s.FormDefinitionId == formId)
                .OrderByDescending(s => s.SnapshotAt)
                .ToListAsync();
            Assert.Equal(20, snaps.Count);
        }
    }

    [Fact]
    public async Task Delete_FormDefinition_BorraSusSnapshots()
    {
        var formId = await CrearFormularioAsync();
        await using (var ctx = CreateContext(_tenantId))
        {
            var f = await ctx.FormDefinitions.FirstAsync(x => x.Id == formId);
            f.Nombre = "trigger1"; await ctx.SaveChangesAsync();
            f.Nombre = "trigger2"; await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            Assert.Equal(2, await ctx.FormDefinitionSnapshots.CountAsync(s => s.FormDefinitionId == formId));
            var f = await ctx.FormDefinitions.FirstAsync(x => x.Id == formId);
            ctx.FormDefinitions.Remove(f);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            Assert.Equal(0, await ctx.FormDefinitionSnapshots.CountAsync(s => s.FormDefinitionId == formId));
        }
    }

    [Fact]
    public async Task RestaurarAsync_RestauraEstado_YGeneraSnapshotDelEstadoReemplazado()
    {
        var formId = await CrearFormularioAsync();

        // Cambio: "HC Original" -> "HC v2" (genera snapshot1 con "HC Original")
        await using (var ctx = CreateContext(_tenantId))
        {
            var f = await ctx.FormDefinitions.FirstAsync(x => x.Id == formId);
            f.Nombre = "HC v2";
            await ctx.SaveChangesAsync();
        }

        Guid snapshot1Id;
        await using (var ctx = CreateContext(_tenantId))
        {
            var s = await ctx.FormDefinitionSnapshots.FirstAsync(x => x.FormDefinitionId == formId);
            snapshot1Id = s.Id;
            Assert.Equal("HC Original", s.Nombre);
        }

        // Restauramos snapshot1: la fila viva debe volver a "HC Original" y debe
        // aparecer snapshot2 conteniendo el estado reemplazado ("HC v2").
        await using (var ctx = CreateContext(_tenantId))
        {
            var svc = new FormDefinitionVersionService(
                ctx, // VisalDbContext implementa IApplicationDbContext directamente
                new FixedTenantContext(_tenantId),
                new NullAuditWriter());

            var ok = await svc.RestaurarAsync(snapshot1Id, Guid.CreateVersion7());
            Assert.True(ok);
        }

        await using (var ctx = CreateContext(_tenantId))
        {
            var f = await ctx.FormDefinitions.FirstAsync(x => x.Id == formId);
            Assert.Equal("HC Original", f.Nombre);

            var snaps = await ctx.FormDefinitionSnapshots
                .Where(s => s.FormDefinitionId == formId)
                .OrderByDescending(s => s.SnapshotAt)
                .ToListAsync();
            Assert.Equal(2, snaps.Count);
            // El snapshot MAS RECIENTE captura el estado que se REEMPLAZO al restaurar.
            Assert.Equal("HC v2", snaps[0].Nombre);
        }
    }

    private async Task<Guid> CrearFormularioAsync()
    {
        await using var ctx = CreateContext(_tenantId);
        var f = new FormDefinition
        {
            Codigo = $"HC-{Guid.CreateVersion7().ToString()[..8]}",
            Nombre = "HC Original",
            Version = "1.0",
            Tipo = "HC",
            SchemaJson = "{\"children\":[]}",
            Activo = true
        };
        ctx.FormDefinitions.Add(f);
        await ctx.SaveChangesAsync();
        return f.Id;
    }

    private VisalDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;
        return new VisalDbContext(options, tenantContext);
    }

    private sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
        public Guid? SucursalId => null;
    }

    private sealed class NullAuditWriter : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            Visal.Domain.Enums.AuditActorType actorType = Visal.Domain.Enums.AuditActorType.Human)
        { /* test: no-op */ }
    }
}

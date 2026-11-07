using System.Text.Json;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Visal.Integration.Tests;

/// <summary>
/// Ola 4b (estabilizacion P0/P1) contra Postgres REAL (Testcontainers). Verifica lo que la UI
/// no pudo ejercitar en el smoke de dev (los cierres de HC estan detras de un confirm() nativo
/// que ambos navegadores de Claude auto-descartan):
///
///  1. El overload <see cref="IAuditWriter.Write(IApplicationDbContext, System.Guid, string,
///     string, System.Guid?, object?, object?, System.Guid?, string, Visal.Domain.Enums.AuditActorType)"/>
///     escribe la auditoria en el CONTEXTO CORTO que recibe por parametro (el que entrega la
///     factory por-operacion), NO en el contexto propio del AuditWriter; y el unico SaveChanges
///     de la operacion la persiste en Postgres real.
///  2. Auditoria + cambio de dominio comparten UNA sola transaccion implicita: si el SaveChanges
///     falla, la auditoria tampoco queda (atomicidad todo-o-nada = la garantia de Ola 4a que
///     impide HC/auditorias huerfanas).
///
/// La factory de prueba es espejo 1:1 de ScopedApplicationDbContextFactory (internal, no visible
/// al proyecto de tests): una instancia nueva de VisalDbContext por Create, con el ITenantContext
/// del scope y el mismo interceptor de auditoria.
/// </summary>
public sealed class AuditShortContextIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private Guid _tenantId;
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using (var ctx = NewContext(tenantId: null))
        {
            await ctx.Database.MigrateAsync();
        }
        _tenantId = Guid.CreateVersion7();
        await using (var ctx = NewContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = _tenantId, Name = "Agencia Test" });
            await ctx.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Overload_con_db_escribe_la_auditoria_en_el_contexto_corto_recibido()
    {
        var factory = NewFactory(_tenantId);

        // El AuditWriter tiene su PROPIO contexto (_db). Si el overload usara _db en vez del
        // contexto que recibe por parametro, la fila caeria en 'propio' (que nunca se guarda)
        // y la verificacion no encontraria nada.
        await using var propio = factory.Create();
        IAuditWriter audit = new AuditWriter(propio);

        var entityId = Guid.CreateVersion7();

        // La operacion abre su contexto corto y persiste la auditoria con SU SaveChanges.
        await using (var db = factory.Create())
        {
            audit.Write(db, Actor, "smoke.audit-short-ctx", "Smoke", entityId,
                previousValue: null, newValue: new { ok = true },
                tenantId: _tenantId, reason: "integracion Ola 4b");
            await db.SaveChangesAsync();
        }

        // 'propio' NUNCA se guardo. Un contexto corto NUEVO ve la fila -> el overload uso el db recibido.
        await using var verify = NewContext(_tenantId);
        var row = await verify.SuperAdminAuditLogs
            .FirstOrDefaultAsync(a => a.EntityId == entityId && a.ActionName == "smoke.audit-short-ctx");

        Assert.NotNull(row);
        Assert.Equal(_tenantId, row!.TenantId);
        Assert.Equal(Actor, row.ActorUserId);
        // NewValue es jsonb en Postgres (se re-serializa con espacios); comparar por valor, no por texto.
        using var parsed = JsonDocument.Parse(row.NewValue!);
        Assert.True(parsed.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Auditoria_y_cambio_de_dominio_son_atomicos_en_un_solo_SaveChanges()
    {
        var factory = NewFactory(_tenantId);
        await using var propio = factory.Create();
        IAuditWriter audit = new AuditWriter(propio);

        var entityId = Guid.CreateVersion7();

        // Un solo contexto corto: auditoria + un insert que VIOLA la PK (Tenant duplicado).
        // El SaveChanges debe fallar entero; EF ejecuta el batch en una transaccion implicita,
        // asi que ni la auditoria ni el insert quedan.
        await using (var db = factory.Create())
        {
            audit.Write(db, Actor, "smoke.rollback", "Smoke", entityId,
                previousValue: null, newValue: new { ok = true }, tenantId: _tenantId);
            db.Tenants.Add(new Tenant { Id = _tenantId, Name = "Duplicado" }); // PK ya existente
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var verify = NewContext(_tenantId);
        var row = await verify.SuperAdminAuditLogs
            .FirstOrDefaultAsync(a => a.ActionName == "smoke.rollback");

        Assert.Null(row); // rollback atomico: la auditoria NO se colo pese a haberse encolado
    }

    // --- infraestructura: espejo 1:1 de ScopedApplicationDbContextFactory ---

    private VisalDbContext NewContext(Guid? tenantId)
    {
        var tenant = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenant, TimeProvider.System))
            .Options;
        return new VisalDbContext(options, tenant);
    }

    private TestFactory NewFactory(Guid? tenantId) => new(this, tenantId);

    private sealed class TestFactory(AuditShortContextIntegrationTests owner, Guid? tenantId)
        : IApplicationDbContextFactory
    {
        public IApplicationDbContext Create() => owner.NewContext(tenantId);
    }

    private sealed class FixedTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId => null;
        public Guid? SucursalId => null;
    }
}

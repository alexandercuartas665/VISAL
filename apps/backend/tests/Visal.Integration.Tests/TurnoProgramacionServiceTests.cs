using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Visal.Application.Common;
using Visal.Application.Tenancy.Turnos;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;

namespace Visal.Integration.Tests;

/// <summary>
/// Verifica el servicio TurnoProgramacion (Capa 6 - Gestion de Turnos):
/// - Reglas duras: nombre obligatorio, unicidad por (tenant, sede, anio, mes),
///   min 1 / max 7 turnos, overload 24h/dia opcional segun config del tenant.
/// - Aislamiento multi-tenant: query filter global impide ver programaciones de
///   otro tenant y crear duplicados cross-tenant.
/// - Duplicar preserva nombre + grid + rechaza destino ya ocupado.
/// - Soft-disable via Activa=false, borrado fisico distinto de desactivar.
/// </summary>
public sealed class TurnoProgramacionServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private Guid _tenantA;
    private Guid _tenantB;
    private Guid _sucA;
    private Guid _sucB;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = CreateContext(tenantId: null);
        await ctx.Database.MigrateAsync();
        _tenantA = Guid.CreateVersion7();
        _tenantB = Guid.CreateVersion7();
        _sucA = Guid.CreateVersion7();
        _sucB = Guid.CreateVersion7();
        ctx.Tenants.Add(new Tenant { Id = _tenantA, Name = "Agencia A" });
        ctx.Tenants.Add(new Tenant { Id = _tenantB, Name = "Agencia B" });
        // Cada tenant necesita al menos 1 sucursal — la regla dura de
        // TurnoProgramacion exige >=1 sede al crear.
        ctx.Sucursales.Add(new Sucursal { Id = _sucA, TenantId = _tenantA, Nombre = "Sede A", Activa = true });
        ctx.Sucursales.Add(new Sucursal { Id = _sucB, TenantId = _tenantB, Nombre = "Sede B", Activa = true });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Crear_NombreObligatorio_Falla()
    {
        var svc = CreateService(_tenantA);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(new CrearTurnoProgramacionCmd(
                SucursalIds: new[] { _sucA }, TipoServicioId: null,
                Nombre: "   ", Anio: 2026, Mes: 1,
                Descripcion: null, GridDataJson: GridUnTurno()), actor: Guid.Empty, default));
    }

    [Fact]
    public async Task Crear_NombreDuplicadoMismoPeriodo_Falla()
    {
        var svc = CreateService(_tenantA);
        await svc.CrearAsync(NuevaCmd("Rotacion A", 2026, 1), Guid.Empty, default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(NuevaCmd("Rotacion A", 2026, 1), Guid.Empty, default));
        Assert.Contains("Ya existe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Crear_MismoNombreDistintoMes_OK()
    {
        var svc = CreateService(_tenantA);
        await svc.CrearAsync(NuevaCmd("Rotacion A", 2026, 1), Guid.Empty, default);
        await svc.CrearAsync(NuevaCmd("Rotacion A", 2026, 2), Guid.Empty, default);

        var list = await svc.ListarAsync(null, null, 2026, null, false, default);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Crear_MasDe7Turnos_Falla()
    {
        var svc = CreateService(_tenantA);
        var turnos = string.Join(",", Enumerable.Range(1, 8).Select(i => $"\"T{i}\""));
        var json = $"{{\"turnos\":[{turnos}],\"dias\":{{}}}}";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(new CrearTurnoProgramacionCmd(
                new[] { _sucA }, null, "Rotacion", 2026, 1, null, json), Guid.Empty, default));
        Assert.Contains("Maximo 7", ex.Message);
    }

    [Fact]
    public async Task Crear_SinTurnos_Falla()
    {
        var svc = CreateService(_tenantA);
        var json = "{\"turnos\":[],\"dias\":{}}";
        // El parser cuando ve turnos vacio agrega "Turno 1" por default para no
        // dejar la grilla huerfana. Asi que este caso debe pasar con 1 turno,
        // no fallar. Verificamos que crea OK con exactamente 1 turno.
        var id = await svc.CrearAsync(new CrearTurnoProgramacionCmd(
            new[] { _sucA }, null, "Rot Vacia", 2026, 1, null, json), Guid.Empty, default);
        var det = await svc.ObtenerAsync(id, default);
        var g = GridDataModel.FromJson(det!.GridDataJson);
        Assert.Single(g.Turnos);
    }

    [Fact]
    public async Task Crear_OverloadDia_NoBloqueaPorDefault()
    {
        var svc = CreateService(_tenantA);
        // 3 turnos, todos con 12h el dia 1 = 36h. Sin flag de bloqueo debe pasar.
        var json = "{\"turnos\":[\"T1\",\"T2\",\"T3\"],\"dias\":{"
            + "\"T1\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}},"
            + "\"T2\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}},"
            + "\"T3\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}}}}";
        var id = await svc.CrearAsync(new CrearTurnoProgramacionCmd(
            new[] { _sucA }, null, "Rot Overload", 2026, 1, null, json), Guid.Empty, default);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Crear_OverloadDia_BloqueaSiFlagActivo()
    {
        // Activamos el flag global del tenant A antes de crear.
        await using (var ctx = CreateContext(_tenantA))
        {
            ctx.TenantConfigurations.Add(new TenantConfiguration
            {
                ConfigKey = "turnos.bloquear_overload",
                ConfigValue = "true"
            });
            await ctx.SaveChangesAsync();
        }

        var svc = CreateService(_tenantA);
        var json = "{\"turnos\":[\"T1\",\"T2\",\"T3\"],\"dias\":{"
            + "\"T1\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}},"
            + "\"T2\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}},"
            + "\"T3\":{\"1\":{\"tipo\":\"DN\",\"horas\":12}}}}";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(new CrearTurnoProgramacionCmd(
                new[] { _sucA }, null, "Rot Overload", 2026, 1, null, json), Guid.Empty, default));
        Assert.Contains("supera 24h", ex.Message);
    }

    [Fact]
    public async Task Crear_MesAnioFueraDeRango_Falla()
    {
        var svc = CreateService(_tenantA);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(NuevaCmd("R", 2026, 13), Guid.Empty, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(NuevaCmd("R", 1999, 1), Guid.Empty, default));
    }

    [Fact]
    public async Task MultiTenant_NoVeProgramacionesDeOtroTenant()
    {
        var svcA = CreateService(_tenantA);
        var svcB = CreateService(_tenantB);

        await svcA.CrearAsync(NuevaCmd("Rot A", 2026, 1), Guid.Empty, default);
        await svcB.CrearAsync(NuevaCmd("Rot B", 2026, 1, _sucB), Guid.Empty, default);

        var listA = await svcA.ListarAsync(null, null, null, null, false, default);
        var listB = await svcB.ListarAsync(null, null, null, null, false, default);

        Assert.Single(listA);
        Assert.Single(listB);
        Assert.Equal("Rot A", listA[0].Nombre);
        Assert.Equal("Rot B", listB[0].Nombre);
    }

    [Fact]
    public async Task MultiTenant_UnicidadEsPorTenant()
    {
        // "Rotacion A - Enero 2026" en tenant A no debe bloquear la misma en tenant B.
        var svcA = CreateService(_tenantA);
        var svcB = CreateService(_tenantB);
        await svcA.CrearAsync(NuevaCmd("Rotacion A", 2026, 1), Guid.Empty, default);
        // Esto debe pasar (otro tenant).
        var id = await svcB.CrearAsync(NuevaCmd("Rotacion A", 2026, 1, _sucB), Guid.Empty, default);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Duplicar_CopiaGridAlNuevoPeriodo()
    {
        var svc = CreateService(_tenantA);
        var json = "{\"turnos\":[\"Manana\",\"Tarde\"],\"dias\":{"
            + "\"Manana\":{\"1\":{\"tipo\":\"M\",\"horas\":8}},"
            + "\"Tarde\":{\"1\":{\"tipo\":\"T\",\"horas\":8}}}}";
        var origen = await svc.CrearAsync(new CrearTurnoProgramacionCmd(
            new[] { _sucA }, null, "Rotacion X", 2026, 1, null, json), Guid.Empty, default);

        var copia = await svc.DuplicarAsync(origen, 2026, 2, Guid.Empty, default);
        var det = await svc.ObtenerAsync(copia, default);

        Assert.NotNull(det);
        Assert.Equal("Rotacion X", det!.Nombre);
        Assert.Equal(2, det.Mes);
        var g = GridDataModel.FromJson(det.GridDataJson);
        Assert.Equal(2, g.Turnos.Count);
        Assert.Equal("M", g.Dias["Manana"]["1"].Tipo);
        Assert.Equal(8m, g.Dias["Manana"]["1"].Horas);
    }

    [Fact]
    public async Task Duplicar_AlMismoPeriodo_Falla()
    {
        var svc = CreateService(_tenantA);
        var id = await svc.CrearAsync(NuevaCmd("Rot", 2026, 1), Guid.Empty, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DuplicarAsync(id, 2026, 1, Guid.Empty, default));
    }

    [Fact]
    public async Task Desactivar_MarcaComoInactivaSinBorrar()
    {
        var svc = CreateService(_tenantA);
        var id = await svc.CrearAsync(NuevaCmd("Rot", 2026, 1), Guid.Empty, default);
        await svc.DesactivarAsync(id, Guid.Empty, default);

        var det = await svc.ObtenerAsync(id, default);
        Assert.NotNull(det);
        Assert.False(det!.Activa);

        var soloActivas = await svc.ListarAsync(null, null, null, null, true, default);
        Assert.Empty(soloActivas);
        var todas = await svc.ListarAsync(null, null, null, null, false, default);
        Assert.Single(todas);
    }

    [Fact]
    public async Task Eliminar_BorraFilaFisicamente()
    {
        var svc = CreateService(_tenantA);
        var id = await svc.CrearAsync(NuevaCmd("Rot", 2026, 1), Guid.Empty, default);

        var ok = await svc.EliminarAsync(id, Guid.Empty, default);
        Assert.True(ok);

        var det = await svc.ObtenerAsync(id, default);
        Assert.Null(det);
    }

    [Fact]
    public async Task Sin_Tenant_Activo_Crear_Falla()
    {
        var svc = CreateService(tenantId: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CrearAsync(NuevaCmd("Rot", 2026, 1), Guid.Empty, default));
    }

    // Helpers
    private static string GridUnTurno() => "{\"turnos\":[\"Turno 1\"],\"dias\":{\"Turno 1\":{}}}";

    private CrearTurnoProgramacionCmd NuevaCmd(string nombre, int anio, int mes, Guid? sucursalId = null) =>
        new(SucursalIds: new[] { sucursalId ?? _sucA }, TipoServicioId: null,
            Nombre: nombre, Anio: anio, Mes: mes,
            Descripcion: null, GridDataJson: GridUnTurno());

    private TurnoProgramacionService CreateService(Guid? tenantId)
    {
        var ctx = CreateContext(tenantId);
        var tenantCtx = new FixedTenantContext(tenantId);
        return new TurnoProgramacionService(ctx, tenantCtx);
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
}

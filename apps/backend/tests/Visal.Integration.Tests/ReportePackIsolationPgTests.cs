using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Visal.Application.Common;
using Visal.Application.Tenancy.Reportes;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;

namespace Visal.Integration.Tests;

/// <summary>
/// Capa 13 Ola 1 — aislamiento por tenant del ejecutor multi-hoja contra Postgres real
/// (Testcontainers). El motor corre SQL CRUDO en una conexion dedicada, asi que el global
/// query filter de EF NO aplica: el aislamiento depende enteramente del filtro
/// <c>t0.tenant_id = @_tenantId</c> que inyecta el generador. Aqui lo verificamos.
/// </summary>
public sealed class ReportePackIsolationPgTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private readonly Guid _tenantA = Guid.Parse("aaaaaaa1-0000-0000-0000-000000000001");
    private readonly Guid _tenantB = Guid.Parse("bbbbbbb2-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using (var ctx = CreateContext(null))
        {
            await ctx.Database.MigrateAsync();
        }
        await using (var seed = CreateContext(null))
        {
            seed.Tenants.Add(new Tenant { Id = _tenantA, Name = "Agencia A" });
            seed.Tenants.Add(new Tenant { Id = _tenantB, Name = "Agencia B" });
            await seed.SaveChangesAsync();
        }

        // Tenant A — dos pacientes con distintivo "ALPHA"; cie10 I10 x2, J44 x1.
        await using (var a = CreateContext(_tenantA))
        {
            a.Pacientes.Add(new Paciente { NombreCompleto = "PACIENTE ALPHA 1", NumeroDocumento = "9001A", Estado = "ACTIVO", Cie10Codigo = "I10" });
            a.Pacientes.Add(new Paciente { NombreCompleto = "PACIENTE ALPHA 2", NumeroDocumento = "9002A", Estado = "ACTIVO", Cie10Codigo = "I10" });
            a.Pacientes.Add(new Paciente { NombreCompleto = "PACIENTE ALPHA 3", NumeroDocumento = "9003A", Estado = "ACTIVO", Cie10Codigo = "J44" });
            await a.SaveChangesAsync();

            var pacA = await a.Pacientes.FirstAsync();
            a.PacienteIngresos.Add(new PacienteIngreso
            {
                PacienteId = pacA.Id, IpsComenta = "IPS ALPHA", ResponsableIngreso = "RESP ALPHA",
                Ingreso = true, SlaMinutos = 30, FechaComentan = DateTimeOffset.UtcNow,
            });
            await a.SaveChangesAsync();
        }

        // Tenant B — un paciente con distintivo "BETA".
        await using (var b = CreateContext(_tenantB))
        {
            b.Pacientes.Add(new Paciente { NombreCompleto = "PACIENTE BETA 1", NumeroDocumento = "8001B", Estado = "ACTIVO", Cie10Codigo = "E11" });
            await b.SaveChangesAsync();

            var pacB = await b.Pacientes.FirstAsync();
            b.PacienteIngresos.Add(new PacienteIngreso
            {
                PacienteId = pacB.Id, IpsComenta = "IPS BETA", ResponsableIngreso = "RESP BETA",
                Ingreso = true, SlaMinutos = 45, FechaComentan = DateTimeOffset.UtcNow,
            });
            await b.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Detalle_el_tenant_B_no_ve_datos_del_tenant_A()
    {
        var res = await EjecutarComo(_tenantB, PackPacientes());
        var texto = TodoElTexto(res.Hojas[0]);

        Assert.DoesNotContain("ALPHA", texto);
        Assert.DoesNotContain("9001A", texto);
        Assert.Contains("BETA", texto);
        Assert.Equal(1, res.Hojas[0].TotalFilas);
    }

    [Fact]
    public async Task Detalle_el_tenant_A_ve_solo_lo_suyo()
    {
        var res = await EjecutarComo(_tenantA, PackPacientes());
        var texto = TodoElTexto(res.Hojas[0]);

        Assert.Contains("ALPHA", texto);
        Assert.DoesNotContain("BETA", texto);
        Assert.Equal(3, res.Hojas[0].TotalFilas);
    }

    [Fact]
    public async Task Agregado_indice_morbilidad_cuenta_solo_el_tenant_actual()
    {
        var pack = new ReportePackDef("Censo", [
            ReporteHojaDef.Agregado("Indice Morbilidad", "paciente",
                groupBy: [new ReporteColumnaDef("cie10", "CIE10")],
                agregados: [new ReporteAgregadoDef(ReporteAgregadoFunc.Count, null, "Cant Px's")],
                orden: [new ReporteOrdenDef("Cant Px's", Desc: true)])
        ]);

        var resA = await EjecutarComo(_tenantA, pack);
        var hoja = resA.Hojas[0];
        // A tiene I10 x2 y J44 x1 => 2 filas; el conteo mayor es 2.
        Assert.Equal(2, hoja.TotalFilas);
        var primeraFila = hoja.Filas[0];
        Assert.Equal("I10", primeraFila[0]);
        Assert.Equal(2L, Convert.ToInt64(primeraFila[1]));

        // B no comparte ningun CIE10 de A.
        var resB = await EjecutarComo(_tenantB, pack);
        var textoB = TodoElTexto(resB.Hojas[0]);
        Assert.DoesNotContain("I10", textoB);
        Assert.DoesNotContain("J44", textoB);
    }

    [Fact]
    public async Task Ingreso_bitacora_aislada_por_tenant_incluso_en_el_cruce_a_paciente()
    {
        var pack = new ReportePackDef("Censo", [
            ReporteHojaDef.Detalle("Ingreso de Pacientes", "ingreso_paciente",
            [
                new ReporteColumnaDef("paciente.nombre_completo", "Paciente"),
                new ReporteColumnaDef("ips_comenta", "IPS"),
                new ReporteColumnaDef("sla_minutos", "SLA"),
            ])
        ]);

        var resB = await EjecutarComo(_tenantB, pack);
        var textoB = TodoElTexto(resB.Hojas[0]);
        Assert.DoesNotContain("ALPHA", textoB);
        Assert.Contains("BETA", textoB);
        Assert.Equal(1, resB.Hojas[0].TotalFilas);

        var resA = await EjecutarComo(_tenantA, pack);
        var textoA = TodoElTexto(resA.Hojas[0]);
        Assert.Contains("ALPHA", textoA);
        Assert.DoesNotContain("BETA", textoA);
        Assert.Equal(1, resA.Hojas[0].TotalFilas);
    }

    private static ReportePackDef PackPacientes() => new("Censo", [
        ReporteHojaDef.Detalle("Base de Pacientes", "paciente",
        [
            new ReporteColumnaDef("numero_documento", "ID"),
            new ReporteColumnaDef("nombre_completo", "Nombre"),
            new ReporteColumnaDef("estado", "Estado"),
        ])
    ]);

    private async Task<ReportePackResultado> EjecutarComo(Guid tenantId, ReportePackDef pack)
    {
        var ctx = CreateContext(tenantId);
        var dinamicos = new ReporteCamposDinamicos(ctx);
        var gen = new ReporteSqlGenerator(ctx, new ReportSemanticModel(), dinamicos);
        var svc = new ReportePackService(ctx, new FixedTenantContext(tenantId), gen, dinamicos);
        try
        {
            return await svc.EjecutarAsync(pack);
        }
        finally
        {
            await ctx.DisposeAsync();
        }
    }

    private static string TodoElTexto(ReporteHojaResultado hoja) =>
        string.Join("|", hoja.Filas.SelectMany(r => r.Select(c => c?.ToString() ?? "")));

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

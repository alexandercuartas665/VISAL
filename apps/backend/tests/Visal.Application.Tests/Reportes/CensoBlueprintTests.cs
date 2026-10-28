using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Reportes;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Valida el plano del CENSO (Ola 4): que sus cuatro hojas referencian solo campos/cruces
/// que existen en el modelo semantico y producen SQL valido (con filtro de tenant). Es la
/// red de seguridad contra typos en las keys del blueprint. No toca la BD.
/// </summary>
public sealed class CensoBlueprintTests
{
    private static ReporteSqlGenerator Generador()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql("Host=localhost;Database=visal_test;Username=x;Password=y")
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new VisalDbContext(opts, new FakeTenant { TenantId = Guid.NewGuid() });
        return new ReporteSqlGenerator(db, new ReportSemanticModel(), new FakeReporteCamposDinamicos());
    }

    [Fact]
    public void Las_cuatro_hojas_del_censo_generan_sql_valido_con_filtro_de_tenant()
    {
        var req = CensoBlueprint.Crear();
        Assert.Equal("Censo 2026", req.Nombre);
        Assert.Equal(5, req.Hojas.Count);

        var gen = Generador();
        foreach (var hoja in req.Hojas)
        {
            var q = gen.Generar(hoja); // lanza si alguna key no existe en el catalogo
            Assert.Contains("@_tenantId", q.Sql);
            Assert.NotEmpty(q.Columnas);
        }
    }

    [Fact]
    public void El_consolidado_lleva_pivote_por_mes()
    {
        var consolidado = CensoBlueprint.Crear().Hojas.Single(h => h.Nombre == "Consolidado Encuesta");
        Assert.NotNull(consolidado.Pivote);
        Assert.Equal("pregunta4", consolidado.Pivote!.FilaCampoRef);
        Assert.Equal("mes", consolidado.Pivote.ColumnaCampoRef);
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }
}

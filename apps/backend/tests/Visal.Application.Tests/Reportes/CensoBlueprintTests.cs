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
    public void El_censo_declara_parametros_de_sede_y_rango_de_fechas()
    {
        var req = CensoBlueprint.Crear();
        Assert.NotNull(req.Parametros);
        Assert.Contains(req.Parametros!, p => p.Clave == "sede" && p.Fuente == ReporteParametroFuente.Sedes);
        Assert.Contains(req.Parametros!, p => p.Clave == "rango_fecha" && p.Tipo == ReporteParametroTipo.RangoFecha);
    }

    [Fact]
    public void Los_filtros_parametrizados_se_omiten_sin_valor_y_aplican_al_resolver()
    {
        var req = CensoBlueprint.Crear();
        var gen = Generador();

        // Sin resolver: los filtros enlazados a parametro se omiten (solo el filtro de tenant).
        var baseSinParams = req.Hojas.Single(h => h.Nombre == "Base de Pacientes");
        Assert.DoesNotContain("BETWEEN", gen.Generar(baseSinParams).Sql);

        // Al resolver con valores: aparece el rango (BETWEEN) y la condicion de sede.
        var valores = new Dictionary<string, IReadOnlyList<string>>
        {
            ["sede"] = [Guid.NewGuid().ToString()],
            ["rango_fecha"] = ["2026-01-01", "2026-01-31"],
        };
        var resuelto = ReporteParametros.Resolver(new ReportePackDef(req.Nombre, req.Hojas), valores);
        var baseConParams = resuelto.Hojas.Single(h => h.Nombre == "Base de Pacientes");
        var sql = gen.Generar(baseConParams).Sql;
        Assert.Contains("BETWEEN", sql);
        Assert.Contains("sede_atencion_id", sql);
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

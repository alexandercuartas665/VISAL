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
        Assert.Equal(6, req.Hojas.Count);

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
    public void La_hoja_de_mortalidad_cuenta_fallecidos_por_mes()
    {
        var hoja = CensoBlueprint.Crear().Hojas.Single(h => h.Nombre == "Indice de Mortalidad");
        Assert.Equal(ReporteHojaModo.Agregado, hoja.Modo);
        Assert.Contains(hoja.GroupBy, g => g.CampoRef == "mes_fallecimiento");
        Assert.Contains(hoja.Agregados, a => a.Func == ReporteAgregadoFunc.Count);
        Assert.Contains(hoja.Filtros, f => f.CampoRef == "estado" && f.Op == ReporteFiltroOp.Eq);
        // Genera SQL valido con el filtro de tenant.
        var sql = Generador().Generar(hoja).Sql;
        Assert.Contains("@_tenantId", sql);
    }

    [Fact]
    public void El_consolidado_es_una_matriz_de_encuesta_fiel()
    {
        var consolidado = CensoBlueprint.Crear().Hojas.Single(h => h.Nombre == "Consolidado Encuesta");
        Assert.Null(consolidado.Pivote);
        Assert.NotNull(consolidado.Matriz);

        // La consulta base trae (mes, pregunta4, pregunta5) en ese orden, acotada a llamadas efectivas.
        Assert.Equal(ReporteHojaModo.Detalle, consolidado.Modo);
        Assert.Equal(["mes", "pregunta4", "pregunta5"], consolidado.Columnas.Select(c => c.CampoRef));
        Assert.Contains(consolidado.Filtros, f => f.CampoRef == "estado" && f.Op == ReporteFiltroOp.Eq);

        var m = consolidado.Matriz!;
        Assert.Equal(0, m.ColMes);
        Assert.Equal(1, m.ColP4);
        Assert.Equal(2, m.ColP5);
        Assert.Equal(5, m.BloqueP4.Cubos.Count);
        Assert.Equal(5, m.BloqueP5.Cubos.Count);
        Assert.Equal("Definitivamente Si", m.BloqueP5.Cubos[0].Etiqueta);
        Assert.Equal("Llamadas Efectivas", m.TituloTotal);
    }

    [Fact]
    public void La_matriz_arma_conteo_y_porcentaje_por_mes()
    {
        var consolidado = CensoBlueprint.Crear().Hojas.Single(h => h.Nombre == "Consolidado Encuesta");
        // Marzo 2026 (202603): 3 llamadas -> P4: dos "5" y un "4"; P5: tres "Definitivamente Si" (5).
        var crudo = new ReporteHojaResultado(
            "Consolidado Encuesta",
            ["Mes", "P4", "P5"],
            new List<IReadOnlyList<object?>>
            {
                new object?[] { 202603, 5, 5 },
                new object?[] { 202603, 5, 5 },
                new object?[] { 202603, 4, 5 },
            });

        var r = ReporteMatrizEncuesta.Aplicar(consolidado, crudo);

        // Cabecera de dos niveles: grupo (esquina + 2 bloques + total) y columnas.
        Assert.NotNull(r.GruposColumna);
        Assert.Equal(4, r.GruposColumna!.Count);
        Assert.Equal(10, r.GruposColumna[1].Span); // bloque P4 = 5 cubos * (conteo+%)
        // Columnas: Mes + 10 (P4) + 10 (P5) + total vacio = 22.
        Assert.Equal(22, r.Columnas.Count);

        var fila = Assert.Single(r.Filas);
        Assert.Equal("Marzo 2026", fila[0]);
        Assert.Equal(2, fila[1]);                 // conteo "5" de P4
        Assert.Equal(66.7, (double)fila[2]!);     // 2/3
        Assert.Equal(1, fila[3]);                 // conteo "4" de P4
        Assert.Equal(3, fila[^1]);                // Llamadas Efectivas
    }

    private sealed class FakeTenant : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }
}

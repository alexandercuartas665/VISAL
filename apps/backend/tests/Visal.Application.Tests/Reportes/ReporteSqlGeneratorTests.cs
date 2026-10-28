using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Reportes;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Pruebas del generador de SQL del constructor de reportes. No tocan la BD: construyen
/// el metamodelo de EF offline (Npgsql + snake_case, sin abrir conexion) y verifican el
/// SQL generado. Lo clave: el filtro por tenant SIEMPRE esta, los valores van
/// parametrizados (nunca concatenados) y las referencias fuera del modelo semantico fallan.
/// </summary>
public sealed class ReporteSqlGeneratorTests
{
    private static ReporteSqlGenerator Crear(IReadOnlyList<ReportableFieldDef>? dinamicos = null)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql("Host=localhost;Database=visal_test;Username=x;Password=y")
            .UseSnakeCaseNamingConvention()
            .Options;
        var db = new VisalDbContext(opts, new FakeTenantContext { TenantId = Guid.NewGuid() });
        return new ReporteSqlGenerator(db, new ReportSemanticModel(), new FakeReporteCamposDinamicos(dinamicos));
    }

    [Fact]
    public void Detalle_inyecta_filtro_de_tenant_y_renombra_columnas()
    {
        var q = Crear().Generar(ReporteHojaDef.Detalle("Base", "paciente",
        [
            new ReporteColumnaDef("numero_documento", "ID"),
            new ReporteColumnaDef("nombre_completo"),
        ]));

        Assert.Contains("t0.\"tenant_id\" = @_tenantId", q.Sql);
        Assert.Contains("\"numero_documento\"", q.Sql);
        Assert.Contains("AS \"ID\"", q.Sql);
        Assert.Contains("LIMIT", q.Sql);
        Assert.Equal(["ID", "Nombre Completo"], q.Columnas);
    }

    [Fact]
    public void Filtro_va_parametrizado_y_no_concatena_el_valor()
    {
        var q = Crear().Generar(ReporteHojaDef.Detalle("Base", "paciente",
            [new ReporteColumnaDef("numero_documento")],
            filtros: [new ReporteFiltroDef("estado", ReporteFiltroOp.Eq, ["ACTIVO"])]));

        Assert.Contains("= @p0", q.Sql);
        Assert.DoesNotContain("ACTIVO", q.Sql); // el valor nunca entra al SQL
        var p = Assert.Single(q.Parametros);
        Assert.Equal("p0", p.Name);
        Assert.Equal("ACTIVO", p.Value);
    }

    [Fact]
    public void Filtro_In_genera_varios_parametros()
    {
        var q = Crear().Generar(ReporteHojaDef.Detalle("Base", "paciente",
            [new ReporteColumnaDef("numero_documento")],
            filtros: [new ReporteFiltroDef("ciudad", ReporteFiltroOp.In, ["Cali", "Popayan"])]));

        Assert.Contains("IN (@p0, @p1)", q.Sql);
        Assert.Equal(2, q.Parametros.Count);
    }

    [Fact]
    public void Agregado_genera_group_by_y_count()
    {
        var q = Crear().Generar(ReporteHojaDef.Agregado("Indice Morbilidad", "paciente",
            groupBy: [new ReporteColumnaDef("cie10", "CIE10"), new ReporteColumnaDef("diagnostico_principal", "Diagnostico principal")],
            agregados: [new ReporteAgregadoDef(ReporteAgregadoFunc.Count, null, "Cant Px's")],
            orden: [new ReporteOrdenDef("Cant Px's", Desc: true)]));

        Assert.Contains("COUNT(*) AS \"Cant Px's\"", q.Sql);
        Assert.Contains("GROUP BY", q.Sql);
        Assert.Contains("t0.\"tenant_id\" = @_tenantId", q.Sql);
        Assert.Contains("ORDER BY \"Cant Px's\" DESC", q.Sql);
        Assert.Equal(["CIE10", "Diagnostico principal", "Cant Px's"], q.Columnas);
    }

    [Fact]
    public void PctTotal_genera_indice_con_funcion_de_ventana_sobre_el_total()
    {
        var q = Crear().Generar(ReporteHojaDef.Agregado("Indice Morbilidad", "paciente",
            groupBy: [new ReporteColumnaDef("cie10", "CIE10")],
            agregados:
            [
                new ReporteAgregadoDef(ReporteAgregadoFunc.Count, null, "Cant"),
                new ReporteAgregadoDef(ReporteAgregadoFunc.PctTotal, null, "% del total"),
            ],
            orden: [new ReporteOrdenDef("% del total", Desc: true)]));

        // El indice es participacion sobre el gran total => suma de conteos como ventana vacia.
        Assert.Contains("SUM(COUNT(*)) OVER ()", q.Sql);
        Assert.Contains("AS \"% del total\"", q.Sql);
        Assert.Contains("GROUP BY", q.Sql);
        Assert.Contains("t0.\"tenant_id\" = @_tenantId", q.Sql);
        // Se puede ordenar por el alias del indice (no por la expresion de ventana).
        Assert.Contains("ORDER BY \"% del total\" DESC", q.Sql);
    }

    [Fact]
    public void Cruce_declarado_genera_left_join_con_filtro_de_tenant_en_el_join()
    {
        var q = Crear().Generar(ReporteHojaDef.Detalle("SIAU", "encuesta_satisfaccion",
        [
            new ReporteColumnaDef("mes"),
            new ReporteColumnaDef("paciente.numero_documento", "ID"),
        ]));

        Assert.Contains("LEFT JOIN", q.Sql);
        Assert.Contains("t0.\"tenant_id\" = @_tenantId", q.Sql);   // base
        Assert.Contains("t1.\"tenant_id\" = @_tenantId", q.Sql);   // tabla cruzada (defensa en profundidad)
    }

    [Fact]
    public void Entidad_desconocida_falla()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Crear().Generar(ReporteHojaDef.Detalle("X", "no_existe", [new ReporteColumnaDef("a")])));
        Assert.Contains("Entidad reportable desconocida", ex.Message);
    }

    [Fact]
    public void Campo_desconocido_falla()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Crear().Generar(ReporteHojaDef.Detalle("X", "paciente", [new ReporteColumnaDef("columna_inventada")])));
    }

    [Fact]
    public void Cruce_desconocido_falla()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Crear().Generar(ReporteHojaDef.Detalle("X", "paciente", [new ReporteColumnaDef("relacion_mala.campo")])));
    }

    [Fact]
    public void Campo_dinamico_se_resuelve_como_ruta_jsonb_y_conserva_filtro_de_tenant()
    {
        var dinamicos = new[]
        {
            new ReportableFieldDef("peso", "Peso", "CamposDinamicosJson", ReportFieldKind.Text, JsonKey: "peso"),
        };
        var q = Crear(dinamicos).Generar(ReporteHojaDef.Detalle("Base", "paciente",
            [new ReporteColumnaDef("peso", "Peso")]));

        // Lee la clave dentro del jsonb (nombre fisico resuelto por el metamodelo de EF).
        Assert.Contains("(t0.\"campos_dinamicos_json\" ->> 'peso')", q.Sql);
        Assert.Contains("AS \"Peso\"", q.Sql);
        // El aislamiento por tenant nunca se pierde.
        Assert.Contains("t0.\"tenant_id\" = @_tenantId", q.Sql);
    }

    [Fact]
    public void Filtro_sobre_campo_dinamico_va_parametrizado_sobre_la_ruta_jsonb()
    {
        var dinamicos = new[]
        {
            new ReportableFieldDef("peso", "Peso", "CamposDinamicosJson", ReportFieldKind.Text, JsonKey: "peso"),
        };
        var q = Crear(dinamicos).Generar(ReporteHojaDef.Detalle("Base", "paciente",
            [new ReporteColumnaDef("numero_documento")],
            filtros: [new ReporteFiltroDef("peso", ReporteFiltroOp.Eq, ["80"])]));

        Assert.Contains("(t0.\"campos_dinamicos_json\" ->> 'peso') = @p0", q.Sql);
        Assert.DoesNotContain("80", q.Sql);              // el valor nunca entra al SQL
        Assert.Equal("80", Assert.Single(q.Parametros).Value);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }
}

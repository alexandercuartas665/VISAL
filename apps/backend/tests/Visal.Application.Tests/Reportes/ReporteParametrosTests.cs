using Visal.Application.Tenancy.Reportes;
using Xunit;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Pruebas del resolutor de parametros de ejecucion (filtros globales). Logica pura: dada una
/// definicion con filtros enlazados a un parametro, verifica que al ejecutar se sustituya el
/// valor, se descarte si viene vacio, y que los filtros con valor fijo queden intactos.
/// </summary>
public sealed class ReporteParametrosTests
{
    private static ReporteHojaDef HojaConFiltroParam() =>
        ReporteHojaDef.Detalle(
            "Base", "paciente",
            columnas: [new ReporteColumnaDef("nombre_completo")],
            filtros:
            [
                new ReporteFiltroDef("estado", ReporteFiltroOp.Eq, ["ACTIVO"]),               // fijo
                new ReporteFiltroDef("fecha_ingreso", ReporteFiltroOp.Between, null, "fecha"), // parametro
                new ReporteFiltroDef("sede_id", ReporteFiltroOp.Eq, null, "sede"),             // parametro
            ]);

    [Fact]
    public void Sustituye_valores_de_parametros_y_conserva_filtros_fijos()
    {
        var pack = new ReportePackDef("r", [HojaConFiltroParam()]);
        var valores = new Dictionary<string, IReadOnlyList<string>>
        {
            ["fecha"] = new[] { "2026-01-01", "2026-01-31" },
            ["sede"] = new[] { "11111111-1111-1111-1111-111111111111" },
        };

        var resuelto = ReporteParametros.Resolver(pack, valores);
        var filtros = resuelto.Hojas[0].Filtros;

        Assert.Equal(3, filtros.Count);
        // El filtro fijo no se toca.
        Assert.Contains(filtros, f => f.CampoRef == "estado" && f.ParamClave is null && f.Valores![0] == "ACTIVO");
        // Los parametrizados quedan con valor concreto y sin ParamClave (ya resueltos).
        var fecha = Assert.Single(filtros, f => f.CampoRef == "fecha_ingreso");
        Assert.Null(fecha.ParamClave);
        Assert.Equal(["2026-01-01", "2026-01-31"], fecha.Valores!);
        var sede = Assert.Single(filtros, f => f.CampoRef == "sede_id");
        Assert.Null(sede.ParamClave);
    }

    [Fact]
    public void Descarta_filtro_cuando_el_parametro_viene_vacio_o_ausente()
    {
        var pack = new ReportePackDef("r", [HojaConFiltroParam()]);
        var valores = new Dictionary<string, IReadOnlyList<string>>
        {
            ["fecha"] = new[] { "", "  " }, // vacio -> se descarta
            // "sede" ausente -> se descarta
        };

        var resuelto = ReporteParametros.Resolver(pack, valores);
        var filtros = resuelto.Hojas[0].Filtros;

        // Solo queda el filtro fijo; los dos parametrizados sin valor se cayeron.
        Assert.Single(filtros);
        Assert.Equal("estado", filtros[0].CampoRef);
    }

    [Fact]
    public void Sin_valores_descarta_todos_los_parametrizados()
    {
        var pack = new ReportePackDef("r", [HojaConFiltroParam()]);

        var resuelto = ReporteParametros.Resolver(pack, null);
        var filtros = resuelto.Hojas[0].Filtros;

        Assert.Single(filtros);
        Assert.Equal("estado", filtros[0].CampoRef);
    }
}

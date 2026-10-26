using Visal.Application.Tenancy.Reportes;
using Xunit;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Pruebas del reshape de pivote (Ola 3b). Logica pura: alimentamos un resultado agregado
/// "largo" (group-by de dos campos + un conteo) y verificamos la matriz resultante. Caso
/// canonico: Consolidado Encuesta (fila = pregunta, columna = mes YYYYMM, celda = conteo).
/// </summary>
public sealed class ReportePivoteTests
{
    private static ReporteHojaDef HojaConsolidado() =>
        ReporteHojaDef.Agregado("Consolidado Encuesta", "encuesta_satisfaccion",
            groupBy: [new ReporteColumnaDef("pregunta4"), new ReporteColumnaDef("mes")],
            agregados: [new ReporteAgregadoDef(ReporteAgregadoFunc.Count, null, "Cantidad")],
            pivote: new ReportePivoteDef("pregunta4", "mes"));

    private static ReporteHojaResultado Largo() => new(
        "Consolidado Encuesta",
        ["P4 Experiencia global", "Mes", "Cantidad"],
        new List<IReadOnlyList<object?>>
        {
            new object?[] { 5, 202602, 10L },
            new object?[] { 4, 202601, 3L },
            new object?[] { 5, 202601, 7L },
            new object?[] { 4, 202602, 2L },
        });

    [Fact]
    public void Pivotea_meses_en_columnas_ordenadas_y_llena_celdas()
    {
        var piv = ReportePivote.Aplicar(HojaConsolidado(), Largo());

        // Columnas: el campo fila primero, luego los meses en orden cronologico (YYYYMM legible).
        Assert.Equal(["P4 Experiencia global", "2026-01", "2026-02"], piv.Columnas);

        // Dos filas (pregunta 4 y 5), ordenadas numericamente.
        Assert.Equal(2, piv.Filas.Count);
        Assert.Equal("4", piv.Filas[0][0]);
        Assert.Equal(3L, piv.Filas[0][1]);   // (4, 2026-01)
        Assert.Equal(2L, piv.Filas[0][2]);   // (4, 2026-02)
        Assert.Equal("5", piv.Filas[1][0]);
        Assert.Equal(7L, piv.Filas[1][1]);   // (5, 2026-01)
        Assert.Equal(10L, piv.Filas[1][2]);  // (5, 2026-02)
    }

    [Fact]
    public void Celda_faltante_queda_nula()
    {
        var largo = new ReporteHojaResultado(
            "X", ["P4", "Mes", "Cantidad"],
            new List<IReadOnlyList<object?>>
            {
                new object?[] { 5, 202601, 7L },
                new object?[] { 4, 202602, 2L },
            });

        var piv = ReportePivote.Aplicar(HojaConsolidado(), largo);

        // Fila 4 no tiene 2026-01; fila 5 no tiene 2026-02 -> celdas nulas.
        Assert.Equal(["P4", "2026-01", "2026-02"], piv.Columnas);
        Assert.Equal("4", piv.Filas[0][0]);
        Assert.Null(piv.Filas[0][1]);        // (4, 2026-01) ausente
        Assert.Equal(2L, piv.Filas[0][2]);
        Assert.Equal("5", piv.Filas[1][0]);
        Assert.Equal(7L, piv.Filas[1][1]);
        Assert.Null(piv.Filas[1][2]);        // (5, 2026-02) ausente
    }

    [Fact]
    public void Exige_exactamente_dos_campos_de_agrupacion()
    {
        var hoja = ReporteHojaDef.Agregado("Mal", "encuesta_satisfaccion",
            groupBy: [new ReporteColumnaDef("mes")],
            agregados: [new ReporteAgregadoDef(ReporteAgregadoFunc.Count, null, "Cantidad")],
            pivote: new ReportePivoteDef("pregunta4", "mes"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ReportePivote.Aplicar(hoja, new ReporteHojaResultado("Mal", ["Mes", "Cantidad"], [])));
        Assert.Contains("dos campos", ex.Message);
    }
}

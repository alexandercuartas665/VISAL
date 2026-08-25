using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica el motor de formulas por fila (FormColumn.Formula, "modo columna
/// calculada"). El caso central: una tabla con dos columnas numericas (peso,
/// talla) y una columna calculada (imc) que resuelve sus identificadores
/// contra las celdas hermanas de la MISMA fila. Tambien cubre los bordes:
/// operando vacio / no numerico -> "" (nunca 0 ni NaN), y reuso de las mismas
/// funciones del top-level (sum, cases).
/// </summary>
public sealed class FormFormulaEvaluatorTests
{
    private static Dictionary<string, string?> Row(params (string Col, string? Val)[] cells)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, val) in cells) { d[col] = val; }
        return d;
    }

    [Fact]
    public void ImcPorFila_dosColumnasNumericas_calculaImc()
    {
        // Peso=70, Talla=1.70 -> 70 / (1.70^2) = 24.22 -> "24.22" (dos decimales).
        var fila = Row(("peso", "70"), ("talla", "1.70"));
        Assert.Equal("24.22", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void ImcPorFila_alCambiarTalla_recalcula()
    {
        // Talla=1.65 -> 70 / (1.65^2) = 25.71 -> "25.71".
        var fila = Row(("peso", "70"), ("talla", "1.65"));
        Assert.Equal("25.71", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void ImcPorFila_tallaEnCm_normalizaAMetros()
    {
        // Heuristica de unidades: talla > 3 se asume cm (170 -> 1.70 m).
        var fila = Row(("peso", "70"), ("talla", "170"));
        Assert.Equal("24.22", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void ImcPorFila_sinTalla_devuelveVacio()
    {
        // Operando faltante -> "" (no 0, no NaN). El viewer no muestra nada.
        var fila = Row(("peso", "80"), ("talla", null));
        Assert.Equal("", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void ImcPorFila_operandoNoNumerico_devuelveVacio()
    {
        var fila = Row(("peso", "abc"), ("talla", "1.70"));
        Assert.Equal("", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void ImcPorFila_identificadoresCaseInsensitive()
    {
        // La formula referencia peso/talla; las celdas hermanas pueden venir con
        // otra capitalizacion en su Name. El dict de fila resuelve sin importar caso.
        var fila = Row(("Peso", "70"), ("Talla", "1.70"));
        Assert.Equal("24.22", FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", fila));
    }

    [Fact]
    public void SumPorFila_reusaFuncionTopLevel()
    {
        // Reuso: las mismas funciones del top-level (sum) aplican por fila.
        var fila = Row(("a", "3"), ("b", "4"), ("c", "5"));
        Assert.Equal("12", FormFormulaEvaluator.EvaluateRow("sum(a, b, c)", fila));
    }

    [Fact]
    public void EvaluateRow_esAliasDeEvaluate()
    {
        // EvaluateRow y Evaluate son el mismo motor: solo cambia la semantica del
        // diccionario (fila vs formulario). Deben dar el mismo resultado.
        var vals = Row(("peso", "70"), ("talla", "1.70"));
        Assert.Equal(
            FormFormulaEvaluator.Evaluate("imc(peso, talla)", vals),
            FormFormulaEvaluator.EvaluateRow("imc(peso, talla)", vals));
    }

    [Fact]
    public void FormulaNoReconocida_devuelveVacio()
    {
        var fila = Row(("peso", "70"));
        Assert.Equal("", FormFormulaEvaluator.EvaluateRow("noexiste(peso)", fila));
    }
}

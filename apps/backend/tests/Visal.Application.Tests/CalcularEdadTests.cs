using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica el algoritmo de calculo de edad de Admision.razor.CalcularEdad.
/// Es un replica local del metodo — mismo cuerpo, para que las pruebas viajen
/// con el codigo. Si cambias el algoritmo del componente, actualiza tambien
/// esta copia.
/// </summary>
public class CalcularEdadTests
{
    private static int? CalcularEdad(DateOnly? fechaNacimiento, DateOnly hoy)
    {
        if (fechaNacimiento is not DateOnly fn) { return null; }
        if (fn > hoy) { return null; }
        var edad = hoy.Year - fn.Year;
        if (fn > hoy.AddYears(-edad)) { edad--; }
        return edad < 0 || edad > 130 ? null : edad;
    }

    [Fact]
    public void CumpleanosYaPaso_DevuelveEdadCompleta()
    {
        var edad = CalcularEdad(new DateOnly(1964, 5, 21), new DateOnly(2026, 7, 2));
        Assert.Equal(62, edad);
    }

    [Fact]
    public void CumpleanosAunNoLlega_RestaUnoAlAnio()
    {
        var edad = CalcularEdad(new DateOnly(1964, 8, 21), new DateOnly(2026, 7, 2));
        Assert.Equal(61, edad);
    }

    [Fact]
    public void CumpleanosHoy_CuentaComoAnioCumplido()
    {
        var edad = CalcularEdad(new DateOnly(2000, 7, 2), new DateOnly(2026, 7, 2));
        Assert.Equal(26, edad);
    }

    [Fact]
    public void NacidoHoy_EsCero()
    {
        var edad = CalcularEdad(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 2));
        Assert.Equal(0, edad);
    }

    [Fact]
    public void FechaFutura_EsNull()
    {
        var edad = CalcularEdad(new DateOnly(2027, 1, 1), new DateOnly(2026, 7, 2));
        Assert.Null(edad);
    }

    [Fact]
    public void UnDiaEnElFuturo_EsNull()
    {
        var edad = CalcularEdad(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 2));
        Assert.Null(edad);
    }

    [Fact]
    public void UnAnioExacto_EsUno()
    {
        var edad = CalcularEdad(new DateOnly(2025, 7, 2), new DateOnly(2026, 7, 2));
        Assert.Equal(1, edad);
    }

    [Fact]
    public void UnAnioMenosUnDia_EsCero()
    {
        var edad = CalcularEdad(new DateOnly(2025, 7, 3), new DateOnly(2026, 7, 2));
        Assert.Equal(0, edad);
    }

    [Fact]
    public void NoventaySeis_EsValido()
    {
        var edad = CalcularEdad(new DateOnly(1930, 1, 1), new DateOnly(2026, 7, 2));
        Assert.Equal(96, edad);
    }

    [Fact]
    public void MayorA130_EsNull()
    {
        var edad = CalcularEdad(new DateOnly(1890, 1, 1), new DateOnly(2026, 7, 2));
        Assert.Null(edad);
    }

    [Fact]
    public void FechaNula_EsNull()
    {
        var edad = CalcularEdad(null, new DateOnly(2026, 7, 2));
        Assert.Null(edad);
    }

    [Fact]
    public void Bisiesto_AntesDelCumple_RestaUno()
    {
        var edad = CalcularEdad(new DateOnly(2000, 2, 29), new DateOnly(2024, 2, 28));
        Assert.Equal(23, edad);
    }

    [Fact]
    public void Bisiesto_DiaDespuesDelNoCumple_CuentaAnio()
    {
        var edad = CalcularEdad(new DateOnly(2000, 2, 29), new DateOnly(2024, 3, 1));
        Assert.Equal(24, edad);
    }
}

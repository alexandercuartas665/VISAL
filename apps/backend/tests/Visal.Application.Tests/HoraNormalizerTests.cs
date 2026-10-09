using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests;

public class HoraNormalizerTests
{
    [Theory]
    // Muestras reales reportadas por auditoria (valores sucios que quedaban crudos).
    [InlineData("10.45", "10:45")]      // punto como separador
    [InlineData("10.00AM ", "10:00")]   // meridiano am + espacio sobrante
    [InlineData("17}:30", "17:30")]     // caracter basura entre numeros
    [InlineData("15.00", "15:00")]
    [InlineData("18.00", "18:00")]
    // Casos canonicos y de digitos sueltos (contrato historico de Hora24).
    [InlineData("19:00", "19:00")]
    [InlineData("1900", "19:00")]
    [InlineData("700", "07:00")]
    [InlineData("7", "07:00")]
    // Meridiano pm (el bug anterior lo convertia mal: 01.00PM -> 01:00).
    [InlineData("01.00PM", "13:00")]
    [InlineData("1.00 p.m.", "13:00")]
    [InlineData("12.00AM", "00:00")]    // medianoche
    [InlineData("12.00PM", "12:00")]    // mediodia
    [InlineData("11:30 pm", "23:30")]
    [InlineData("10:30:45", "10:30")]   // descarta segundos
    public void Normalizar_RecuperaHorasSucias(string entrada, string esperado)
    {
        Assert.Equal(esperado, HoraNormalizer.Normalizar(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalizar_VacioDevuelveCadenaVacia(string? entrada)
    {
        Assert.Equal("", HoraNormalizer.Normalizar(entrada));
    }

    [Theory]
    [InlineData("notiene")]   // texto sin digitos
    [InlineData("abc")]
    [InlineData("24:00")]     // hora fuera de rango
    [InlineData("25")]        // hora fuera de rango
    [InlineData("10:75")]     // minutos fuera de rango
    public void Normalizar_NoConvertibleDevuelveNull(string entrada)
    {
        Assert.Null(HoraNormalizer.Normalizar(entrada));
    }

    [Fact]
    public void EsValida_DistingueVacioRecuperableYBasura()
    {
        Assert.True(HoraNormalizer.EsValida("", out var vacio));
        Assert.Equal("", vacio);

        Assert.True(HoraNormalizer.EsValida("10.45", out var ok));
        Assert.Equal("10:45", ok);

        Assert.False(HoraNormalizer.EsValida("notiene", out var bad));
        Assert.Equal("", bad);
    }
}

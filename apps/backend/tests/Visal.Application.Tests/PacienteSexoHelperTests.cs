using Visal.Application.Common;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests de PacienteSexoHelper.ANombreCompleto: convierte variantes reconocidas
/// de hombre/mujer a la palabra completa (MASCULINO/FEMENINO) y deja intacto
/// cualquier otro valor (INDETERMINADO, basura de columnas corridas, null/vacio).
/// </summary>
public class PacienteSexoHelperTests
{
    [Theory]
    [InlineData("M", "MASCULINO")]
    [InlineData("m", "MASCULINO")]
    [InlineData(" M ", "MASCULINO")]
    [InlineData("MASCULINO", "MASCULINO")]
    [InlineData("masculino", "MASCULINO")]
    [InlineData("HOMBRE", "MASCULINO")]
    [InlineData("F", "FEMENINO")]
    [InlineData("f", "FEMENINO")]
    [InlineData(" F ", "FEMENINO")]
    [InlineData("FEMENINO", "FEMENINO")]
    [InlineData("femenino", "FEMENINO")]
    [InlineData("MUJER", "FEMENINO")]
    public void ANombreCompleto_NormalizaVariantesReconocidas(string entrada, string esperado)
    {
        Assert.Equal(esperado, PacienteSexoHelper.ANombreCompleto(entrada));
    }

    [Theory]
    [InlineData("INDETERMINADO", "INDETERMINADO")]
    [InlineData("PASTO", "PASTO")]
    [InlineData("0", "0")]
    public void ANombreCompleto_DejaIntactoLoNoReconocido(string entrada, string esperado)
    {
        // Solo recorta; no adivina un sexo a partir de basura heredada.
        Assert.Equal(esperado, PacienteSexoHelper.ANombreCompleto(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANombreCompleto_NullOVacioPasaSinCambios(string? entrada)
    {
        Assert.Equal(entrada, PacienteSexoHelper.ANombreCompleto(entrada));
    }
}

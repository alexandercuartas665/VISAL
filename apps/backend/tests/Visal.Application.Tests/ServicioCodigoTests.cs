using Visal.Application.Common;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica el recorte del sufijo de modalidad d/f del codigo de servicio para RIPS
/// y la decodificacion a etiqueta legible (Dentro/Fuera). El sufijo solo se reconoce
/// cuando la ultima letra es d/f Y el caracter previo es un digito.
/// </summary>
public class ServicioCodigoTests
{
    [Theory]
    [InlineData("E891865d", "E891865")]   // sufijo dentro
    [InlineData("E891865f", "E891865")]   // sufijo fuera
    [InlineData("E985110D", "E985110")]   // mayuscula tambien
    [InlineData("E985110F", "E985110")]
    [InlineData("890201", "890201")]      // sin sufijo -> intacto
    [InlineData("PKGD", "PKGD")]          // letra previa no es digito -> intacto
    [InlineData("", "")]
    public void Base_QuitaSoloSufijoValido(string entrada, string esperado)
    {
        Assert.Equal(esperado, ServicioCodigo.Base(entrada));
    }

    [Fact]
    public void Base_Null_DevuelveNull()
    {
        Assert.Null(ServicioCodigo.Base(null));
    }

    [Theory]
    [InlineData("E891865d", "Dentro")]
    [InlineData("E891865f", "Fuera")]
    [InlineData("E985110D", "Dentro")]
    [InlineData("890201", null)]          // sin sufijo
    [InlineData("PKGD", null)]            // letra previa no es digito
    [InlineData(null, null)]
    public void ModalidadLabel_DecodificaSufijo(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, ServicioCodigo.ModalidadLabel(entrada));
    }

    [Theory]
    [InlineData("E891865d", "E891865 (Dentro)")]
    [InlineData("E891865f", "E891865 (Fuera)")]
    [InlineData("890201", "890201")]      // sin sufijo -> codigo a secas
    public void Mostrar_ArmaCodigoMasModalidad(string entrada, string esperado)
    {
        Assert.Equal(esperado, ServicioCodigo.Mostrar(entrada));
    }
}

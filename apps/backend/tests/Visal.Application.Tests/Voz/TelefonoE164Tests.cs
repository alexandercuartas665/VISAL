using Visal.Application.Voz;
using Xunit;

namespace Visal.Application.Tests.Voz;

public class TelefonoE164Tests
{
    [Theory]
    [InlineData("3001234567", "+573001234567")]      // celular CO 10 digitos -> +57
    [InlineData("300 123 4567", "+573001234567")]      // con espacios
    [InlineData("+573001234567", "+573001234567")]     // ya E.164
    [InlineData("573001234567", "+573001234567")]      // con indicativo sin +
    [InlineData("3001234567 - 3119998877", "+573001234567")] // toma el primero
    public void Normalizar_DevuelveE164(string entrada, string esperado)
    {
        Assert.Equal(esperado, TelefonoE164.Normalizar(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]              // muy corto
    [InlineData("abcдef")]           // sin digitos
    public void Normalizar_InvalidoDevuelveNull(string? entrada)
    {
        Assert.Null(TelefonoE164.Normalizar(entrada));
    }
}

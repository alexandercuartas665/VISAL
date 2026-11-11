using PdfSharp.Pdf;
using Visal.Infrastructure.Pdf;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica la deteccion de PDFs protegidos con clave usada en la carga de
/// autorizaciones (modulo Asignacion). Ver <see cref="PdfProtectionInspector"/>.
/// </summary>
public class PdfProtectionInspectorTests
{
    private static byte[] PdfPlano()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static byte[] PdfConClaveUsuario(string clave)
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        // Clave de usuario para ABRIR: PdfSharp lo abre solo si se entrega la clave; sin
        // proveedor (como la fusion real) lanza "A password is required" -> se rechaza.
        doc.SecuritySettings.UserPassword = clave;
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static byte[] PdfSoloPermisos(string ownerPassword)
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        // Clave de propietario (permisos) sin clave de usuario: la clave de usuario es
        // vacia, asi que abre sin pedir nada -> no rompe el pipeline -> se acepta.
        doc.SecuritySettings.OwnerPassword = ownerPassword;
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    [Fact]
    public void PdfPlano_EsOk()
    {
        Assert.Equal(PdfEstado.Ok, PdfProtectionInspector.Inspeccionar(PdfPlano()));
    }

    [Fact]
    public void PdfConClaveDeUsuario_SeRechaza()
    {
        Assert.Equal(PdfEstado.ProtegidoPorPassword, PdfProtectionInspector.Inspeccionar(PdfConClaveUsuario("secreto123")));
    }

    [Fact]
    public void PdfSoloConClaveDePropietario_EsOk()
    {
        // Cifrado de solo-permisos: abre con clave vacia, no molesta al usuario ni rompe el pipeline.
        Assert.Equal(PdfEstado.Ok, PdfProtectionInspector.Inspeccionar(PdfSoloPermisos("owner123")));
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44 })] // "%PD" (muy corto)
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 })]
    public void BytesNoPdf_SeMarcanInvalidos(byte[] basura)
    {
        Assert.Equal(PdfEstado.NoEsPdfValido, PdfProtectionInspector.Inspeccionar(basura));
    }

    [Fact]
    public void TextoPlanoNoEsPdf_SeMarcaInvalido()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("esto no es un pdf, es texto plano cualquiera");
        Assert.Equal(PdfEstado.NoEsPdfValido, PdfProtectionInspector.Inspeccionar(bytes));
    }

    [Fact]
    public void Nulo_SeMarcaInvalido()
    {
        Assert.Equal(PdfEstado.NoEsPdfValido, PdfProtectionInspector.Inspeccionar(null));
    }
}

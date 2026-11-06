using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Visal.Infrastructure.Pdf;
using Xunit;

namespace Visal.Application.Tests.Pdf;

/// <summary>
/// El membrete se estampa en la PRIMERA pagina de cada anexo externo (p. ej. listados de
/// firmas) con QuestPDF (franja PNG) + PdfSharp (overlay). Estos tests prueban que el pipeline
/// corre de verdad en este runtime (no solo que compila) y que un PDF ilegible cae a null sin
/// romper nada.
/// </summary>
public sealed class MembreteEstampadorTests
{
    static MembreteEstampadorTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static byte[] PdfDeUnaPagina(string texto = "Documento de prueba")
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.Content().Text(texto).FontSize(12);
            });
        }).GeneratePdf();
    }

    private static int ContarPaginas(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    [Fact]
    public void Estampa_y_preserva_el_pdf_de_una_pagina()
    {
        var original = PdfDeUnaPagina();
        var estampado = MembreteEstampador.Estampar(original, "HC-021237   |   CUP 890201 - CONSULTA EXTERNA");

        Assert.NotNull(estampado);
        Assert.NotEmpty(estampado!);
        // Sigue siendo un PDF valido de 1 pagina (el membrete no agrega paginas).
        Assert.Equal(1, ContarPaginas(estampado));
        // El overlay cambia el contenido -> los bytes no son identicos al original.
        Assert.NotEqual(original.Length, estampado.Length);
    }

    [Fact]
    public void Conserva_el_numero_de_paginas_en_un_pdf_multipagina()
    {
        var original = Document.Create(doc =>
        {
            for (var i = 0; i < 3; i++)
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(36);
                    page.Content().Text("Hoja").FontSize(12);
                });
            }
        }).GeneratePdf();

        var estampado = MembreteEstampador.Estampar(original, "HC-000123   |   CUP 890105");

        Assert.NotNull(estampado);
        Assert.Equal(3, ContarPaginas(estampado!));
    }

    [Fact]
    public void Texto_muy_largo_no_desborda_ni_lanza()
    {
        var original = PdfDeUnaPagina();
        var largo = "HC-021237   |   CUP 890201 - " + new string('A', 400);

        var estampado = MembreteEstampador.Estampar(original, largo);

        Assert.NotNull(estampado);
        Assert.Equal(1, ContarPaginas(estampado!));
    }

    [Fact]
    public void Pdf_invalido_devuelve_null_sin_lanzar()
    {
        var basura = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02, 0x03 };
        var res = MembreteEstampador.Estampar(basura, "HC-000001");
        Assert.Null(res);
    }
}

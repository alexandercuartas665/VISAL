using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Visal.Infrastructure.Pdf;

/// <summary>
/// Estampa un "membrete" (franja de texto en el borde superior) sobre la PRIMERA pagina de un
/// PDF. Pensado para identificar de que historia clinica y CUP proviene cada documento externo
/// (anexo) que se incluye en un archivo de Cuenta Medica (p. ej. los listados de firmas).
///
/// La franja se rinde como PNG con QuestPDF (Skia, con fuentes embebidas -> seguro en Linux) y
/// se dibuja sobre la pagina con PdfSharp: <c>DrawImage</c> NO requiere las fuentes de PdfSharp
/// (solo <c>DrawString</c> las necesitaria), por eso no hace falta registrar un FontResolver.
/// </summary>
public static class MembreteEstampador
{
    private static bool _licenciaLista;

    /// <summary>Alto de la franja del membrete, en puntos PDF (1/72 in).</summary>
    public const double BandaAltoPt = 20;

    /// <summary>
    /// Devuelve el PDF con el membrete estampado en la primera pagina, o <c>null</c> si el PDF
    /// no se pudo abrir/modificar (cifrado, corrupto, rotacion rara): el llamador debe usar el
    /// PDF original sin membrete en ese caso, para no romper el archivo.
    /// </summary>
    public static byte[]? Estampar(byte[] pdf, string texto)
    {
        AsegurarLicencia();
        try
        {
            using var inMs = new MemoryStream(pdf);
            using var doc = PdfReader.Open(inMs, PdfDocumentOpenMode.Modify);
            if (doc.PageCount == 0) { return null; }
            var page = doc.Pages[0];
            var anchoPt = page.Width.Point;
            if (anchoPt <= 0) { return null; }
            var png = RenderBandaPng(anchoPt, BandaAltoPt, texto);
            // OJO: XImage.FromStream llama MemoryStream.GetBuffer(), que lanza si el stream se
            // construye con new MemoryStream(byte[]) (buffer no publico). Hay que escribir el PNG
            // en un MemoryStream vacio (buffer publicamente visible).
            using var imgMs = new MemoryStream();
            imgMs.Write(png, 0, png.Length);
            imgMs.Position = 0;
            using var ximg = XImage.FromStream(imgMs);
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                gfx.DrawImage(ximg, 0, 0, anchoPt, BandaAltoPt);
            }
            using var outMs = new MemoryStream();
            doc.Save(outMs, false);
            return outMs.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Rinde la franja como PNG (ancho = ancho de pagina en puntos, alto fijo) con
    /// QuestPDF: fondo gris claro, borde inferior y el texto a la izquierda.</summary>
    private static byte[] RenderBandaPng(double anchoPt, double altoPt, string texto)
    {
        // Cap defensivo: a 8pt una pagina A4/Carta cabe ~135 chars en una linea; capando a 120
        // se evita que un nombre de servicio largo envuelva a 2 lineas y desborde la franja.
        if (texto.Length > 120) { texto = texto[..117] + "..."; }
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size((float)anchoPt, (float)altoPt, Unit.Point);
                page.Margin(0);
                page.Content()
                    .Background(Colors.Grey.Lighten3)
                    .BorderBottom(0.75f).BorderColor(Colors.Grey.Medium)
                    .PaddingHorizontal(6)
                    .AlignMiddle()
                    .Text(texto).FontSize(8).SemiBold().FontColor(Colors.Black);
            });
        }).GenerateImages(new ImageGenerationSettings
        {
            ImageFormat = ImageFormat.Png,
            RasterDpi = 200,
        }).First();
    }

    private static void AsegurarLicencia()
    {
        if (_licenciaLista) { return; }
        QuestPDF.Settings.License = LicenseType.Community;
        _licenciaLista = true;
    }
}

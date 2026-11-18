using System.IO.Compression;
using ClosedXML.Excel;
using Visal.Application.Common;

namespace Visal.Application.Tests;

/// <summary>Importar profesionales: una firma JPEG con cabecera APP0 "JFXX" hacia que ClosedXML
/// reventara al abrir el Excel ("Unable to determine the format of the image").</summary>
public class XlsxImagenesSaneadorTests
{
    // JPEG sintetico minimo: SOI + APP0 JFIF + SOF0 (1x1, 1 componente) + EOI. ClosedXML solo lee
    // la cabecera hasta el SOF para conocer formato y tamano.
    private static byte[] JpegJfif() => new byte[]
    {
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xD9,
    };

    /// <summary>Como la firma real que fallo: APP0 "JFXX" + APP4 ANTES del APP0 JFIF.</summary>
    private static byte[] JpegJfxxPrimero()
    {
        var jfxx = new byte[] { 0xFF, 0xE0, 0x00, 0x08, (byte)'J', (byte)'F', (byte)'X', (byte)'X', 0x00, 0x13 };
        var app4 = new byte[] { 0xFF, 0xE4, 0x00, 0x04, 0xAB, 0xCD };
        var j = JpegJfif();
        return j.Take(2).Concat(jfxx).Concat(app4).Concat(j.Skip(2)).ToArray();
    }

    private static byte[] LibroConImagen(byte[] jpegReal)
    {
        // Se arma con un JPEG valido y luego se reemplaza el binario dentro del zip.
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Hoja1");
        ws.Cell(1, 1).Value = "N documento";
        ws.Cell(2, 1).Value = "123";
        ws.AddPicture(new MemoryStream(JpegJfif())).MoveTo(ws.Cell(2, 12));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var zipMs = new MemoryStream();
        zipMs.Write(ms.ToArray());
        using (var z = new ZipArchive(zipMs, ZipArchiveMode.Update, leaveOpen: true))
        {
            var media = z.Entries.Single(e => e.FullName.StartsWith("xl/media/", StringComparison.Ordinal));
            var nombre = media.FullName;
            media.Delete();
            using var s = z.CreateEntry(nombre).Open();
            s.Write(jpegReal);
        }
        return zipMs.ToArray();
    }

    [Fact]
    public void Jpeg_estandar_no_se_toca()
    {
        Assert.Null(XlsxImagenesSaneador.NormalizarJpeg(JpegJfif()));
    }

    [Fact]
    public void Jfxx_antes_de_jfif_mueve_el_jfif_al_frente_sin_perder_bytes()
    {
        var original = JpegJfxxPrimero();
        var nuevo = XlsxImagenesSaneador.NormalizarJpeg(original);

        Assert.NotNull(nuevo);
        Assert.Equal(original.Length, nuevo!.Length); // nada se elimina
        Assert.Equal("JFIF", System.Text.Encoding.ASCII.GetString(nuevo, 6, 4));
        Assert.Equal(original.OrderBy(b => b), nuevo.OrderBy(b => b)); // mismos bytes, otro orden
    }

    [Fact]
    public void Sin_jfif_ni_exif_antepone_un_jfif_estandar()
    {
        var sinApp0 = JpegJfif().Take(2).Concat(JpegJfif().Skip(20)).ToArray(); // SOI + SOF0 + EOI
        var nuevo = XlsxImagenesSaneador.NormalizarJpeg(sinApp0);

        Assert.NotNull(nuevo);
        Assert.Equal("JFIF", System.Text.Encoding.ASCII.GetString(nuevo!, 6, 4));
        Assert.Equal(sinApp0.Skip(2), nuevo.Skip(20));
    }

    [Fact]
    public void Excel_con_firma_jfxx_revienta_en_closedxml_y_abre_tras_sanear()
    {
        var xlsx = LibroConImagen(JpegJfxxPrimero());

        // Reproduce el error reportado.
        var ex = Assert.ThrowsAny<Exception>(() => new XLWorkbook(new MemoryStream(xlsx)));
        Assert.Contains("format of the image", ex.Message);

        var saneado = XlsxImagenesSaneador.Sanear(xlsx, out var arregladas);

        Assert.Single(arregladas);
        using var wb = new XLWorkbook(new MemoryStream(saneado));
        var ws = wb.Worksheets.First();
        Assert.Equal("123", ws.Cell(2, 1).GetString());
        var pic = Assert.Single(ws.Pictures);
        Assert.Equal(12, pic.TopLeftCell.Address.ColumnNumber);
    }

    [Fact]
    public void Excel_sin_nada_que_arreglar_devuelve_los_mismos_bytes()
    {
        var xlsx = LibroConImagen(JpegJfif());
        var saneado = XlsxImagenesSaneador.Sanear(xlsx, out var arregladas);

        Assert.Same(xlsx, saneado);
        Assert.Empty(arregladas);
    }

    [Fact]
    public void Bytes_que_no_son_zip_se_devuelven_tal_cual()
    {
        var basura = new byte[] { 1, 2, 3, 4 };
        Assert.Same(basura, XlsxImagenesSaneador.Sanear(basura));
    }
}

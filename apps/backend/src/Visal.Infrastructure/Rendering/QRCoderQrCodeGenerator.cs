using QRCoder;
using Visal.Application.Common;

namespace Visal.Infrastructure.Rendering;

/// <summary>
/// Implementacion de <see cref="IQrCodeGenerator"/> con QRCoder. Usa
/// <see cref="PngByteQRCode"/> (100% managed, sin System.Drawing) para que
/// funcione igual en Linux/Docker. El PNG resultante es cuadrado y blocky, asi
/// que escala sin perdida cuando el &lt;img&gt; lo muestra al tamano pedido.
/// </summary>
public sealed class QRCoderQrCodeGenerator : IQrCodeGenerator
{
    public string GenerarPngDataUri(string? contenido, int tamanoPx = 200)
    {
        if (string.IsNullOrWhiteSpace(contenido))
        {
            return string.Empty;
        }

        // Nivel de correccion M (~15%) — buen balance densidad/robustez para una
        // URL corta que se imprime y se escanea desde papel.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);

        // pixelsPerModule: apuntamos al tamano pedido asumiendo ~30 modulos de
        // lado (tipico para una URL con ECC M). Minimo 2 para que sea legible.
        var ppm = Math.Max(2, (int)Math.Round(tamanoPx / 30.0));
        var png = new PngByteQRCode(data);
        byte[] bytes = png.GetGraphic(ppm);

        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}

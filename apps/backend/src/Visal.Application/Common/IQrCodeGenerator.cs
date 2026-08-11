namespace Visal.Application.Common;

/// <summary>
/// Genera codigos QR como imagen PNG embebida (data URI). Se usa para pintar el
/// QR de verificacion de una orden de medicamentos en el visor y en la
/// impresion. La implementacion vive en Infrastructure (QRCoder), la interfaz
/// aqui para que la UI dependa solo de la abstraccion.
/// </summary>
public interface IQrCodeGenerator
{
    /// <summary>
    /// Devuelve un data URI PNG ("data:image/png;base64,...") que codifica el
    /// contenido dado. <paramref name="tamanoPx"/> es el tamano objetivo
    /// aproximado en pixeles (el QR es escalable; el tamano exacto de
    /// visualizacion lo fija el &lt;img&gt;). Retorna cadena vacia si el
    /// contenido es nulo o vacio.
    /// </summary>
    string GenerarPngDataUri(string? contenido, int tamanoPx = 200);
}

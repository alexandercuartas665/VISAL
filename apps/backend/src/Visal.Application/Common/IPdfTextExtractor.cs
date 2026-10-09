namespace Visal.Application.Common;

/// <summary>Extrae el texto (capa de texto) de un PDF. Si el PDF es una imagen escaneada
/// sin capa de texto, devuelve cadena vacia o muy corta (el llamador lo detecta).</summary>
public interface IPdfTextExtractor
{
    /// <summary>Devuelve el texto concatenado de todas las paginas del PDF. "" si no hay texto.</summary>
    string ExtractText(byte[] pdfBytes);
}

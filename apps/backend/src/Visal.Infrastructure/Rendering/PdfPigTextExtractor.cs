using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Visal.Application.Common;

namespace Visal.Infrastructure.Rendering;

/// <summary>Extrae la capa de texto de un PDF con PdfPig (C# puro, sin binarios nativos;
/// corre igual en el Docker Linux). Usa ContentOrderTextExtractor para respetar el orden
/// de lectura y el espaciado. Un PDF escaneado (solo imagen) devuelve texto vacio/corto.</summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public string ExtractText(byte[] pdfBytes)
    {
        if (pdfBytes is null || pdfBytes.Length == 0) { return string.Empty; }
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
            {
                sb.AppendLine(ContentOrderTextExtractor.GetText(page));
                sb.AppendLine();
            }
            return sb.ToString().Trim();
        }
        catch
        {
            // PDF corrupto, cifrado o ilegible -> sin texto (el llamador decide el fallback/aviso).
            return string.Empty;
        }
    }
}

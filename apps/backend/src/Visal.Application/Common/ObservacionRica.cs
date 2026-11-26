using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Visal.Application.Common;

/// <summary>Observacion enriquecida ya normalizada: HTML saneado (o null si el texto es plano),
/// texto plano equivalente (para listados, auditoria y busquedas) y cantidad de imagenes.</summary>
public sealed record ObservacionRicaResultado(string? Html, string? Texto, int Imagenes)
{
    public bool Vacia => Texto is null && Imagenes == 0;
}

/// <summary>
/// IMP-71: observaciones con texto enriquecido + imagenes (editor TinyMCE) que deja el
/// autorizador en /autorizaciones y que ve el profesional en la historia clinica. El HTML
/// se sanea (mismas reglas que las lecciones de Capacitaciones: sin scripts/handlers, imagenes
/// solo embebidas en base64) y se limita a ~2 MB. Junto al HTML se guarda el texto plano
/// equivalente en la columna historica (Respuesta / Observacion), asi los listados, exportes y
/// la auditoria siguen trabajando con texto.
/// </summary>
public static partial class ObservacionRica
{
    /// <summary>Tope del HTML saneado: ~2 MB de imagenes (base64 infla 4/3).</summary>
    public const int MaxHtmlChars = 2_800_000;

    public const string MensajeTope =
        "La observacion es muy pesada (maximo 2 MB). Reduce o recorta las imagenes.";

    private static readonly HtmlParser Parser = new();

    /// <summary>Normaliza lo que llega del editor. Sin etiquetas HTML = texto plano (compatibilidad:
    /// Html null, Texto = el texto recortado). Lanza <see cref="InvalidOperationException"/> si
    /// supera el tope.</summary>
    public static ObservacionRicaResultado Normalizar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) { return new ObservacionRicaResultado(null, null, 0); }
        var s = entrada.Trim();
        if (!s.Contains('<'))
        {
            return new ObservacionRicaResultado(null, s, 0);
        }

        var limpio = LeccionHtmlSanitizer.Clean(s);
        if (limpio is null) { return new ObservacionRicaResultado(null, null, 0); }
        if (limpio.Length > MaxHtmlChars) { throw new InvalidOperationException(MensajeTope); }

        var doc = Parser.ParseDocument("<body>" + limpio + "</body>");
        var imagenes = doc.QuerySelectorAll("img").Length;
        var texto = TextoPlano(limpio);
        if (texto is null && imagenes == 0) { return new ObservacionRicaResultado(null, null, 0); }
        return new ObservacionRicaResultado(limpio, texto, imagenes);
    }

    /// <summary>Texto plano para la columna historica: el texto, o un marcador si solo hay imagenes.</summary>
    public static string TextoOMarcador(ObservacionRicaResultado r)
        => r.Texto ?? (r.Imagenes == 1 ? "(imagen adjunta)" : $"({r.Imagenes} imagenes adjuntas)");

    /// <summary>Convierte HTML a texto plano conservando los saltos de parrafo/linea/lista.</summary>
    public static string? TextoPlano(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return null; }
        // Marca los cortes de bloque antes de extraer el texto (TextContent los pierde).
        var marcado = BloqueRegex().Replace(html, "\n$0");
        var doc = Parser.ParseDocument("<body>" + marcado + "</body>");
        var raw = WebUtility.HtmlDecode(doc.Body?.TextContent ?? "");
        var lineas = raw.Replace("\r", "")
            .Split('\n')
            .Select(l => EspaciosRegex().Replace(l, " ").Trim())
            .Where(l => l.Length > 0);
        var texto = string.Join("\n", lineas);
        return texto.Length == 0 ? null : texto;
    }

    [GeneratedRegex(@"<(br|/p|/div|/li|/h[1-6]|/tr|/blockquote)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BloqueRegex();

    [GeneratedRegex(@"[ \t\u00A0]+")]
    private static partial Regex EspaciosRegex();
}

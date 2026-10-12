using Ganss.Xss;

namespace Visal.Application.Common;

/// <summary>
/// Sanitiza el HTML enriquecido de la descripcion de una leccion antes de guardarlo.
/// Lo escribe un admin pero lo leen otros usuarios en el reproductor, asi que hay que
/// quitar cualquier vector de XSS (scripts, handlers on*, iframes, etc.) y dejar solo
/// formato seguro: negrita/subrayado/listas/enlaces/tablas e imagenes embebidas (base64).
/// </summary>
public static class LeccionHtmlSanitizer
{
    private static readonly HtmlSanitizer _sanitizer = Crear();

    private static HtmlSanitizer Crear()
    {
        var s = new HtmlSanitizer();

        // Imagenes embebidas como data URL (base64).
        s.AllowedSchemes.Add("data");

        // Etiquetas de formato y de tabla (ademas de las permitidas por defecto).
        foreach (var t in new[]
        {
            "u", "s", "strike", "sub", "sup", "span", "br", "hr", "blockquote", "pre", "code",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "colgroup", "col",
            "figure", "figcaption",
        })
        {
            s.AllowedTags.Add(t);
        }

        // Atributos que usan las tablas / imagenes de TinyMCE.
        foreach (var a in new[]
        {
            "style", "colspan", "rowspan", "scope", "width", "height", "alt", "title",
            "border", "cellpadding", "cellspacing",
        })
        {
            s.AllowedAttributes.Add(a);
        }

        // Propiedades CSS inline seguras (tablas, alineacion, colores).
        foreach (var c in new[]
        {
            "text-align", "vertical-align", "width", "height", "border", "border-collapse",
            "border-color", "border-width", "border-style", "background-color", "color",
            "padding", "margin", "font-weight", "font-style", "text-decoration",
        })
        {
            s.AllowedCssProperties.Add(c);
        }

        return s;
    }

    /// <summary>Devuelve el HTML saneado, o null si queda vacio.</summary>
    public static string? Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return null; }
        var limpio = _sanitizer.Sanitize(html);
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }
}

using System.Globalization;
using System.Text;

namespace Visal.Application.Voz;

/// <summary>
/// Pregunta 5 de la encuesta SIAU ("Recomendaria a VISAL RT S.A.S a un familiar"):
/// es una lista NPS, no un numeral. Mapea el texto de la opcion a su ordinal
/// (5..1). Es la misma escala que usa el reporte CENSO (Consolidado y hoja de
/// Satisfaccion). Null si el texto no coincide con ninguna opcion conocida.
/// </summary>
public static class NpsRecomendacion
{
    public static readonly (int Valor, string Etiqueta)[] Opciones =
    {
        (5, "Definitivamente Si"),
        (4, "Probablemente Si"),
        (3, "Ni Probable Ni Improbable"),
        (2, "Probablemente No"),
        (1, "Definitivamente NO"),
    };

    /// <summary>Texto de la opcion NPS (o un digito 1-5) -> ordinal 1-5; null si no coincide.</summary>
    public static int? TextoAOrdinal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        var t = SinAcentos(texto.Trim().ToLowerInvariant());

        // Passthrough numerico: la IA o el analisis pueden devolver "5".
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 5)
        {
            return n;
        }

        var tieneSi = ContienePalabra(t, "si");
        var tieneNo = ContienePalabra(t, "no");

        if (t.Contains("definitivamente") && tieneSi) { return 5; }
        if (t.Contains("probablemente") && tieneSi) { return 4; }
        if (t.Contains("improbable") || (t.Contains("ni ") && t.Contains("probable"))) { return 3; }
        if (t.Contains("probablemente") && tieneNo) { return 2; }
        if (t.Contains("definitivamente") && tieneNo) { return 1; }
        return null;
    }

    /// <summary>Ordinal 1-5 -> etiqueta NPS; "-" si null o fuera de rango.</summary>
    public static string Etiqueta(int? valor)
    {
        if (valor is null) { return "-"; }
        foreach (var o in Opciones)
        {
            if (o.Valor == valor.Value) { return o.Etiqueta; }
        }
        return valor.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ContienePalabra(string texto, string palabra)
    {
        // Busca la palabra como token (evita que "nosotros" cuente como "no").
        var tokens = texto.Split(new[] { ' ', ',', '.', ';', ':', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Contains(palabra);
    }

    private static string SinAcentos(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

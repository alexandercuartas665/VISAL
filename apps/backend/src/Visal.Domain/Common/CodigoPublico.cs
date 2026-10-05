using System.Text.RegularExpressions;

namespace Visal.Domain.Common;

/// <summary>
/// Codigos publicos legibles a partir del consecutivo global (secuencia BD).
/// La historia se muestra "HC-000123" y el lote/asignacion "AS-000045". El Id UUID
/// sigue siendo la llave interna; esto es solo para mostrar/buscar/imprimir.
/// </summary>
public static class CodigoPublico
{
    public const string PrefijoHc = "HC-";
    public const string PrefijoAsignacion = "AS-";
    private const int Ancho = 6;

    public static string Hc(long consecutivo) => PrefijoHc + consecutivo.ToString("D" + Ancho);
    public static string Asignacion(long consecutivo) => PrefijoAsignacion + consecutivo.ToString("D" + Ancho);

    // Codigo consecutivo = prefijo de letras OPCIONAL + separador + SOLO digitos.
    // Asi "HC-000123"/"AS-45"/"hc123"/"000123"/"123" -> numero, pero un prefijo HEX
    // como "01A0D59F" o "019F973B" (digitos MEZCLADOS con letras a-f) NO matchea y
    // se trata como codigo hex legacy, no como consecutivo.
    private static readonly Regex RxConsecutivo = new(@"^[A-Za-z]*[-\s]*(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Extrae el numero consecutivo de un texto de busqueda. Acepta "HC-000123",
    /// "AS-45", "hc123", "000123" o "123" -> 123. Devuelve null si el texto NO es un
    /// consecutivo (vacio, o un codigo hex con letras intercaladas), para que el
    /// buscador caiga al matching por prefijo hex legacy.
    /// </summary>
    public static long? ParseNumero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        var m = RxConsecutivo.Match(texto.Trim());
        if (!m.Success) { return null; }
        return long.TryParse(m.Groups[1].Value, out var n) && n > 0 ? n : null;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Normaliza una entrada libre de HORA a "HH:mm" en formato 24h. Es la FUENTE DE
/// VERDAD unica de validacion/normalizacion de hora: la usan el control de captura
/// (<c>Hora24.razor</c>), el render de formularios (<c>FormViewer</c>) y el calculo
/// de la fecha de atencion (<c>FechaAtencionHelper</c>). Tener una sola logica evita
/// que la UI acepte un valor que luego el backend no sabe parsear (bug historico:
/// "10.45", "17}:30", "10.00AM" quedaban crudos y la fecha de atencion caia a 00:00).
///
/// Recupera entradas sucias:
///   "19:00", "1900", "7", "700"   -> por digitos (1-2 = hora; 3-4 = HHmm)
///   "10.45", "15.00"              -> punto como separador
///   "17}:30", "10 30"             -> ignora caracteres basura entre numeros
///   "10.00AM", "01.00 p.m."       -> convierte meridiano a 24h
/// Rechaza (devuelve null) lo no convertible: "notiene", cualquier letra que no sea
/// el meridiano am/pm, y horas/minutos fuera de rango (h&gt;23 o m&gt;59).
/// </summary>
public static class HoraNormalizer
{
    // Meridiano: "am", "pm", "a.m.", "p. m.", etc. Captura a/p para saber el periodo.
    private static readonly Regex Meridiano =
        new(@"([ap])\s*\.?\s*m\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Normaliza una hora libre.
    /// </summary>
    /// <returns>
    /// "HH:mm" si la entrada es recuperable; cadena vacia ("") si la entrada esta
    /// vacia (campo opcional sin diligenciar); <c>null</c> si tiene contenido pero
    /// no es convertible a una hora valida.
    /// </returns>
    public static string? Normalizar(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return ""; }
        var s = raw.Trim();

        // 1) Detectar y retirar el meridiano am/pm antes de leer los digitos.
        bool? pm = null;
        var mer = Meridiano.Match(s);
        if (mer.Success)
        {
            pm = char.ToLowerInvariant(mer.Groups[1].Value[0]) == 'p';
            s = s.Remove(mer.Index, mer.Length);
        }

        // 2) Si tras quitar el meridiano quedan letras, no es una hora.
        if (s.Any(char.IsLetter)) { return null; }

        // 3) Separar hora y minutos. Con ':' o '.' partimos por el primero; sin
        //    separador, usamos la longitud de los digitos (heuristica de reloj).
        int h, m;
        var sep = s.IndexOfAny(new[] { ':', '.' });
        if (sep >= 0)
        {
            var hPart = DigitsOnly(s[..sep]);
            var mPart = DigitsOnly(s[(sep + 1)..]);
            if (hPart.Length == 0) { return null; }
            h = int.Parse(hPart.Length > 2 ? hPart[..2] : hPart, CultureInfo.InvariantCulture);
            m = mPart.Length == 0 ? 0 : int.Parse(mPart[..Math.Min(2, mPart.Length)], CultureInfo.InvariantCulture);
        }
        else
        {
            var digits = DigitsOnly(s);
            if (digits.Length == 0) { return null; }
            if (digits.Length <= 2) { h = int.Parse(digits, CultureInfo.InvariantCulture); m = 0; }
            else
            {
                if (digits.Length > 4) { digits = digits[..4]; }
                m = int.Parse(digits[^2..], CultureInfo.InvariantCulture);
                h = int.Parse(digits[..^2], CultureInfo.InvariantCulture);
            }
        }

        // 4) Meridiano -> 24h. 12am = 00:xx (medianoche); 12pm = 12:xx (mediodia).
        if (pm is true && h >= 1 && h <= 11) { h += 12; }
        else if (pm is false && h == 12) { h = 0; }

        if (h > 23 || m > 59) { return null; }
        return $"{h:D2}:{m:D2}";
    }

    /// <summary>
    /// true si <paramref name="raw"/> esta vacio o es una hora recuperable; false si
    /// tiene contenido no convertible. Deja en <paramref name="hhmm"/> la hora
    /// normalizada ("HH:mm") o "" cuando esta vacio/invalido.
    /// </summary>
    public static bool EsValida(string? raw, out string hhmm)
    {
        var norm = Normalizar(raw);
        hhmm = norm ?? "";
        return norm is not null;
    }

    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());
}

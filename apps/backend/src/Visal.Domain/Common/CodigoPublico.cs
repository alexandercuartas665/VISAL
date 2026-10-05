using System.Text;

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

    /// <summary>
    /// Extrae el numero consecutivo de un texto de busqueda. Acepta "HC-000123",
    /// "AS-45", "hc123", "000123" o "123" -> 123. Devuelve null si no hay digitos
    /// o el numero no es valido. Tolera prefijo/ceros/espacios para que el usuario
    /// pueda escribir el codigo de cualquier forma razonable.
    /// </summary>
    public static long? ParseNumero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return null; }
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (char.IsDigit(c)) { sb.Append(c); }
        }
        if (sb.Length == 0) { return null; }
        return long.TryParse(sb.ToString(), out var n) && n > 0 ? n : null;
    }
}

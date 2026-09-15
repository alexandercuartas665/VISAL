namespace Visal.Application.Common;

/// <summary>
/// Normalizacion de numeros celulares colombianos a formato E.164 sin "+"
/// ("57XXXXXXXXXX", 12 digitos). Centraliza la regla que antes se duplicaba en
/// varios servicios (alertas, firma remota) y se usa al guardar el celular de un
/// profesional para que WhatsApp/Gupshup siempre reciba el indicativo de pais.
/// </summary>
public static class TelefonoHelper
{
    /// <summary>
    /// Devuelve el celular normalizado a "57" + 10 digitos cuando reconoce un movil
    /// colombiano (10 digitos que empiezan en 3, con o sin espacios/simbolos), o un
    /// numero que ya trae "57" + 10 digitos. Si no encaja (vacio, longitud rara,
    /// fijo), devuelve solo los digitos (limpia espacios) sin forzar el 57, para no
    /// corromper numeros que requieren revision manual. Null si viene vacio.
    /// </summary>
    public static string? NormalizarCelularCo(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var d = new string(raw.Where(char.IsDigit).ToArray());
        if (d.Length == 0) { return raw.Trim(); }
        if (d.Length == 10 && d[0] == '3') { return "57" + d; }          // movil CO sin indicativo
        if (d.Length == 12 && d.StartsWith("57")) { return d; }          // ya normalizado
        return d;                                                        // otros: solo digitos (revision manual)
    }

    /// <summary>True si el valor es un celular colombiano valido ya normalizado
    /// ("57" + movil de 10 digitos que empieza en 3). Para validacion de formulario.</summary>
    public static bool EsCelularCoValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) { return false; }
        var d = new string(valor.Where(char.IsDigit).ToArray());
        return d.Length == 12 && d.StartsWith("57") && d[2] == '3';
    }
}

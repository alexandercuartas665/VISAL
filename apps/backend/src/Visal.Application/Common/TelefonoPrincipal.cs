using System.Text;

namespace Visal.Application.Common;

/// <summary>
/// Extrae UN solo telefono de un campo de texto libre que puede traer varios
/// ("3156896006-3113116200", "3118359029 y 3135158931", "314 7183076; 301 4317920",
/// "2710102 Ext 503 WhatsApp: 57 321 2049545"...). Se usa en salidas de facturacion
/// (Snapshots) donde la EPS espera un unico numero.
///
/// Regla:
///   1. Parte el texto en "candidatos": grupos de digitos que se juntan mientras esten
///      separados SOLO por espacios (incluido el espacio duro), guiones o puntos (un numero
///      escrito "304-472-05-39" o "314 7183076" es uno solo). Un candidato se corta cuando el
///      separador trae otra cosa (";", ",", "/", " y ", letras como "Ext", parentesis...) o
///      cuando ya tiene 7+ digitos y sumar el siguiente grupo pasaria de 10 (otro telefono:
///      "3146010867 - 3194741575"). A cada candidato de 12 digitos se le quita el indicativo 57.
///   2. Devuelve el PRIMER candidato valido (10 digitos = celular / fijo con indicativo, o 7 =
///      fijo); si ninguno es valido, el primero de 7+ digitos (p. ej. un numero mal digitado de 9),
///      y si tampoco, el primero que haya.
/// Devuelve solo digitos. Si el texto no trae digitos ("NO TIENE", "SD") se devuelve tal cual
/// (recortado), sin inventar.
/// </summary>
public static class TelefonoPrincipal
{
    public static string? Primero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return texto; }
        var s = texto.Trim();

        var candidatos = Candidatos(s);
        if (candidatos.Count == 0) { return s; }
        return candidatos.FirstOrDefault(c => c.Length is 10 or 7)
            ?? candidatos.FirstOrDefault(c => c.Length >= 7)
            ?? candidatos[0];
    }

    private static List<string> Candidatos(string s)
    {
        var res = new List<string>();
        var num = new StringBuilder();
        var i = 0;

        void Cerrar()
        {
            if (num.Length == 0) { return; }
            var r = num.ToString();
            if (r.Length == 12 && r.StartsWith("57", StringComparison.Ordinal)) { r = r[2..]; }
            res.Add(r);
            num.Clear();
        }

        while (i < s.Length)
        {
            if (!char.IsAsciiDigit(s[i])) { i++; continue; }

            // Grupo de digitos.
            var ini = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; }
            var grupo = s[ini..i];

            // Ya hay un numero completo y este grupo lo pasaria de 10: es otro telefono.
            if (num.Length >= 7 && num.Length + grupo.Length > 10) { Cerrar(); }
            num.Append(grupo);

            // Separador hasta el proximo digito: solo se sigue juntando si es espacio/guion/punto.
            var j = i;
            while (j < s.Length && (char.IsWhiteSpace(s[j]) || s[j] == '-' || s[j] == '.')) { j++; }
            if (j >= s.Length || !char.IsAsciiDigit(s[j])) { Cerrar(); }
            i = j;
        }
        Cerrar();
        return res;
    }
}

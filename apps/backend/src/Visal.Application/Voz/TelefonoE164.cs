using Visal.Application.Common;

namespace Visal.Application.Voz;

/// <summary>Normaliza telefonos al formato E.164 requerido por Retell/Telnyx.</summary>
public static class TelefonoE164
{
    /// <summary>
    /// Toma el primer telefono del campo (puede traer varios), lo deja en digitos,
    /// antepone 57 a los celulares colombianos de 10 digitos y devuelve "+&lt;digitos&gt;".
    /// Null si no es un numero plausible (E.164 admite 8..15 digitos).
    /// </summary>
    public static string? Normalizar(string? telefono)
    {
        var t = PacienteTelefonoHelper.Principal(telefono);
        if (string.IsNullOrWhiteSpace(t)) { return null; }
        var digits = new string(t.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return null; }
        if (digits.Length == 10) { digits = "57" + digits; }
        if (digits.Length < 8 || digits.Length > 15) { return null; }
        return "+" + digits;
    }
}

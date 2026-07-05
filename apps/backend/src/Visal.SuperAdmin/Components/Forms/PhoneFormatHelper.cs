using System.Linq;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>Formatea el telefono completo del paciente en digitos (sin el "+" ni
/// separadores), evitando la duplicacion del codigo pais cuando el campo
/// <c>Telefono</c> ya lo trae al inicio por datos historicos. Es el shape que
/// espera el chat de WhatsApp: pura cadena numerica lista para usar como
/// destinatario del provider.
///
/// El bug que evita: pacientes con <c>CodigoPaisTelefono="+57"</c> y
/// <c>Telefono="573217626882"</c> generaban "57573217626882" al concatenar.
/// </summary>
internal static class PhoneFormatHelper
{
    public static string BuildFullDigits(string? codigoPais, string? telefono)
    {
        var prefix = new string((codigoPais ?? "+57").Where(char.IsDigit).ToArray());
        var digits = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits)) { return prefix; }

        // Si el numero ya arranca con el prefijo, no lo agregamos otra vez.
        // Loop porque en casos raros pudo quedar pegado dos veces.
        while (!string.IsNullOrEmpty(prefix)
               && digits.Length > prefix.Length
               && digits.StartsWith(prefix))
        {
            digits = digits.Substring(prefix.Length);
        }

        return prefix + digits;
    }
}

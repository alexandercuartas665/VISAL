using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Llena automaticamente los campos del encabezado del documento (FormHeader.Campos)
/// usando heuristica sobre el Label: No Historia / Fecha / Hora se derivan del HC
/// activo y NO los puede editar el medico.
///
/// Convencion de keys: cada FormHeaderField se renderiza con valor en
/// _valores["hdr:" + field.Id]. Este helper escribe esos valores y devuelve el
/// conjunto de keys que deben quedar bloqueadas (readonly) en el FormViewer.
/// </summary>
public static class HeaderAutoFillHelper
{
    /// <summary>
    /// Aplica los valores automaticos. Devuelve el set de keys bloqueadas para
    /// que el FormViewer las dibuje readonly.
    /// </summary>
    public static HashSet<string> Aplicar(
        Dictionary<string, string?> valores,
        FormHeader? header,
        Guid hcId,
        DateTimeOffset fechaApertura)
    {
        var bloqueadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (header is null) { return bloqueadas; }

        // Consecutivo legible del HC: ultimos 8 chars del Guid (estable, unico).
        var consecutivo = hcId.ToString("N")[^8..].ToUpperInvariant();
        var local = fechaApertura.ToLocalTime();
        var fecha = local.ToString("dd/MM/yyyy");
        var hora = local.ToString("HH:mm");

        foreach (var f in header.Campos)
        {
            var key = "hdr:" + f.Id;
            var label = (f.Label ?? "").Trim().ToLowerInvariant();
            string? auto = label switch
            {
                _ when ContainsAny(label, "historia", "hc", "consecutivo", "no.") => consecutivo,
                _ when ContainsAny(label, "fecha") && !label.Contains("hora") => fecha,
                _ when label.Contains("hora") => hora,
                _ when label.Contains("ciudad") => null,
                _ => null
            };
            if (auto is not null)
            {
                valores[key] = auto;
                bloqueadas.Add(key);
            }
        }
        return bloqueadas;
    }

    private static bool ContainsAny(string s, params string[] needles)
    {
        foreach (var n in needles) { if (s.Contains(n)) { return true; } }
        return false;
    }
}

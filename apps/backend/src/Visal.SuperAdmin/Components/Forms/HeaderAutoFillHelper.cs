using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Llena automaticamente los campos del encabezado del documento (FormHeader.Campos)
/// usando heuristica sobre el Label: No Historia / Consecutivo se derivan del HC y
/// NUNCA los edita el medico. La Fecha (y la Hora) se pre-llenan con la apertura del
/// HC pero, mientras la HC este ABIERTA, quedan editables para que el medico pueda
/// ajustarlas; al cerrar la HC vuelven a bloquearse.
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
    /// <param name="hcAbierta">Si la HC esta abierta, los campos de fecha/hora
    /// quedan editables (no se agregan a bloqueadas) y solo se siembran cuando
    /// estan vacios, para no pisar lo que el medico haya ajustado.</param>
    public static HashSet<string> Aplicar(
        Dictionary<string, string?> valores,
        FormHeader? header,
        Guid hcId,
        DateTimeOffset fechaApertura,
        bool hcAbierta = false)
    {
        var bloqueadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (header is null) { return bloqueadas; }

        // Consecutivo legible del HC: ultimos 8 chars del Guid (estable, unico).
        var consecutivo = hcId.ToString("N")[^8..].ToUpperInvariant();
        var local = fechaApertura.ToLocalTime();
        // La fecha ahora incluye la hora (dd/MM/yyyy HH:mm) para que el documento
        // muestre ambas y el medico pueda editarlas mientras la HC este abierta.
        var fechaHora = local.ToString("dd/MM/yyyy HH:mm");
        var hora = local.ToString("HH:mm");

        foreach (var f in header.Campos)
        {
            var key = "hdr:" + f.Id;
            var label = (f.Label ?? "").Trim().ToLowerInvariant();
            var tieneValor = valores.TryGetValue(key, out var actual) && !string.IsNullOrWhiteSpace(actual);

            if (ContainsAny(label, "historia", "hc", "consecutivo", "no."))
            {
                // Identificador del sistema: siempre autogenerado y bloqueado.
                valores[key] = consecutivo;
                bloqueadas.Add(key);
            }
            else if (label.Contains("hora") && !label.Contains("fecha"))
            {
                // Campo de solo hora. Se siembra si esta vacio; editable con HC abierta.
                if (!tieneValor) { valores[key] = hora; }
                if (!hcAbierta) { bloqueadas.Add(key); }
            }
            else if (label.Contains("fecha"))
            {
                // Fecha (puede venir combinada, p.ej. "Ciudad y Fecha"): incluye la
                // hora. Se siembra con la fecha/hora de apertura si esta vacio o si
                // aun tiene el valor "legacy" (solo fecha, igual a la apertura, que
                // antes se ponia y bloqueaba). Asi las HC viejas ganan la hora al
                // abrirse, pero NO se pisan las ediciones del medico. Editable con la
                // HC abierta; bloqueado al cerrar.
                var soloFecha = local.ToString("dd/MM/yyyy");
                if (!tieneValor || string.Equals(actual?.Trim(), soloFecha, StringComparison.Ordinal))
                {
                    valores[key] = fechaHora;
                }
                if (!hcAbierta) { bloqueadas.Add(key); }
            }
            // Otros labels (p.ej. "Ciudad" sola) no se autollenan ni se bloquean.
        }
        return bloqueadas;
    }

    private static bool ContainsAny(string s, params string[] needles)
    {
        foreach (var n in needles) { if (s.Contains(n)) { return true; } }
        return false;
    }
}

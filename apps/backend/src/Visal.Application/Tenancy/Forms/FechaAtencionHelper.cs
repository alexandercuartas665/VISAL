using System.Globalization;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Extrae la "fecha real de la atencion" desde los valores diligenciados de una
/// HC, guiandose por el flag <c>IsFechaAtencion</c> declarado en el schema.
///
/// Reglas:
/// - Solo se consideran nodos de tipo "field" con FieldType date | datetime y
///   IsFechaAtencion = true.
/// - Si varios campos estan marcados, se toma la MAYOR fecha entre los que
///   tengan valor no vacio.
/// - Si ninguno esta marcado o ninguno tiene valor valido, devuelve null y el
///   servicio deja intacto el <c>HistoriaClinica.FechaAtencion</c> previo (o
///   null si nunca se seteo).
///
/// El parseo tolera:
/// - "yyyy-MM-dd" (fieldType=date)
/// - "yyyy-MM-ddTHH:mm" y "yyyy-MM-ddTHH:mm:ss" (fieldType=datetime)
/// - ISO 8601 con offset ("yyyy-MM-ddTHH:mm:ssZ", con timezone)
/// - Otros formatos culture-agnostic parseables por DateTimeOffset.TryParse.
///
/// Las fechas sin hora se interpretan como medianoche local (Bogota).
/// </summary>
public static class FechaAtencionHelper
{
    public static DateTimeOffset? Calcular(FormSchema? schema, IReadOnlyDictionary<string, string?>? valores)
    {
        if (schema is null || valores is null || valores.Count == 0) { return null; }

        DateTimeOffset? mayor = null;
        Recurse(schema.Children);

        // Fallback de cabecera: si NINGUN campo del cuerpo marcado IsFechaAtencion
        // aporto una fecha, usar el campo de HEADER "Ciudad y Fecha" (o cualquier
        // campo de cabecera cuyo label contenga "fecha"). Su valor vive en
        // valores["hdr:{id}"] y en la practica viene como "dd/MM/yyyy [HH:mm]"
        // (Colombia). La mayoria de formatos (HC-FO-*, PP-FO-85_*, etc.) capturan la
        // fecha de atencion SOLO en el header, que el escaneo del cuerpo nunca ve.
        if (mayor is null && schema.Header?.Campos is { Count: > 0 } campos)
        {
            foreach (var c in campos)
            {
                if (string.IsNullOrWhiteSpace(c.Label)
                    || c.Label.IndexOf("fecha", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                if (!valores.TryGetValue("hdr:" + c.Id, out var raw) || string.IsNullOrWhiteSpace(raw)) { continue; }
                if (TryParseHeaderFecha(raw!, out var dt))
                {
                    if (mayor is null || dt > mayor) { mayor = dt; }
                }
            }
        }

        return mayor;

        void Recurse(IEnumerable<FormNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                if (n.IsText) { continue; }
                if (!n.IsFechaAtencion) { continue; }
                if (string.IsNullOrWhiteSpace(n.Name)) { continue; }

                // Aceptamos date/datetime y tambien text: algunos formatos (p.ej.
                // PP-FO-85_F) guardan la fecha de atencion en un campo text con un
                // valor de fecha. Como el campo esta marcado IsFechaAtencion, la
                // intencion es que sea una fecha; si el valor no parsea, se ignora.
                var tipoOk = n.FieldType is "date" or "datetime" or "text";
                if (!tipoOk) { continue; }

                if (!valores.TryGetValue(n.Name, out var raw)) { continue; }
                if (string.IsNullOrWhiteSpace(raw)) { continue; }

                if (TryParse(raw, out var dt))
                {
                    // Npgsql 6+ exige offset=0 (UTC) para timestamp with time zone.
                    // Si el string vino sin timezone, TryParse aplica AssumeLocal
                    // y quedaria con offset -05:00 (Bogota), lo que hace que
                    // SaveChangesAsync tire InvalidCastException. Normalizamos.
                    if (dt.Offset != TimeSpan.Zero) { dt = dt.ToUniversalTime(); }
                    if (mayor is null || dt > mayor) { mayor = dt; }
                }
            }
        }
    }

    // Formatos aceptados para el campo de cabecera "Ciudad y Fecha". Colombia usa
    // dd/MM/yyyy; NO se usa InvariantCulture generica (leeria 04/09 como 9 de abril).
    private static readonly string[] HeaderFechaFormatos =
    {
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yyyy HH:mm", "d/M/yyyy H:mm",
        "dd/MM/yyyy", "d/M/yyyy",
        "dd-MM-yyyy HH:mm", "dd-MM-yyyy",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"
    };

    /// <summary>
    /// Parsea el valor del campo de cabecera "Ciudad y Fecha". Acepta un prefijo de
    /// ciudad opcional ("Pasto, 04/09/2026 10:00") tomando lo que sigue a la ultima
    /// coma, y formatos dd/MM/yyyy [HH:mm] (Colombia) e ISO. Los componentes se tratan
    /// como hora UTC (offset 0) para preservar el dia/hora tal cual se digito y evitar
    /// corrimientos de zona horaria.
    /// </summary>
    private static bool TryParseHeaderFecha(string raw, out DateTimeOffset value)
    {
        value = default;
        var s = raw.Trim();
        if (s.Length == 0) { return false; }
        var coma = s.LastIndexOf(',');
        if (coma >= 0 && coma < s.Length - 1) { s = s[(coma + 1)..].Trim(); }
        return DateTimeOffset.TryParseExact(s, HeaderFechaFormatos, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static bool TryParse(string raw, out DateTimeOffset value)
    {
        // ISO 8601 primero (culture invariant).
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out value)) { return true; }
        if (DateTimeOffset.TryParse(raw, out value)) { return true; }
        value = default;
        return false;
    }
}

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
        return mayor;

        void Recurse(IEnumerable<FormNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                if (n.IsText) { continue; }
                if (!n.IsFechaAtencion) { continue; }
                if (string.IsNullOrWhiteSpace(n.Name)) { continue; }

                var esFechaODateTime = n.FieldType == "date" || n.FieldType == "datetime";
                if (!esFechaODateTime) { continue; }

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

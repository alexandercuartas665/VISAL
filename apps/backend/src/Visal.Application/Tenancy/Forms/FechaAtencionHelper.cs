using System.Globalization;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Extrae la "fecha real de la atencion" desde los valores diligenciados de una
/// HC, guiandose por el flag <c>IsFechaAtencion</c> declarado en el schema.
///
/// Reglas:
/// - Solo se consideran nodos de tipo "field" con FieldType date | datetime y
///   IsFechaAtencion = true.
/// - Si varios campos estan marcados, se toma la fecha/hora MAS TEMPRANA (minima)
///   entre los que tengan valor no vacio: la atencion es cuando INICIA, no cuando
///   termina. Antes se tomaba la MAYOR, lo que en formatos de turno (p. ej. PP-FO-84)
///   agarraba la hora de ENTREGA (fin) en vez de la de RECEPCION (inicio).
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
                    // Guardia: descarta anios imposibles y fechas futuras (ver abajo).
                    if (!EnRangoAtencion(dt)) { continue; }
                    // Mas temprana = inicio de la atencion (ver doc del tipo).
                    if (mayor is null || dt < mayor) { mayor = dt; }
                }
            }
        }

        // Enriquecer con la HORA: varios formatos capturan la fecha en un campo y la
        // hora en OTRO campo aparte ("hora_atencion" = "15:30"). El escaneo de arriba
        // solo ve la fecha, asi que la atencion queda a medianoche. Si la fecha resuelta
        // quedo a las 00:00 (hora Bogota) y hay un campo de hora de atencion con un
        // valor HH:mm, combinamos ambos para tener la hora real de la atencion.
        if (mayor is DateTimeOffset m)
        {
            var bog = m.ToOffset(BogotaOffset);
            if (bog.TimeOfDay == TimeSpan.Zero && TryGetHoraAtencion(valores, out var hora))
            {
                mayor = new DateTimeOffset(bog.Year, bog.Month, bog.Day,
                    hora.Hours, hora.Minutes, 0, BogotaOffset).ToUniversalTime();
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
                    // Guardia SOLO para la fecha de atencion: descarta anios imposibles
                    // (typos tipo 0006/2626) y fechas futuras (una atencion no puede ser
                    // futura). No afecta otros campos de fecha (nacimiento, proxima cita).
                    if (!EnRangoAtencion(dt)) { continue; }
                    // Mas temprana = inicio de la atencion: en formatos de turno evita
                    // agarrar la entrega (fin) en vez de la recepcion (inicio).
                    if (mayor is null || dt < mayor) { mayor = dt; }
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

    // Colombia usa UTC-5 fijo (sin horario de verano). La hora que el usuario digita en
    // "Ciudad y Fecha" es hora de pared local de Bogota.
    private static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Rango valido para una FECHA DE ATENCION: anio >= 1900 (descarta typos imposibles
    /// como 0006/0226/2626) y la fecha no puede ser FUTURA (una atencion no ocurre
    /// manana). Se evalua en fecha de pared de Bogota. Solo se aplica aqui — a la fecha
    /// de atencion — no a otros campos (nacimiento, proxima cita, vigencias) que pueden
    /// ser muy antiguos o futuros legitimamente.
    /// </summary>
    private static bool EnRangoAtencion(DateTimeOffset dt)
    {
        var bog = dt.ToOffset(BogotaOffset);
        var hoy = DateTimeOffset.UtcNow.ToOffset(BogotaOffset).Date;
        return bog.Year >= 1900 && bog.Date <= hoy;
    }

    /// <summary>
    /// Parsea el valor del campo de cabecera "Ciudad y Fecha". Acepta un prefijo de
    /// ciudad opcional ("Pasto, 04/09/2026 10:00") tomando lo que sigue a la ultima
    /// coma, y formatos dd/MM/yyyy [HH:mm] (Colombia) e ISO.
    ///
    /// La hora digitada es hora LOCAL de Bogota (UTC-5), asi que se interpreta con ese
    /// offset y se guarda el instante en UTC (offset 0, que Npgsql exige para
    /// timestamp with time zone). Antes se usaba AssumeUniversal (tratar la hora como
    /// UTC), lo que corria -5h al localizar en impresion: 07:00 digitado se imprimia
    /// 02:00 (y una hora de madrugada podia caer al dia anterior).
    /// </summary>
    private static bool TryParseHeaderFecha(string raw, out DateTimeOffset value)
    {
        value = default;
        var s = raw.Trim();
        if (s.Length == 0) { return false; }
        var coma = s.LastIndexOf(',');
        if (coma >= 0 && coma < s.Length - 1) { s = s[(coma + 1)..].Trim(); }
        if (!DateTime.TryParseExact(s, HeaderFechaFormatos, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var naive))
        {
            return false;
        }
        value = new DateTimeOffset(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), BogotaOffset)
            .ToUniversalTime();
        return true;
    }

    /// <summary>
    /// Busca en los valores un campo de HORA de atencion (clave "hora_atencion", o
    /// cualquier clave que contenga "hora" y "atenc") con un valor HH:mm. Prioriza la
    /// clave exacta. No confunde con campos clinicos como "hora_hambre"/"horas_sueno".
    /// </summary>
    private static bool TryGetHoraAtencion(IReadOnlyDictionary<string, string?> valores, out TimeSpan hora)
    {
        hora = default;
        // 1) clave exacta (cuerpo o header).
        if ((valores.TryGetValue("hora_atencion", out var exact) && TryParseHora(exact, out hora))
            || (valores.TryGetValue("hdr:hora_atencion", out var exactH) && TryParseHora(exactH, out hora)))
        {
            return true;
        }
        // 2) fallback: cualquier clave con "hora" + "atenc".
        foreach (var kv in valores)
        {
            var k = kv.Key;
            if (k is null) { continue; }
            if (k.IndexOf("hora", StringComparison.OrdinalIgnoreCase) < 0
                || k.IndexOf("atenc", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
            if (TryParseHora(kv.Value, out hora)) { return true; }
        }
        return false;
    }

    private static readonly string[] HoraFormatos = { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss" };

    private static bool TryParseHora(string? raw, out TimeSpan hora)
    {
        hora = default;
        if (string.IsNullOrWhiteSpace(raw)) { return false; }
        // Backstop: normalizamos con la MISMA logica que el control de captura
        // (HoraNormalizer) antes de parsear. Asi un valor recuperable que entro por
        // fuera de la UI (prefill, import, datos legacy) — "10.45", "17}:30",
        // "10.00AM" — produce la hora correcta en vez de perderse y dejar la atencion
        // a las 00:00. Lo no convertible devuelve null y no aporta hora.
        var norm = HoraNormalizer.Normalizar(raw);
        if (string.IsNullOrEmpty(norm)) { return false; }
        if (DateTime.TryParseExact(norm, HoraFormatos, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            hora = dt.TimeOfDay;
            return true;
        }
        return false;
    }

    // Formatos aceptados para un campo de CUERPO marcado IsFechaAtencion, SIN zona
    // horaria en el string. Mismo criterio que la cabecera "Ciudad y Fecha": Colombia
    // es dd/MM/yyyy y NUNCA MM/dd. Usar InvariantCulture generico leia "06/10/2026"
    // como 6 de JUNIO (MM/dd) y transponia dia/mes en impresion cuando el dia era <= 12.
    // ISO primero por ser no ambiguo. El valor naive se interpreta como hora de pared de
    // Bogota (UTC-5), igual que TryParseHeaderFecha, y se guarda en UTC (Npgsql exige
    // offset 0 para timestamp with time zone).
    private static readonly string[] CuerpoFechaSinZona =
    {
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "dd/MM/yyyy",
        "d/M/yyyy HH:mm", "d/M/yyyy H:mm", "d/M/yyyy",
        "dd-MM-yyyy HH:mm", "dd-MM-yyyy", "d-M-yyyy HH:mm", "d-M-yyyy",
    };

    // True si el string ISO trae zona explicita: termina en 'Z' o tiene un offset
    // ("+hh:mm" / "-hh:mm") DESPUES de la 'T'. Evita confundir el '-' que separa la
    // fecha (yyyy-MM-dd) con un offset negativo.
    private static bool TieneZonaExplicita(string s)
    {
        if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) { return true; }
        var t = s.IndexOf('T');
        if (t < 0) { return false; }
        var tail = s[(t + 1)..];
        return tail.Contains('+') || tail.Contains('-');
    }

    private static bool TryParse(string raw, out DateTimeOffset value)
    {
        value = default;
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s)) { return false; }

        // 1) ISO con zona explicita en el string: es un instante absoluto, se respeta.
        if (TieneZonaExplicita(s)
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        // 2) ISO sin zona + dd/MM Colombia (RIGIDO: nunca MM/dd). Naive = hora de pared
        //    de Bogota, guardado en UTC.
        if (DateTime.TryParseExact(s, CuerpoFechaSinZona, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var naive))
        {
            value = new DateTimeOffset(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), BogotaOffset)
                .ToUniversalTime();
            return true;
        }

        return false;
    }
}

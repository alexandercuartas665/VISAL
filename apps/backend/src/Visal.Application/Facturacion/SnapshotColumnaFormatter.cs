using System.Globalization;
using Visal.Domain.Enums;

namespace Visal.Application.Facturacion;

/// <summary>
/// Convierte el valor crudo de una celda del snapshot al formato de salida elegido por el tenant
/// (fecha, numero, moneda, etc.). Provee: el patron Excel para la celda (ClosedXML), el string
/// formateado para el CSV, y los parsers tolerantes que ambos usan. Cultura de referencia: es-CO
/// (separador de miles ".", decimal ","). Si el valor no se puede parsear al tipo pedido, se deja
/// tal cual (nunca rompe la exportacion).
/// </summary>
public static class SnapshotColumnaFormatter
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Intenta interpretar el valor como numero (acepta ya-tipado, invariante o es-CO).</summary>
    public static bool TryNumero(object? val, out double num)
    {
        switch (val)
        {
            case null: num = 0; return false;
            case double d: num = d; return true;
            case decimal m: num = (double)m; return true;
            case long l: num = l; return true;
            case int i: num = i; return true;
        }
        var s = val.ToString();
        if (string.IsNullOrWhiteSpace(s)) { num = 0; return false; }
        s = s.Trim();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out num)
            || double.TryParse(s, NumberStyles.Any, Co, out num);
    }

    /// <summary>Intenta interpretar el valor como fecha (acepta DateTime, ISO, o formato local).</summary>
    public static bool TryFecha(object? val, out DateTime fecha)
    {
        switch (val)
        {
            case null: fecha = default; return false;
            case DateTime dt: fecha = dt; return true;
            case DateTimeOffset dto: fecha = dto.DateTime; return true;
        }
        var s = val.ToString();
        if (string.IsNullOrWhiteSpace(s)) { fecha = default; return false; }
        s = s.Trim();
        // es-CO primero: interpreta dd/MM/yyyy (formato colombiano) y tambien ISO yyyy-MM-dd.
        // Invariante como respaldo. Evita que "01/08/2026" se lea como MM/dd (8 de enero).
        return DateTime.TryParse(s, Co, DateTimeStyles.None, out fecha)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
    }

    /// <summary>Patron de formato de celda para Excel (ClosedXML NumberFormat.Format). Null = sin patron.</summary>
    public static string? ExcelPattern(SnapshotColumnaFormato tipo, string? patron) => tipo switch
    {
        SnapshotColumnaFormato.Texto => "@",
        SnapshotColumnaFormato.NumeroEntero => "#,##0",
        SnapshotColumnaFormato.NumeroDecimal => "#,##0.00",
        SnapshotColumnaFormato.Moneda => "\"$\" #,##0.00",
        SnapshotColumnaFormato.Porcentaje => "0.00%",
        SnapshotColumnaFormato.Fecha => "dd/mm/yyyy",
        SnapshotColumnaFormato.FechaHora => "dd/mm/yyyy hh:mm",
        SnapshotColumnaFormato.FechaIso => "yyyy/mm/dd",
        SnapshotColumnaFormato.FechaHoraIso => "yyyy/mm/dd hh:mm",
        SnapshotColumnaFormato.Personalizado => string.IsNullOrWhiteSpace(patron) ? null : patron,
        _ => null
    };

    /// <summary>Devuelve el valor ya formateado como texto para el CSV.</summary>
    public static string FormatoCsv(object? val, SnapshotColumnaFormato tipo, string? patron)
    {
        if (val is null) { return string.Empty; }
        switch (tipo)
        {
            case SnapshotColumnaFormato.NumeroEntero:
                return TryNumero(val, out var e) ? e.ToString("#,##0", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.NumeroDecimal:
                return TryNumero(val, out var d) ? d.ToString("#,##0.00", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.Moneda:
                return TryNumero(val, out var mo) ? "$ " + mo.ToString("#,##0.00", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.Porcentaje:
                return TryNumero(val, out var p) ? p.ToString("0.00%", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.Fecha:
                return TryFecha(val, out var f) ? f.ToString("dd/MM/yyyy", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.FechaHora:
                return TryFecha(val, out var fh) ? fh.ToString("dd/MM/yyyy HH:mm", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.FechaIso:
                return TryFecha(val, out var fi) ? fi.ToString("yyyy/MM/dd", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.FechaHoraIso:
                return TryFecha(val, out var fhi) ? fhi.ToString("yyyy/MM/dd HH:mm", Co) : val.ToString() ?? string.Empty;
            case SnapshotColumnaFormato.Texto:
            case SnapshotColumnaFormato.Personalizado:
            case SnapshotColumnaFormato.General:
            default:
                return val.ToString() ?? string.Empty;
        }
    }
}

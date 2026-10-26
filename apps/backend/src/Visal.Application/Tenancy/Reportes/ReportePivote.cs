using System.Globalization;

namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Reshape (Ola 3b) de un resultado Agregado "largo" (group-by de DOS campos + un agregado)
/// a una matriz pivote: un campo en las filas, el otro abierto en columnas, el primer
/// agregado en las celdas. Es logica pura (no toca BD): opera sobre un
/// <see cref="ReporteHojaResultado"/> ya ejecutado. Caso canonico: Consolidado Encuesta
/// (fila = pregunta, columna = mes YYYYMM, celda = conteo).
/// </summary>
public static class ReportePivote
{
    public static ReporteHojaResultado Aplicar(ReporteHojaDef hoja, ReporteHojaResultado largo)
    {
        var piv = hoja.Pivote
            ?? throw new InvalidOperationException("La hoja no tiene configuracion de pivote.");
        if (hoja.Modo != ReporteHojaModo.Agregado)
        {
            throw new InvalidOperationException("El pivote solo aplica a hojas en modo Agregado.");
        }
        if (hoja.GroupBy.Count != 2)
        {
            throw new InvalidOperationException("El pivote requiere exactamente dos campos de agrupacion (fila y columna).");
        }
        if (hoja.Agregados.Count == 0)
        {
            throw new InvalidOperationException("El pivote requiere al menos un agregado para las celdas.");
        }

        var filaIdx = IndiceGroupBy(hoja, piv.FilaCampoRef, "fila");
        var colIdx = IndiceGroupBy(hoja, piv.ColumnaCampoRef, "columna");
        if (filaIdx == colIdx)
        {
            throw new InvalidOperationException("El pivote necesita un campo de fila distinto del de columna.");
        }
        // El primer agregado va despues de los dos group-by en la salida "larga".
        var valIdx = hoja.GroupBy.Count;

        // Claves distintas de fila y columna, ordenadas (numerico si todo es numerico).
        var filas = ClavesOrdenadas(largo.Filas, filaIdx);
        var columnas = ClavesOrdenadas(largo.Filas, colIdx);

        // Mapa (claveFila|claveColumna) -> valor de celda.
        var celdas = new Dictionary<(string, string), object?>();
        foreach (var row in largo.Filas)
        {
            var kf = Clave(En(row, filaIdx));
            var kc = Clave(En(row, colIdx));
            celdas[(kf, kc)] = En(row, valIdx);
        }

        // Columnas de salida: [titulo del campo fila, col1, col2, ...].
        var tituloFila = filaIdx < largo.Columnas.Count ? largo.Columnas[filaIdx] : "Fila";
        var columnasSalida = new List<string> { tituloFila };
        columnasSalida.AddRange(columnas.Select(c => Encabezado(c.Original)));

        var filasSalida = new List<IReadOnlyList<object?>>(filas.Count);
        foreach (var f in filas)
        {
            var fila = new object?[columnas.Count + 1];
            fila[0] = Encabezado(f.Original);
            for (var i = 0; i < columnas.Count; i++)
            {
                celdas.TryGetValue((f.Clave, columnas[i].Clave), out var v);
                fila[i + 1] = v;
            }
            filasSalida.Add(fila);
        }

        return new ReporteHojaResultado(largo.Nombre, columnasSalida, filasSalida);
    }

    private static int IndiceGroupBy(ReporteHojaDef hoja, string campoRef, string cual)
    {
        for (var i = 0; i < hoja.GroupBy.Count; i++)
        {
            if (string.Equals(hoja.GroupBy[i].CampoRef, campoRef, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        throw new InvalidOperationException($"El campo de {cual} del pivote ('{campoRef}') no esta en la agrupacion de la hoja.");
    }

    private static object? En(IReadOnlyList<object?> row, int idx) => idx < row.Count ? row[idx] : null;

    private sealed record ClaveValor(string Clave, object? Original);

    // Lista de claves distintas en una columna, ordenada: numerica si todos los valores no
    // nulos son numericos; si no, ordinal por texto. Los nulos van al final.
    private static List<ClaveValor> ClavesOrdenadas(IReadOnlyList<IReadOnlyList<object?>> filas, int idx)
    {
        var vistas = new Dictionary<string, object?>();
        foreach (var row in filas)
        {
            var v = En(row, idx);
            var k = Clave(v);
            if (!vistas.ContainsKey(k)) { vistas[k] = v; }
        }

        var items = vistas.Select(kv => new ClaveValor(kv.Key, kv.Value)).ToList();
        var todoNumerico = items.Where(i => i.Original is not null).All(i => EsNumerico(i.Original!));

        items.Sort((a, b) =>
        {
            if (a.Original is null) { return b.Original is null ? 0 : 1; }
            if (b.Original is null) { return -1; }
            if (todoNumerico)
            {
                return Convert.ToDecimal(a.Original, CultureInfo.InvariantCulture)
                    .CompareTo(Convert.ToDecimal(b.Original, CultureInfo.InvariantCulture));
            }
            return string.Compare(a.Clave, b.Clave, StringComparison.Ordinal);
        });
        return items;
    }

    private static bool EsNumerico(object v) =>
        v is int or long or short or byte or decimal or double or float;

    private static string Clave(object? v) =>
        v is null ? "\0" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";

    // Encabezado legible: un entero YYYYMM se muestra como "YYYY-MM"; el resto, tal cual.
    private static string Encabezado(object? v)
    {
        if (v is null) { return "(vacio)"; }
        if (v is int or long or short)
        {
            var n = Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (n is >= 190001 and <= 999912)
            {
                var mes = (int)(n % 100);
                if (mes is >= 1 and <= 12)
                {
                    return $"{n / 100:0000}-{mes:00}";
                }
            }
        }
        return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
    }
}

using System.Globalization;

namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Constructor dedicado de la hoja "Consolidado Encuesta" (hoja 5 del Censo): una MATRIZ fiel
/// al Excel canonico que el pivote generico no puede expresar (dos bloques de pregunta con
/// conteo + porcentaje y un total de llamadas efectivas por mes). Es logica pura (no toca BD):
/// opera sobre las filas crudas ya ejecutadas por la consulta base de la hoja, que deben traer
/// (mes YYYYMM, pregunta4, pregunta5) ya acotadas a llamadas efectivas por el filtro de la hoja.
///
/// Salida por mes (fila): etiqueta del mes, por cada cubo del bloque P4 (conteo + %), por cada
/// cubo del bloque P5 (conteo + %) y el total de llamadas efectivas del mes. El % es
/// conteo/total del mes (0 si el mes no tuvo llamadas). Ademas arma la cabecera de dos niveles:
/// la fila de grupo fusionada (esquina, titulo P4, titulo P5, titulo total) y la fila de columnas
/// (Mes, etiquetas de cada cubo con "%" intercalado, y una celda vacia bajo el total).
/// </summary>
public static class ReporteMatrizEncuesta
{
    private static readonly string[] MesesEs =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public static ReporteHojaResultado Aplicar(ReporteHojaDef hoja, ReporteHojaResultado crudo)
    {
        var m = hoja.Matriz
            ?? throw new InvalidOperationException("La hoja no tiene configuracion de matriz de encuesta.");

        // Conteos por mes: total de la fila + conteo por valor de cada bloque.
        var porMes = new SortedDictionary<int, MesAcum>();
        foreach (var row in crudo.Filas)
        {
            var mes = EnteroONulo(En(row, m.ColMes));
            if (mes is not int mm) { continue; }
            if (!porMes.TryGetValue(mm, out var acum)) { acum = new MesAcum(); porMes[mm] = acum; }

            acum.Total++;
            var p4 = EnteroONulo(En(row, m.ColP4));
            if (p4 is int v4) { acum.P4[v4] = acum.P4.GetValueOrDefault(v4) + 1; }
            var p5 = EnteroONulo(En(row, m.ColP5));
            if (p5 is int v5) { acum.P5[v5] = acum.P5.GetValueOrDefault(v5) + 1; }
        }

        // --- Columnas (fila de nombres, nivel 2) ---
        var columnas = new List<string> { m.TituloMes };
        var formatos = new List<string?> { "@" };
        foreach (var cubo in m.BloqueP4.Cubos) { columnas.Add(cubo.Etiqueta); columnas.Add("%"); formatos.Add("#,##0"); formatos.Add("0.0\"%\""); }
        foreach (var cubo in m.BloqueP5.Cubos) { columnas.Add(cubo.Etiqueta); columnas.Add("%"); formatos.Add("#,##0"); formatos.Add("0.0\"%\""); }
        columnas.Add(string.Empty); // bajo el total: el titulo va en la fila de grupo.
        formatos.Add("#,##0");

        // --- Cabecera de grupo (nivel 1, fusionada) ---
        var grupos = new List<ReporteGrupoColumna>
        {
            new(m.TituloEsquina, 1),
            new(m.BloqueP4.Titulo, m.BloqueP4.Cubos.Count * 2),
            new(m.BloqueP5.Titulo, m.BloqueP5.Cubos.Count * 2),
            new(m.TituloTotal, 1),
        };

        // --- Filas (una por mes) ---
        var filas = new List<IReadOnlyList<object?>>(porMes.Count);
        foreach (var (mes, acum) in porMes)
        {
            var fila = new List<object?> { EtiquetaMes(mes) };
            Volcar(fila, m.BloqueP4, acum.P4, acum.Total);
            Volcar(fila, m.BloqueP5, acum.P5, acum.Total);
            fila.Add(acum.Total);
            filas.Add(fila);
        }

        return new ReporteHojaResultado(crudo.Nombre, columnas, filas, formatos, grupos);
    }

    private static void Volcar(List<object?> fila, ReporteMatrizBloqueDef bloque, Dictionary<int, int> conteos, int total)
    {
        foreach (var cubo in bloque.Cubos)
        {
            var n = conteos.GetValueOrDefault(cubo.Valor);
            fila.Add(n);
            fila.Add(total > 0 ? Math.Round(n * 100.0 / total, 1) : 0.0);
        }
    }

    private sealed class MesAcum
    {
        public int Total;
        public Dictionary<int, int> P4 { get; } = new();
        public Dictionary<int, int> P5 { get; } = new();
    }

    private static object? En(IReadOnlyList<object?> row, int idx) => idx >= 0 && idx < row.Count ? row[idx] : null;

    private static int? EnteroONulo(object? v)
    {
        if (v is null) { return null; }
        if (v is int i) { return i; }
        if (v is long or short or byte) { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : null;
    }

    // YYYYMM -> "Marzo 2026". Si el entero no parece un periodo valido, lo devuelve tal cual.
    private static string EtiquetaMes(int yyyymm)
    {
        var mes = yyyymm % 100;
        var anio = yyyymm / 100;
        return mes is >= 1 and <= 12 ? $"{MesesEs[mes - 1]} {anio}" : yyyymm.ToString(CultureInfo.InvariantCulture);
    }
}

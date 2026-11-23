using System.Globalization;
using System.Text.RegularExpressions;

namespace Visal.Application.Facturacion;

/// <summary>
/// Operadores del filtro por columna de la vista "Ver" de un snapshot y su traduccion a una
/// condicion SQL sobre <c>datos_json-&gt;&gt;'columna'</c> (Postgres). Los valores siempre van
/// parametrizados ({n} de FromSqlRaw); la expresion de la columna la arma el llamador tras
/// validarla contra el catalogo del builder. Ninguna literal lleva llaves (chocarian con el
/// formato de FromSqlRaw).
/// </summary>
public static class SnapshotFiltroOperador
{
    public const string Contiene = "contiene";
    public const string NoContiene = "no_contiene";
    public const string Igual = "igual";
    public const string Distinto = "distinto";
    public const string EmpiezaPor = "empieza";
    public const string Mayor = "mayor";
    public const string Menor = "menor";
    public const string MayorIgual = "mayor_igual";
    public const string MenorIgual = "menor_igual";
    public const string Vacio = "vacio";
    public const string NoVacio = "no_vacio";

    /// <summary>Operadores en el orden en que se ofrecen en la UI, con su etiqueta.</summary>
    public static readonly IReadOnlyList<(string Codigo, string Etiqueta)> Todos = new[]
    {
        (Contiene, "contiene"),
        (NoContiene, "no contiene"),
        (Igual, "es igual a"),
        (Distinto, "es distinto de"),
        (EmpiezaPor, "empieza por"),
        (Mayor, "mayor que"),
        (MayorIgual, "mayor o igual a"),
        (Menor, "menor que"),
        (MenorIgual, "menor o igual a"),
        (Vacio, "esta vacio"),
        (NoVacio, "no esta vacio"),
    };

    /// <summary>Operador normalizado (null / desconocido = contiene, compatibilidad con filtros viejos).</summary>
    public static string Normalizar(string? op) =>
        Todos.Any(t => t.Codigo == op) ? op! : Contiene;

    /// <summary>True si el operador no usa valor (vacio / no vacio).</summary>
    public static bool SinValor(string? op) => Normalizar(op) is Vacio or NoVacio;

    public static string Etiqueta(string? op) => Todos.First(t => t.Codigo == Normalizar(op)).Etiqueta;

    // Texto numerico: "0", "46800", "46800.00", "-3,5" (coma decimal aceptada en la entrada).
    private const string RegexNumeroSql = "^\\s*-?[0-9]+([.][0-9]+)?\\s*$";
    // Valor de fecha guardado en el JSON: empieza por aaaa-mm-dd.
    private const string RegexFechaSql = "^[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]";

    private static readonly Regex RxNumeroEntrada = new(@"^\s*-?\d+([.,]\d+)?\s*$", RegexOptions.Compiled);
    private static readonly string[] FormatosFecha = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy" };

    /// <summary>
    /// Condicion SQL para un filtro. <paramref name="expr"/> es la expresion de la columna
    /// (p.ej. <c>datos_json-&gt;&gt;'Fecha'</c>); los parametros se agregan a <paramref name="vals"/>
    /// y se referencian como {indice}. Devuelve null si el filtro no aplica (valor vacio en un
    /// operador que lo necesita).
    /// </summary>
    public static string? ConstruirCondicion(string expr, string? operador, string? valor, List<object> vals)
    {
        var op = Normalizar(operador);
        var v = (valor ?? "").Trim();
        string P(object o) { vals.Add(o); return "{" + (vals.Count - 1) + "}"; }

        switch (op)
        {
            case Vacio:
                return $"({expr} IS NULL OR btrim({expr}) = '')";
            case NoVacio:
                return $"({expr} IS NOT NULL AND btrim({expr}) <> '')";
        }
        if (v.Length == 0) { return null; }

        var numero = AComoNumero(v);
        var fechaIso = AComoFechaIso(v);
        var numExpr = $"(CASE WHEN {expr} ~ '{RegexNumeroSql}' THEN ({expr})::numeric END)";

        switch (op)
        {
            case Contiene:
                return $"{expr} ILIKE {P("%" + Escapar(v) + "%")}";
            case NoContiene:
                return $"({expr} IS NULL OR {expr} NOT ILIKE {P("%" + Escapar(v) + "%")})";
            case EmpiezaPor:
                return $"{expr} ILIKE {P(Escapar(v) + "%")}";
            case Igual:
                return CondIgual(expr, numExpr, v, numero, fechaIso, P);
            case Distinto:
                // Los vacios no cuentan como "distinto" (para eso esta "esta vacio").
                return $"({expr} IS NOT NULL AND btrim({expr}) <> '' AND NOT {CondIgual(expr, numExpr, v, numero, fechaIso, P)})";
            case Mayor:
            case Menor:
            case MayorIgual:
            case MenorIgual:
                var sqlOp = op switch { Mayor => ">", Menor => "<", MayorIgual => ">=", _ => "<=" };
                if (numero is decimal n) { return $"{numExpr} {sqlOp} {P(n)}"; }
                if (fechaIso is string f)
                {
                    return $"({expr} ~ '{RegexFechaSql}' AND left({expr}, 10) {sqlOp} {P(f)})";
                }
                return $"lower({expr}) {sqlOp} lower({P(v)})";
        }
        return null;
    }

    private static string CondIgual(string expr, string numExpr, string v, decimal? numero, string? fechaIso, Func<object, string> P)
    {
        // Texto exacto (sin distinguir mayusculas ni espacios de los extremos); si el valor es
        // numero tambien compara como numero (46800 = 46800.00); si es fecha, por el dia.
        var cond = $"lower(btrim({expr})) = lower({P(v)})";
        if (numero is decimal n) { cond += $" OR {numExpr} = {P(n)}"; }
        if (fechaIso is string f) { cond += $" OR left({expr}, 10) = {P(f)}"; }
        return "(" + cond + ")";
    }

    /// <summary>Escapa los comodines de LIKE para que el texto se busque literal.</summary>
    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static decimal? AComoNumero(string v)
    {
        if (!RxNumeroEntrada.IsMatch(v)) { return null; }
        return decimal.TryParse(v.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static string? AComoFechaIso(string v) =>
        DateTime.TryParseExact(v.Trim(), FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
}

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Evalua reglas de visibilidad condicional (FormNode.VisibleWhen). Recibe
/// el diccionario de valores actuales del formulario (mismo diccionario que
/// usa el FormViewer) y devuelve true si el nodo debe mostrarse.
///
/// Reglas de diseño:
/// - Si la regla es null, el nodo se muestra siempre → true.
/// - Si el operador es desconocido, damos el beneficio de la duda y mostramos
///   (evitar romper formularios existentes con reglas mal escritas).
/// - Nulos/vacios: para operadores que no sean isEmpty/isNotEmpty, un valor
///   ausente hace la comparacion falsa (el nodo queda OCULTO por defecto —
///   mas seguro clinicamente cuando el paciente no tiene el dato aun).
/// - Comparacion de strings es case-insensitive y con trim.
/// </summary>
public static class VisibleWhenEvaluator
{
    /// <summary>
    /// Evalua la regla contra los valores actuales. Sin regla → visible.
    /// </summary>
    public static bool ShouldShow(VisibleWhenRule? rule, IReadOnlyDictionary<string, string?> values)
    {
        if (rule is null || string.IsNullOrWhiteSpace(rule.Field)) { return true; }

        var actual = LookupValue(values, rule.Field);
        var op = (rule.Operator ?? "equals").Trim().ToLowerInvariant();
        var expected = rule.Value ?? "";

        return op switch
        {
            "isempty" => string.IsNullOrWhiteSpace(actual),
            "isnotempty" => !string.IsNullOrWhiteSpace(actual),
            "equals" => Eq(actual, expected),
            "notequals" => !Eq(actual, expected),
            "in" => ParseList(expected).Any(v => Eq(actual, v)),
            "notin" => !ParseList(expected).Any(v => Eq(actual, v)),
            // Preparado para el futuro — por ahora conservador: sin match → oculto.
            "greaterthan" => TryCompare(actual, expected, out var c) && c > 0,
            "lessthan" => TryCompare(actual, expected, out var c) && c < 0,
            _ => true // operador desconocido: no romper el form
        };
    }

    /// <summary>
    /// Case-insensitive equality con trim.
    /// Nulo ≠ ""; nulo ≠ cualquier string no vacio.
    /// </summary>
    private static bool Eq(string? a, string b)
    {
        if (a is null) { return false; }
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Busca el valor de <paramref name="key"/> en el diccionario. Los valores
    /// pueden venir desde el prefill del paciente o desde el schema. Los
    /// valores nulos se propagan como null.
    /// </summary>
    private static string? LookupValue(IReadOnlyDictionary<string, string?> values, string key)
    {
        return values.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// Parsea el value de "in"/"notIn". Acepta:
    ///  - array JSON: ["A","B"]
    ///  - lista coma-separada: "A,B"
    /// </summary>
    private static IEnumerable<string> ParseList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { yield break; }
        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            List<string>? parsed = null;
            try { parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(trimmed); }
            catch { parsed = null; }
            if (parsed is not null)
            {
                foreach (var p in parsed) { yield return p; }
                yield break;
            }
        }
        foreach (var p in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return p;
        }
    }

    /// <summary>Compara numericamente si ambos parsean; de lo contrario, ordinal.</summary>
    private static bool TryCompare(string? a, string b, out int cmp)
    {
        if (a is null) { cmp = 0; return false; }
        if (double.TryParse(a, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var db))
        {
            cmp = da.CompareTo(db);
            return true;
        }
        cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return true;
    }
}

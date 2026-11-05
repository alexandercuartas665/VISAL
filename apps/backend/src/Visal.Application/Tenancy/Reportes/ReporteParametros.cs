namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Resuelve los parametros de ejecucion (globales) de un pack antes de generar el SQL.
/// Reshape puro y testeable: por cada hoja reescribe sus filtros enlazados a un parametro
/// (<see cref="ReporteFiltroDef.ParamClave"/>) sustituyendo el valor pedido al ejecutar.
/// Un filtro enlazado cuyo parametro llega vacio (o no llega) se DESCARTA: esa hoja no se
/// filtra por el. Los filtros con valor fijo (sin <c>ParamClave</c>) quedan intactos.
/// </summary>
public static class ReporteParametros
{
    /// <summary>
    /// Devuelve una copia del pack con los filtros parametrizados ya resueltos a valores
    /// concretos. <paramref name="valores"/> mapea clave de parametro -&gt; valores elegidos
    /// (vacio o ausente = descartar los filtros que lo usan).
    /// </summary>
    public static ReportePackDef Resolver(
        ReportePackDef pack,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? valores)
    {
        var mapa = valores ?? new Dictionary<string, IReadOnlyList<string>>();
        var hojas = pack.Hojas.Select(h => ResolverHoja(h, mapa)).ToList();
        return pack with { Hojas = hojas };
    }

    private static ReporteHojaDef ResolverHoja(
        ReporteHojaDef hoja, IReadOnlyDictionary<string, IReadOnlyList<string>> valores)
    {
        var hayFiltroParam = hoja.Filtros.Any(f => !string.IsNullOrWhiteSpace(f.ParamClave));
        var hayExisteParam = (hoja.Existencias ?? []).Any(e =>
            !string.IsNullOrWhiteSpace(e.ParamClave) || !string.IsNullOrWhiteSpace(e.ToggleParamClave));
        // Sin nada parametrizado: nada que reescribir.
        if (!hayFiltroParam && !hayExisteParam)
        {
            return hoja;
        }

        List<string> Limpios(string clave) =>
            valores.TryGetValue(clave, out var vals)
                ? vals.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                : [];

        var filtros = new List<ReporteFiltroDef>(hoja.Filtros.Count);
        foreach (var f in hoja.Filtros)
        {
            if (string.IsNullOrWhiteSpace(f.ParamClave))
            {
                filtros.Add(f);
                continue;
            }

            // Filtro enlazado a parametro: solo entra si hay al menos un valor no vacio.
            var limpios = Limpios(f.ParamClave);
            if (limpios.Count == 0)
            {
                continue; // parametro sin valor -> esta hoja no se filtra por el
            }
            filtros.Add(f with { Valores = limpios, ParamClave = null });
        }

        // Existencias: a diferencia de un filtro, la existencia SIEMPRE se mantiene (es "solo lo
        // atendido"); lo que depende del parametro es unicamente el acotar por fecha. Sin valor ->
        // se conserva la existencia pero sin rango (ParamClave y Valores en null).
        var existencias = hoja.Existencias;
        if (existencias is { Count: > 0 })
        {
            var resueltas = new List<ReporteExisteDef>(existencias.Count);
            foreach (var e in existencias)
            {
                // Existencia-interruptor: solo entra si su parametro llega con valor; si no, se descarta
                // (la hoja sale como si esta existencia no estuviera). Un booleano sin marcar llega vacio.
                if (!string.IsNullOrWhiteSpace(e.ToggleParamClave) && Limpios(e.ToggleParamClave).Count == 0)
                {
                    continue;
                }
                var baseEx = string.IsNullOrWhiteSpace(e.ToggleParamClave) ? e : e with { ToggleParamClave = null };

                if (string.IsNullOrWhiteSpace(baseEx.ParamClave))
                {
                    resueltas.Add(baseEx);
                    continue;
                }
                var limpios = Limpios(baseEx.ParamClave);
                resueltas.Add(limpios.Count >= 2
                    ? baseEx with { Valores = limpios, ParamClave = null }
                    : baseEx with { Valores = null, ParamClave = null });
            }
            existencias = resueltas;
        }

        return hoja with { Filtros = filtros, Existencias = existencias };
    }
}

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
        // Sin filtros parametrizados: nada que reescribir.
        if (!hoja.Filtros.Any(f => !string.IsNullOrWhiteSpace(f.ParamClave)))
        {
            return hoja;
        }

        var filtros = new List<ReporteFiltroDef>(hoja.Filtros.Count);
        foreach (var f in hoja.Filtros)
        {
            if (string.IsNullOrWhiteSpace(f.ParamClave))
            {
                filtros.Add(f);
                continue;
            }

            // Filtro enlazado a parametro: solo entra si hay al menos un valor no vacio.
            var limpios = valores.TryGetValue(f.ParamClave, out var vals)
                ? vals.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                : [];
            if (limpios.Count == 0)
            {
                continue; // parametro sin valor -> esta hoja no se filtra por el
            }
            filtros.Add(f with { Valores = limpios, ParamClave = null });
        }

        return hoja with { Filtros = filtros };
    }
}

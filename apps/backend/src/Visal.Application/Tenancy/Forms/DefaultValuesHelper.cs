namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Aplica los <c>DefaultValue</c> declarados en el schema del FormDefinition al
/// diccionario de valores de la HC/escala/evolucion/consentimiento.
///
/// Dos modos de uso:
///
/// 1) <see cref="Aplicar"/> — al CREAR una HC nueva. Se enfoca en las filas
///    semilla (SeedRows) del schema: pre-llena las celdas editables de esas
///    filas con la primera opcion / defaultValue / opciones globales.
///    Cadena de prioridad al iniciar una HC nueva:
///      1) DefaultValue del schema (este helper)
///      2) Prefill paciente (PacientePrefillHelper)
///      3) Prefill historia medica acumulada (HistoriaMedicaPrefillHelper)
///      4) Lo que el doctor escriba a mano en el FormViewer.
///
/// 2) <see cref="HidratarDefaultsAusentes"/> — al REABRIR una HC existente y
///    tambien como red de seguridad al persistir. Recorre las filas dinamicas
///    (leyendo <c>tbl:sec:_rows</c>) y los fields sueltos y escribe el
///    defaultValue en las celdas AUSENTES del diccionario. Idempotente: si la
///    clave ya existe (aunque valga cadena vacia) se respeta.
///    Why: el visor pintaba el defaultValue como placeholder visual pero no
///    lo escribia al modelo cuando las columnas del schema se agregaron
///    despues de que existieran filas capturadas en la HC. Al Guardar el
///    JSON crudo salia sin esas celdas y la impresion mostraba vacio.
/// </summary>
public static class DefaultValuesHelper
{
    public static void Aplicar(Dictionary<string, string?> valores, FormSchema? schema)
    {
        if (schema is null) { return; }
        Recurse(schema.Children);

        void Recurse(IEnumerable<FormNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                if (n.IsText) { continue; }

                if (n.IsTable)
                {
                    // Para tablas con SeedRows: pre-llenamos las celdas EDITABLES
                    // (las que estan vacias en seedRow[i][j]) con un default. El
                    // orden de precedencia es:
                    //   1) Opciones por fila (SeedRowCellOptions[i_j]) -> primera opcion.
                    //      Cubre tablas tipo Examen Fisico donde cada fila tiene sus
                    //      propias opciones (Cabeza, Cardiovascular, etc.).
                    //   2) col.DefaultValue del schema (ej. "NO REFIERE" como fallback global).
                    //   3) col.Options[0] como ultima alternativa.
                    // Las celdas con valor fijo seed (label "Atrofia") no se tocan.
                    if (n.SeedRows is null || n.Columns is null) { continue; }
                    var seedCount = n.SeedRows.Count;
                    for (var i = 0; i < seedCount; i++)
                    {
                        var seedRow = n.SeedRows[i];
                        for (var j = 0; j < n.Columns.Count; j++)
                        {
                            var col = n.Columns[j];
                            var hasSeed = j < seedRow.Count && !string.IsNullOrEmpty(seedRow[j]);
                            if (hasSeed) { continue; }

                            if (!string.IsNullOrEmpty(col.EnabledByColumn))
                            {
                                var triggerCol = n.Columns.FirstOrDefault(c =>
                                    string.Equals(c.Name, col.EnabledByColumn, StringComparison.OrdinalIgnoreCase));
                                if (triggerCol is not null)
                                {
                                    var triggerKey = $"tbl:{n.Id}:{i}:{triggerCol.Id}";
                                    valores.TryGetValue(triggerKey, out var triggerVal);
                                    var habilitada = !string.IsNullOrEmpty(col.EnabledByValue)
                                        && !string.IsNullOrEmpty(triggerVal)
                                        && string.Equals(triggerVal.Trim(), col.EnabledByValue.Trim(),
                                            StringComparison.OrdinalIgnoreCase);
                                    if (!habilitada) { continue; }
                                }
                            }

                            string? defaultParaCelda = null;
                            if (n.SeedRowCellOptions is not null
                                && n.SeedRowCellOptions.TryGetValue($"{i}_{j}", out var rowOpts)
                                && rowOpts is { Count: > 0 })
                            {
                                defaultParaCelda = rowOpts[0];
                            }
                            else if (!string.IsNullOrEmpty(col.DefaultValue))
                            {
                                defaultParaCelda = col.DefaultValue;
                            }
                            else if (col.Options is { Count: > 0 })
                            {
                                defaultParaCelda = col.Options[0];
                            }
                            if (string.IsNullOrEmpty(defaultParaCelda)) { continue; }

                            var cellKey = $"tbl:{n.Id}:{i}:{col.Id}";
                            if (!valores.TryGetValue(cellKey, out var existing) || string.IsNullOrEmpty(existing))
                            {
                                valores[cellKey] = defaultParaCelda;
                            }
                        }
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrEmpty(n.DefaultValue)) { continue; }
                if (!valores.TryGetValue(n.Name!, out var existingField) || string.IsNullOrEmpty(existingField))
                {
                    valores[n.Name!] = n.DefaultValue;
                }
            }
        }
    }

    /// <summary>
    /// Recorre las filas DINAMICAS ya persistidas (segun <c>tbl:sec:_rows</c>) y
    /// los fields sueltos, y escribe el <c>DefaultValue</c> del schema en las
    /// celdas/keys AUSENTES del diccionario. Devuelve <c>true</c> si escribio
    /// al menos un valor.
    ///
    /// Regla de "ausente": solo se escribe si la clave NO existe en el dict.
    /// Si la clave existe con cadena vacia se respeta (el doctor la vacio
    /// deliberadamente y no queremos re-hidratarla al reabrir).
    /// </summary>
    public static bool HidratarDefaultsAusentes(Dictionary<string, string?> valores, FormSchema? schema)
    {
        if (schema is null) { return false; }
        var cambio = false;
        Recurse(schema.Children);
        return cambio;

        void Recurse(IEnumerable<FormNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                if (n.IsText) { continue; }

                if (n.IsTable)
                {
                    if (n.Columns is null) { continue; }
                    if (!valores.TryGetValue($"tbl:{n.Id}:_rows", out var rowsRaw)
                        || !int.TryParse(rowsRaw, out var rows) || rows <= 0)
                    {
                        continue;
                    }

                    for (var i = 0; i < rows; i++)
                    {
                        var seedRow = (n.SeedRows is not null && i < n.SeedRows.Count) ? n.SeedRows[i] : null;
                        for (var j = 0; j < n.Columns.Count; j++)
                        {
                            var col = n.Columns[j];
                            var hasSeed = seedRow is not null && j < seedRow.Count && !string.IsNullOrEmpty(seedRow[j]);
                            if (hasSeed) { continue; }

                            // Columna condicional: si el trigger no matchea en esta fila,
                            // no hidratamos (la celda debe quedar vacia hasta que el trigger
                            // la habilite).
                            if (!string.IsNullOrEmpty(col.EnabledByColumn))
                            {
                                var triggerCol = n.Columns.FirstOrDefault(c =>
                                    string.Equals(c.Name, col.EnabledByColumn, StringComparison.OrdinalIgnoreCase));
                                if (triggerCol is not null)
                                {
                                    var triggerKey = $"tbl:{n.Id}:{i}:{triggerCol.Id}";
                                    valores.TryGetValue(triggerKey, out var triggerVal);
                                    var habilitada = !string.IsNullOrEmpty(col.EnabledByValue)
                                        && !string.IsNullOrEmpty(triggerVal)
                                        && string.Equals(triggerVal.Trim(), col.EnabledByValue.Trim(),
                                            StringComparison.OrdinalIgnoreCase);
                                    if (!habilitada) { continue; }
                                }
                            }

                            string? defaultParaCelda = null;
                            if (n.SeedRowCellOptions is not null
                                && n.SeedRowCellOptions.TryGetValue($"{i}_{j}", out var rowOpts)
                                && rowOpts is { Count: > 0 })
                            {
                                defaultParaCelda = rowOpts[0];
                            }
                            else if (!string.IsNullOrEmpty(col.DefaultValue))
                            {
                                defaultParaCelda = col.DefaultValue;
                            }
                            if (string.IsNullOrEmpty(defaultParaCelda)) { continue; }

                            var cellKey = $"tbl:{n.Id}:{i}:{col.Id}";
                            if (!valores.ContainsKey(cellKey))
                            {
                                valores[cellKey] = defaultParaCelda;
                                cambio = true;
                            }
                        }
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrEmpty(n.DefaultValue)) { continue; }
                if (!valores.ContainsKey(n.Name!))
                {
                    valores[n.Name!] = n.DefaultValue;
                    cambio = true;
                }
            }
        }
    }
}

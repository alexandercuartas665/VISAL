using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Recorre el schema de un FormDefinition y aplica el <c>DefaultValue</c> declarado
/// en cada FormNode al diccionario de valores de la HC/escala/evolucion/consentimiento.
///
/// Se invoca ANTES de PacientePrefillHelper.Aplicar para que el prefill del paciente
/// pueda sobreescribir el default cuando aplique. Es decir, la cadena de prioridad
/// al iniciar una HC nueva es:
///   1) DefaultValue del schema (este helper)
///   2) Prefill paciente (PacientePrefillHelper)
///   3) Prefill historia medica acumulada (HistoriaMedicaPrefillHelper)
///   4) Lo que el doctor escriba a mano en el FormViewer
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
                    //      Esto cubre tablas tipo Examen Fisico donde cada fila tiene
                    //      sus propias opciones (la global "NO REFIERE" no aplica para
                    //      Cabeza, Cardiovascular, etc.; cada fila quiere su propio texto).
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

                            // Si la columna depende de un trigger y el trigger no
                            // satisface en esta fila, NO escribimos default. La celda
                            // arranca vacia hasta que el doctor active la fila.
                            // Asumimos que el trigger esta en una columna anterior en
                            // el orden de Columns (ya rellenada en iteraciones previas
                            // de este loop), tal cual el JSON declara.
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

                // Campos individuales (no-table, no-text)
                if (string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrEmpty(n.DefaultValue)) { continue; }
                if (!valores.TryGetValue(n.Name!, out var existingField) || string.IsNullOrEmpty(existingField))
                {
                    valores[n.Name!] = n.DefaultValue;
                }
            }
        }
    }
}

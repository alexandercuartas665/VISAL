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
                    // (las que estan vacias en seedRow[i][j]) con el DefaultValue
                    // de su columna. Las celdas con valor fijo (seed) no se tocan.
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
                            if (string.IsNullOrEmpty(col.DefaultValue)) { continue; }

                            var cellKey = $"tbl:{n.Id}:{i}:{col.Id}";
                            if (!valores.TryGetValue(cellKey, out var existing) || string.IsNullOrEmpty(existing))
                            {
                                valores[cellKey] = col.DefaultValue;
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

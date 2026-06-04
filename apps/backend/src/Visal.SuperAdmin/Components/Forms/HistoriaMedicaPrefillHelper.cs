using Visal.Application.Tenancy;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Aplica las rutas de prefill cuyo sourceModule = "historiaMedica". A diferencia
/// de PacientePrefillHelper (datos estaticos del paciente), aqui las fuentes son
/// DERIVADAS de la instancia actual de la HC: orden de medicamentos, ordenes de
/// servicio, escalas, etc.
///
/// Se evalua en tiempo real cada vez que el dato derivado cambia (el doctor
/// agrega/quita un medicamento desde el tab "Ordenes medicamento", por ejemplo),
/// y tambien al abrir la HC y antes de cada autosave para evitar drift entre la
/// fuente y lo persistido en ValoresJson.
///
/// Los campos rellenados desde una fuente derivada se marcan como readonly en el
/// FormViewer (badge "auto") para que el doctor no los edite a mano.
/// </summary>
public static class HistoriaMedicaPrefillHelper
{
    /// <summary>Catalogo de campos disponibles bajo sourceModule = "historiaMedica".</summary>
    public static readonly string[] CamposDisponibles = new[]
    {
        "medicamentos.lista_numerada"
        // A futuro: "ordenes_servicio.lista_numerada", "escalas.barthel.puntaje", etc.
    };

    /// <summary>
    /// Construye el diccionario de valores derivados a partir de los datos actuales
    /// de la HC. Las claves coinciden con CamposDisponibles.
    /// </summary>
    public static Dictionary<string, string?> BuildValores(IReadOnlyList<OrdenMedicamentoItemDto> medicamentos)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["medicamentos.lista_numerada"] = ListaNumeradaMedicamentos(medicamentos)
        };
    }

    /// <summary>
    /// Produce un texto numerado para una lista de medicamentos. Formato:
    /// "1. Acetaminofen 500mg - c/8h - 30 - Tomar con comida\n2. Ibuprofeno ...".
    /// Las columnas vacias se omiten para no inflar el texto.
    /// </summary>
    public static string ListaNumeradaMedicamentos(IReadOnlyList<OrdenMedicamentoItemDto> items)
    {
        if (items is null || items.Count == 0) { return ""; }
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var m in items.OrderBy(x => x.Orden))
        {
            sb.Append(i++).Append(". ").Append(m.NombreMedicamento);
            // Posologia: viene como un campo libre, o se arma de cantidad/frecuencia/dias.
            var posologia = !string.IsNullOrWhiteSpace(m.Posologia)
                ? m.Posologia!
                : string.Join(" - ", new[] { m.Cantidad, m.Frecuencia, m.Dias }.Where(x => !string.IsNullOrWhiteSpace(x))!);
            if (!string.IsNullOrWhiteSpace(posologia)) { sb.Append(" - ").Append(posologia); }
            if (!string.IsNullOrWhiteSpace(m.Observacion)) { sb.Append(" - ").Append(m.Observacion); }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Aplica los mapeos de la ruta sourceModule = "historiaMedica" al diccionario
    /// de valores del formulario. Devuelve el conjunto de targets que fueron
    /// poblados, para que el caller los marque como readonly en el FormViewer.
    /// </summary>
    public static HashSet<string> Aplicar(
        Dictionary<string, string?> valores,
        IReadOnlyList<OrdenMedicamentoItemDto> medicamentos,
        PrefillRouteSet rutas)
    {
        var readOnlyTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, "historiaMedica", StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return readOnlyTargets; }

        var fuente = BuildValores(medicamentos);
        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Source) || string.IsNullOrWhiteSpace(m.Target)) { continue; }
            if (fuente.TryGetValue(m.Source, out var v))
            {
                valores[m.Target] = v;
                readOnlyTargets.Add(m.Target);
            }
        }
        return readOnlyTargets;
    }
}

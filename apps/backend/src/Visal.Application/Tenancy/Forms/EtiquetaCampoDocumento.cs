namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Resuelve en RENDER la etiqueta (caption) de un campo de formulario. Caso especial: el
/// campo del "documento del paciente" en los formatos tiene la clave fija <c>cc</c> y una
/// etiqueta estatica "CC" en el esquema, aunque el paciente no sea CC (puede ser RC/TI/CE/
/// PT...). El numero vive bajo la clave <c>cc</c> en el <c>valores_json</c> de cada HC y NO
/// se debe renombrar (miles de HC ya lo tienen). Aqui solo se compone la ETIQUETA en vivo con
/// el tipo de documento real del paciente, sin tocar datos ni claves; asi la impresion sale
/// con el tipo correcto tanto en HC nuevas como en las ~historicas (la etiqueta se resuelve al
/// imprimir, no esta congelada). Fallback: la etiqueta del esquema cuando no se conoce el tipo.
/// </summary>
public static class EtiquetaCampoDocumento
{
    /// <summary>Clave fija del campo "documento del paciente" en los formatos (prefill
    /// numeroDocumento -&gt; "cc").</summary>
    public const string ClaveDocumento = "cc";

    /// <summary>
    /// Etiqueta a mostrar para el nodo <paramref name="nombreCampo"/> con etiqueta de esquema
    /// <paramref name="etiquetaEsquema"/>. Si es el campo del documento (clave <c>cc</c>) y se
    /// conoce el <paramref name="tipoDocumentoPaciente"/>, devuelve ese tipo (ej. "RC"); si no,
    /// devuelve la etiqueta del esquema sin cambios.
    /// </summary>
    public static string Resolver(string? nombreCampo, string? etiquetaEsquema, string? tipoDocumentoPaciente)
    {
        if (string.Equals(nombreCampo, ClaveDocumento, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(tipoDocumentoPaciente))
        {
            return tipoDocumentoPaciente.Trim();
        }
        return etiquetaEsquema ?? "";
    }
}

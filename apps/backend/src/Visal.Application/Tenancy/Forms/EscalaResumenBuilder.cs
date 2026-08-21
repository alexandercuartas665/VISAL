using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Arma el "resumen automatico" de una escala clinica que se vuelca al campo
/// analisis de la HC cuando la escala se CIERRA (ver EscalaService.CerrarAsync
/// y el origen de prefill "escalas.resumen").
///
/// Regla del resumen (confirmada con el usuario):
///  - Encabezado: <c>[NOMBRE ESCALA - dd/MM/yyyy]</c>.
///  - Cuerpo: SOLO los campos de la seccion cuyo titulo contenga "RESULTADO"
///    (comparacion insensible a mayusculas y acentos). Si ninguna seccion se
///    llama asi, se usa la ULTIMA seccion con campos. Cada campo con valor sale
///    como <c>Etiqueta: valor</c>; los vacios se omiten.
///  - Se ignoran tablas, QR y bloques de texto.
///
/// Si el cuerpo queda vacio (ninguna respuesta en la seccion resultado), devuelve
/// cadena vacia: el caller no agrega nada.
/// </summary>
public static class EscalaResumenBuilder
{
    public static string Construir(string? schemaJson, string? valoresJson, string nombreEscala, DateTimeOffset fecha)
    {
        var schema = FormSchema.FromJson(schemaJson);
        var valores = ParseValores(valoresJson);
        if (valores.Count == 0) { return string.Empty; }

        var seccion = ElegirSeccionResultado(schema);
        var campos = (seccion?.Children ?? schema.Children)
            .Where(n => n is { Type: "field" } && !n.IsTable && !n.IsQr && !string.IsNullOrWhiteSpace(n.Name));

        var lineas = new List<string>();
        foreach (var campo in campos)
        {
            if (!valores.TryGetValue(campo.Name!, out var valor)) { continue; }
            if (string.IsNullOrWhiteSpace(valor)) { continue; }
            var etiqueta = string.IsNullOrWhiteSpace(campo.Label) ? campo.Name! : campo.Label!.Trim();
            lineas.Add($"{etiqueta}: {valor.Trim()}");
        }

        if (lineas.Count == 0) { return string.Empty; }

        var titulo = string.IsNullOrWhiteSpace(nombreEscala) ? "ESCALA" : nombreEscala.Trim();
        var fechaTxt = fecha.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append('[').Append(titulo).Append(" - ").Append(fechaTxt).Append(']').Append('\n');
        sb.Append(string.Join('\n', lineas));
        return sb.ToString();
    }

    /// <summary>Devuelve la seccion "RESULTADO" (por titulo) o, si no existe, la
    /// ultima seccion con al menos un campo. null si el schema no tiene secciones.</summary>
    private static FormNode? ElegirSeccionResultado(FormSchema schema)
    {
        var secciones = schema.Children.Where(n => n.IsSection && n.Children is { Count: > 0 }).ToList();
        if (secciones.Count == 0) { return null; }

        var porTitulo = secciones.FirstOrDefault(s => Normalizar(s.Label).Contains("resultado"));
        return porTitulo ?? secciones[^1];
    }

    private static Dictionary<string, string?> ParseValores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Minusculas sin acentos, para comparar titulos de seccion.</summary>
    private static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return string.Empty; }
        var norm = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }
}

namespace Visal.Application.Common;

/// <summary>
/// Ayudas para el codigo de servicio del contrato (ServicioContrato.CodigoServicio).
///
/// Los codigos VISAL traen un sufijo de modalidad de una sola letra al final:
///   - <c>d</c> = Dentro (intramural)  ->  ej. "E891865d"
///   - <c>f</c> = Fuera  (extramural)  ->  ej. "E891865f"
///
/// RIPS/CUPS NO maneja ese sufijo, por lo que el codigo que viaja a facturacion
/// debe ir "base" (sin la letra). La UI, en cambio, decodifica la letra a una
/// etiqueta legible (Dentro/Fuera) para que el usuario la vea.
///
/// El sufijo solo se reconoce cuando la ultima letra es d/f Y el caracter previo
/// es un digito, para no mutilar codigos que legitimamente terminen en esas
/// letras (ej. "PKGD" no se toca).
/// </summary>
public static class ServicioCodigo
{
    /// <summary>Devuelve el codigo sin el sufijo de modalidad d/f. Si no hay sufijo
    /// reconocible (o el codigo es null/vacio) devuelve el codigo tal cual.</summary>
    public static string? Base(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) { return codigo; }
        var c = codigo.Trim();
        if (TieneSufijo(c)) { return c[..^1]; }
        return c;
    }

    /// <summary>"Dentro" / "Fuera" segun el sufijo, o null si el codigo no lo trae.</summary>
    public static string? ModalidadLabel(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) { return null; }
        var c = codigo.Trim();
        if (!TieneSufijo(c)) { return null; }
        return char.ToLowerInvariant(c[^1]) == 'd' ? "Dentro" : "Fuera";
    }

    /// <summary>Texto para mostrar en selectores: el codigo base + la modalidad
    /// entre parentesis cuando aplica (ej. "E891865 (Dentro)"). Si no hay sufijo,
    /// devuelve el codigo base a secas.</summary>
    public static string Mostrar(string? codigo)
    {
        var b = Base(codigo) ?? "";
        var m = ModalidadLabel(codigo);
        return m is null ? b : $"{b} ({m})";
    }

    private static bool TieneSufijo(string c)
    {
        if (c.Length < 2) { return false; }
        var last = char.ToLowerInvariant(c[^1]);
        return (last == 'd' || last == 'f') && char.IsDigit(c[^2]);
    }
}

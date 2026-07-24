namespace Visal.Application.Common;

/// <summary>
/// Utilidades para el campo Paciente.Telefono, que puede contener MULTIPLES
/// telefonos separados. Los consumidores que necesitan UN solo numero (WhatsApp,
/// firma remota) llaman Principal para obtener el primero.
///
/// Separadores aceptados: ";", ",", "\n", "-", "/", "|". Los operadores en prod
/// escriben con cualquiera de estos ("311 2887609 - 3132539732 - ..." se ve
/// mucho); si Principal no los reconoce, el string entero pasa como telefono
/// y otros campos varchar(40) revientan al persistir (fijado 2026-07-24 tras
/// crash al iniciar HC para paciente con 4 numeros separados por " - ").
/// </summary>
public static class PacienteTelefonoHelper
{
    private static readonly char[] _sep = new[] { ';', ',', '\n', '-', '/', '|' };

    /// <summary>Devuelve el primer telefono de la lista almacenada o null si esta vacia.</summary>
    public static string? Principal(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) { return null; }
        var partes = telefono.Split(_sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return partes.Length > 0 ? partes[0] : null;
    }

    /// <summary>Enumera todos los telefonos almacenados, trimmed y sin vacios.</summary>
    public static IReadOnlyList<string> Enumerar(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) { return Array.Empty<string>(); }
        return telefono.Split(_sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Empaqueta una lista de telefonos al formato de almacenamiento ("t1; t2; t3").</summary>
    public static string? Empaquetar(IEnumerable<string?>? telefonos)
    {
        if (telefonos is null) { return null; }
        var limpios = telefonos
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return limpios.Count == 0 ? null : string.Join("; ", limpios);
    }
}

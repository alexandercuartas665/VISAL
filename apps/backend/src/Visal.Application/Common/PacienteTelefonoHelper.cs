namespace Visal.Application.Common;

/// <summary>
/// Utilidades para el campo Paciente.Telefono, que puede contener MULTIPLES
/// telefonos separados por "; " o ",". Los consumidores que necesitan UN solo
/// numero (WhatsApp, firma remota) llaman Principal para obtener el primero.
/// </summary>
public static class PacienteTelefonoHelper
{
    private static readonly char[] _sep = new[] { ';', ',', '\n' };

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

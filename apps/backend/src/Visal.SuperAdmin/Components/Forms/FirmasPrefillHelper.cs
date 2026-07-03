using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Aplica las rutas de prefill "firmaPaciente" y "firmaProfesional" de un
/// FormDefinition. Las URLs ya vienen resueltas por IFirmaResolverService desde
/// el caller (no usamos DbContext aqui para mantener el helper estatico y
/// alineado al estilo de PacientePrefillHelper).
///
/// Patron de uso desde HistoriasClinicasModulo / HcEscalas / HcDocumentos:
/// <code>
///   var firmaPac = await FirmaSvc.ResolverFirmaPacienteAsync(pacienteId);
///   var firmaProf = await FirmaSvc.ResolverFirmaProfesionalAsync(tenantUserId);
///   FirmasPrefillHelper.Aplicar(valores, firmaPac, firmaProf, rutas);
/// </code>
/// </summary>
public static class FirmasPrefillHelper
{
    /// <summary>Aplica al diccionario los targets de los mappings cuyas rutas
    /// tengan sourceModule = "firmaPaciente" o "firmaProfesional". Solo escribe
    /// si la URL respectiva no es null/vacia.</summary>
    public static void Aplicar(
        Dictionary<string, string?> valores,
        string? firmaPacienteUrl,
        string? firmaProfesionalUrl,
        PrefillRouteSet rutas)
    {
        AplicarRuta(valores, rutas, "firmaPaciente", firmaPacienteUrl);
        AplicarRuta(valores, rutas, "firmaProfesional", firmaProfesionalUrl);
    }

    /// <summary>Aplica los targets de la ruta firmaAcompananteN (N = 1..4). Cada
    /// mapeo puede tener source = "url" (data URL de la firma), "nombre" (nombre
    /// del contacto) o "parentesco" (relacion con el paciente). Solo escribe los
    /// que no sean null.</summary>
    public static void AplicarAcompanante(
        Dictionary<string, string?> valores,
        int indice1Based,
        string? url,
        string? nombre,
        string? parentesco,
        PrefillRouteSet rutas)
    {
        var sourceModule = "firmaAcompanante" + indice1Based;
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, sourceModule, StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return; }
        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Target)) { continue; }
            string? valor = m.Source switch
            {
                "url" => url,
                "nombre" => nombre,
                "parentesco" => parentesco,
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(valor)) { valores[m.Target] = valor; }
        }
    }

    private static void AplicarRuta(
        Dictionary<string, string?> valores,
        PrefillRouteSet rutas,
        string sourceModule,
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { return; }
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, sourceModule, StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return; }
        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Target)) { continue; }
            // El unico source que existe para estas rutas es "url" (definido en
            // PrefillSourceCatalog). Cualquier otro source se ignora silenciosamente.
            if (!string.Equals(m.Source, "url", StringComparison.OrdinalIgnoreCase)) { continue; }
            valores[m.Target] = url;
        }
    }
}

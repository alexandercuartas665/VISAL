using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Aplica las rutas de prefill cuyo sourceModule = "profesional". Consume
/// el mismo <see cref="PrefillProfesionalDatosDto"/> que resuelve
/// FirmaResolverService para el profesional vinculado al usuario logueado.
///
/// Existe ademas de <see cref="SistemaPrefillHelper"/> (que tambien lleva
/// datos del profesional bajo keys "usuarioX") porque el catalogo publico
/// (<see cref="PrefillRoutes.Campos"/>) expone la fuente "profesional" con
/// nombres nativos (nombreCompleto, numeroDocumento, registroMedico...) y
/// hay formularios historicos configurados asi. Sin este helper esas rutas
/// quedaban huerfanas: aparecian en el dropdown pero nunca se aplicaban al
/// iniciar la HC.
/// </summary>
public static class ProfesionalPrefillHelper
{
    /// <summary>Diccionario fuente: claves = keys expuestas en
    /// <c>PrefillRoutes.Campos["profesional"]</c>. Devuelve string? para que
    /// el nulo/vacio no sobreescriba el valor destino.</summary>
    public static Dictionary<string, string?> Valores(PrefillProfesionalDatosDto? datos)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["nombreCompleto"] = datos?.Nombre,
            ["numeroDocumento"] = datos?.Identificacion,
            ["registroMedico"] = datos?.RegistroMedico,
            ["ciudad"] = datos?.Ciudad,
            ["celular"] = datos?.Celular,
            ["tipoProfesional"] = datos?.TipoProfesional
        };
    }

    /// <summary>Aplica los mapeos de la ruta sourceModule = "profesional" al
    /// diccionario de valores del formulario. Si no hay rutas configuradas o
    /// no se logro resolver el profesional, no hace nada — el resto del
    /// prefill (paciente, sistema, firmas) sigue funcionando.</summary>
    public static void Aplicar(
        Dictionary<string, string?> valores,
        PrefillProfesionalDatosDto? datos,
        PrefillRouteSet rutas)
    {
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, "profesional", StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return; }
        var fuente = Valores(datos);
        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Source) || string.IsNullOrWhiteSpace(m.Target)) { continue; }
            if (fuente.TryGetValue(m.Source, out var v) && v is not null)
            {
                valores[m.Target] = v;
            }
        }
    }
}

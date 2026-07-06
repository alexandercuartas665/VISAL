using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Aplica las rutas de prefill "paciente" de un FormDefinition para llenar valores
/// iniciales cuando se inicia una HC, escala, evolucion o consentimiento.
///
/// La forma "configurada" lee la ruta cuyo sourceModule = "paciente" y mapea cada
/// (source, target) usando el diccionario de campos del paciente. La forma
/// "heuristica" (fallback) intenta matchear por el Name/Label del campo cuando el
/// formulario aun no tiene rutas configuradas.
///
/// Centralizar aqui evita duplicar la logica entre HistoriasClinicasModulo,
/// HcEscalas y HcDocumentos.
/// </summary>
public static class PacientePrefillHelper
{
    /// <summary>
    /// Devuelve el diccionario de valores del paciente que estan disponibles como
    /// fuente para prefill. Las claves coinciden con PrefillSourceCatalog.Campos["paciente"].
    /// "edad" se calcula a partir de FechaNacimiento.
    /// </summary>
    public static Dictionary<string, string?> ValoresPaciente(PacienteAsignacionDto p)
    {
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["numeroDocumento"] = p.NumeroDocumento,
            ["tipoDocumento"] = p.TipoDocumento,
            ["nombreCompleto"] = p.NombreCompleto,
            ["primerNombre"] = p.PrimerNombre,
            ["segundoNombre"] = p.SegundoNombre,
            ["primerApellido"] = p.PrimerApellido,
            ["segundoApellido"] = p.SegundoApellido,
            ["fechaNacimiento"] = p.FechaNacimiento?.ToString("yyyy-MM-dd"),
            ["edad"] = CalcularEdad(p.FechaNacimiento),
            ["sexo"] = p.Sexo,
            ["estadoCivil"] = p.EstadoCivil,
            // Prefill: mostrar el primer telefono (el campo p.Telefono puede
            // contener varios separados por "; ").
            ["telefono"] = Visal.Application.Common.PacienteTelefonoHelper.Principal(p.Telefono),
            ["email"] = p.Email,
            ["direccion"] = p.Direccion,
            ["ciudad"] = p.Ciudad,
            ["zona"] = p.Zona,
            ["ocupacion"] = p.Ocupacion,
            ["regimen"] = p.Regimen,
            ["contactoEmergencia"] = p.ContactoEmergencia,
            ["parentesco"] = p.Parentesco,
            ["telefonoEmergencia"] = p.TelefonoEmergencia,
            ["sede"] = p.Sede,
            ["eps"] = p.Eps
        };
    }

    /// <summary>
    /// Calcula la edad en anos cumplidos a la fecha actual. Devuelve null si la
    /// fecha de nacimiento no esta disponible.
    /// </summary>
    public static string? CalcularEdad(DateOnly? fechaNacimiento)
    {
        if (fechaNacimiento is not DateOnly fn) { return null; }
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var edad = hoy.Year - fn.Year;
        if (fn > hoy.AddYears(-edad)) { edad--; }
        return edad < 0 ? null : edad.ToString();
    }

    /// <summary>
    /// Aplica el prefill al diccionario de valores del formulario. Si el formulario
    /// tiene una ruta paciente configurada, usa esos mapeos; si no, intenta una
    /// heuristica basica por nombre/etiqueta de cada campo (el mismo criterio que
    /// se usaba antes de tener rutas).
    /// </summary>
    /// <param name="valores">Diccionario destino. Se modifica en sitio.</param>
    /// <param name="paciente">Datos del paciente activo.</param>
    /// <param name="schema">Schema del formulario destino (para fallback heuristico).</param>
    /// <param name="rutas">Rutas de prefill configuradas en el FormDefinition.</param>
    public static void Aplicar(
        Dictionary<string, string?> valores,
        PacienteAsignacionDto paciente,
        FormSchema? schema,
        PrefillRouteSet rutas)
    {
        var pacienteValues = ValoresPaciente(paciente);

        // 1) Modo configurado: usar la ruta sourceModule = "paciente".
        var rutaPaciente = rutas.Routes
            .FirstOrDefault(r => string.Equals(r.SourceModule, "paciente", StringComparison.OrdinalIgnoreCase));
        if (rutaPaciente is not null && rutaPaciente.Mappings.Count > 0)
        {
            foreach (var m in rutaPaciente.Mappings)
            {
                if (string.IsNullOrWhiteSpace(m.Source) || string.IsNullOrWhiteSpace(m.Target)) { continue; }
                if (pacienteValues.TryGetValue(m.Source, out var v) && v is not null)
                {
                    valores[m.Target] = v;
                }
            }
            return;
        }

        // 2) Fallback heuristico: matching por convencion de nombre.
        if (schema is null) { return; }
        var heuristic = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documento"] = paciente.NumeroDocumento,
            ["identificacion"] = paciente.NumeroDocumento,
            ["nombre"] = paciente.NombreCompleto,
            ["nombres_apellidos"] = paciente.NombreCompleto,
            ["edad"] = CalcularEdad(paciente.FechaNacimiento),
            ["sede"] = paciente.Sede,
            ["ciudad"] = paciente.Ciudad,
            ["eps"] = paciente.Eps,
            ["aseguradora"] = paciente.Eps
        };
        Recurse(schema.Children);

        void Recurse(IEnumerable<FormNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsSection && n.Children is not null) { Recurse(n.Children); continue; }
                if (n.IsText || n.IsTable) { continue; }
                var name = (n.Name ?? n.Label ?? "").ToLowerInvariant().Replace(" ", "_");
                if (string.IsNullOrEmpty(name)) { continue; }
                foreach (var kv in heuristic)
                {
                    if (name.Contains(kv.Key.ToLowerInvariant()))
                    {
                        var key = n.Name ?? $"fld:{n.Id}";
                        valores[key] = kv.Value;
                        break;
                    }
                }
            }
        }
    }
}

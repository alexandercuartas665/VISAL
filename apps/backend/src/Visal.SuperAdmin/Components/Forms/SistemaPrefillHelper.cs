using Visal.Application.Tenancy.Forms;

namespace Visal.SuperAdmin.Components.Forms;

/// <summary>
/// Aplica las rutas de prefill cuyo sourceModule = "sistema". Cubre fechas /
/// horas del momento de creacion, datos del tenant (agencia) y de la sede +
/// usuario logueado. Util en escalas/evoluciones donde el formulario suele
/// pedir fecha y hora de aplicacion sin que el doctor las escriba a mano.
///
/// El contexto (SistemaCtx) lo arma quien invoca el helper porque solo el
/// caller tiene acceso a auth state, tenant branding y sede activa.
/// </summary>
public static class SistemaPrefillHelper
{
    /// <summary>Contexto del momento + tenant + sede + usuario que se aplica
    /// a las rutas de prefill "sistema".</summary>
    public sealed record SistemaCtx(
        DateTimeOffset Ahora,
        string? AgenciaNombre,
        string? AgenciaSlogan,
        string? SedeNombre,
        string? SedeCiudad,
        string? UsuarioNombre,
        string? UsuarioEmail,
        // Datos del profesional vinculado al usuario logueado. Todos opcionales:
        // si el usuario no esta ligado a un profesional (o el profesional no tiene
        // el dato) quedan en null y el prefill no escribe la celda.
        string? UsuarioIdentificacion = null,
        string? UsuarioRegistroMedico = null,
        string? UsuarioFirmaUrl = null);

    /// <summary>Diccionario fuente con todos los valores disponibles bajo
    /// sourceModule = "sistema". Las claves coinciden con
    /// PrefillSourceCatalog.Campos["sistema"]. Fechas en formato ISO porque
    /// los inputs date del FormViewer las requieren asi.</summary>
    public static Dictionary<string, string?> ValoresSistema(SistemaCtx ctx)
    {
        var ahoraLocal = ctx.Ahora.ToLocalTime();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            // Fecha del momento — formatos para distintos tipos de campo.
            ["fechaActual"] = ahoraLocal.ToString("yyyy-MM-dd"),
            ["fechaCorta"] = ahoraLocal.ToString("dd/MM/yyyy"),
            ["fechaLarga"] = ahoraLocal.ToString("dd 'de' MMMM 'de' yyyy",
                System.Globalization.CultureInfo.GetCultureInfo("es-CO")),
            // Hora.
            ["horaActual"] = ahoraLocal.ToString("HH:mm"),
            ["horaActualLarga"] = ahoraLocal.ToString("HH:mm:ss"),
            // Fecha + hora combinadas.
            ["fechaHoraActual"] = ahoraLocal.ToString("yyyy-MM-ddTHH:mm"),
            // Tenant / agencia.
            ["agencia"] = ctx.AgenciaNombre,
            ["agenciaNombre"] = ctx.AgenciaNombre,
            ["agenciaSlogan"] = ctx.AgenciaSlogan,
            // Sede activa del usuario logueado.
            ["sede"] = ctx.SedeNombre,
            ["sedeNombre"] = ctx.SedeNombre,
            ["sedeCiudad"] = ctx.SedeCiudad,
            // Usuario logueado.
            ["usuario"] = ctx.UsuarioNombre,
            ["usuarioNombre"] = ctx.UsuarioNombre,
            ["usuarioEmail"] = ctx.UsuarioEmail,
            // Datos del profesional vinculado (identificacion, registro medico,
            // firma). El source expuesto en el catalogo es "usuarioX"; se
            // aceptan alias sin el prefijo por ergonomia del usuario admin.
            ["usuarioIdentificacion"] = ctx.UsuarioIdentificacion,
            ["identificacion"] = ctx.UsuarioIdentificacion,
            ["usuarioRegistroMedico"] = ctx.UsuarioRegistroMedico,
            ["registroMedico"] = ctx.UsuarioRegistroMedico,
            ["usuarioFirma"] = ctx.UsuarioFirmaUrl,
            ["firma"] = ctx.UsuarioFirmaUrl
        };
    }

    /// <summary>Aplica los mapeos de la ruta sourceModule = "sistema" al
    /// diccionario de valores del formulario. Si no hay rutas configuradas,
    /// no hace nada (no usa heuristica — el sistema no infiere fechas porque
    /// es ruido en mas del 50% de los campos del HC).</summary>
    public static void Aplicar(
        Dictionary<string, string?> valores,
        SistemaCtx ctx,
        PrefillRouteSet rutas)
    {
        var ruta = rutas.Routes.FirstOrDefault(r =>
            string.Equals(r.SourceModule, "sistema", StringComparison.OrdinalIgnoreCase));
        if (ruta is null || ruta.Mappings.Count == 0) { return; }
        var sistemaValues = ValoresSistema(ctx);
        foreach (var m in ruta.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.Source) || string.IsNullOrWhiteSpace(m.Target)) { continue; }
            if (sistemaValues.TryGetValue(m.Source, out var v) && v is not null)
            {
                valores[m.Target] = v;
            }
        }
    }
}

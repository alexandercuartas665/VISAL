namespace Visal.SuperAdmin;

/// <summary>
/// Version semantica visible de la aplicacion (la que se muestra en la barra
/// lateral como "vX.Y.Z"). Se incrementa a mano en cada release/deploy:
///   - PATCH (0.1.0 -> 0.1.1): correcciones y ajustes menores.
///   - MINOR (0.1.0 -> 0.2.0): funcionalidades nuevas.
///   - MAJOR (0.x -> 1.0.0): cambios grandes / hito.
/// El git SHA y la fecha del build siguen en el tooltip del chip para
/// diagnostico exacto; esta constante es solo el numero legible para el usuario.
/// </summary>
public static class AppVersion
{
    public const string Current = "0.2.6";
}

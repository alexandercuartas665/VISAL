namespace Visal.SuperAdmin;

/// <summary>
/// Formatea los mensajes de error de la UI con un CODIGO trazable
/// (modulo + consecutivo) para que un operador pueda "ficharlos" rapido al
/// reportarlos. Ademas, para errores tecnicos desenreda la excepcion hasta su
/// causa raiz — clave con <c>DbUpdateException</c>, cuyo <c>Message</c> es el
/// generico "An error occurred while saving the entity changes. See the inner
/// exception for details." y esconde el error real de Postgres — y traduce los
/// SQLSTATE/errores comunes a un mensaje en espanol.
///
/// Formato de salida tecnico: "[MOD-NNN] mensaje amigable\nDetalle: causa raiz".
/// Las cajas de error usan white-space:pre-wrap, asi el salto de linea se ve.
///
/// Convencion de codigos: NNN &lt; 100 y 100-899 = reglas de negocio/validacion;
/// 900-999 = errores tecnicos (BD, IO, etc.). El prefijo MOD identifica el
/// modulo (p.ej. ASG = Asignacion).
/// </summary>
public static class UiError
{
    /// <summary>Error tecnico (excepcion no controlada). Expone la causa raiz.</summary>
    public static string Tecnico(string modulo, int codigo, Exception ex)
    {
        var raiz = Raiz(ex);
        var raw = string.IsNullOrWhiteSpace(raiz.Message) ? (ex.Message ?? "") : raiz.Message;
        var amigable = Traducir(raw);
        return $"[{modulo}-{codigo:000}] {amigable}\nDetalle: {raw}";
    }

    /// <summary>Error de negocio/validacion con mensaje ya legible; solo antepone el codigo.</summary>
    public static string Negocio(string modulo, int codigo, string mensaje)
        => $"[{modulo}-{codigo:000}] {mensaje}";

    private static Exception Raiz(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null) { e = e.InnerException; }
        return e;
    }

    private static string Traducir(string raw)
    {
        bool Has(string s) => raw.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
        if (Has("value too long"))
            return "Un texto supera el largo maximo permitido para uno de los campos (posible descripcion de servicio muy larga).";
        if (Has("duplicate key") || Has("23505"))
            return "Ya existe un registro con esos mismos datos (clave duplicada).";
        if (Has("violates foreign key") || Has("23503"))
            return "Referencia invalida: falta un registro relacionado.";
        if (Has("null value in column") || Has("23502"))
            return "Falta un dato obligatorio.";
        if (Has("violates check constraint") || Has("23514"))
            return "Un dato no cumple una regla de validacion del sistema.";
        if (Has("could not connect") || Has("connection") && Has("refused"))
            return "No se pudo conectar con la base de datos. Reintenta en un momento.";
        return "Error tecnico al procesar la solicitud.";
    }
}

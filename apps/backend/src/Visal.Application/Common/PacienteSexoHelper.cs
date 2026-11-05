namespace Visal.Application.Common;

/// <summary>
/// Normaliza el campo Paciente.Sexo a la PALABRA COMPLETA en mayusculas
/// (MASCULINO / FEMENINO), que es lo que usa el select de Admision y lo que
/// esperan impresiones, formularios y prefill.
///
/// Historia: las importaciones por Excel guardaban el valor crudo de la columna
/// "Sexo" tal cual (ver Admision.razor import), por lo que la base quedo con una
/// mezcla de "M"/"F" (codigo de una letra) y la palabra completa. Se depuro la
/// data existente (M->MASCULINO, F->FEMENINO) y este helper evita que vuelva a
/// entrar el codigo corto por cualquier via de escritura (SaveAsync es el punto
/// unico: lo usan el formulario y la importacion).
///
/// Regla: solo convierte variantes RECONOCIDAS de hombre/mujer. Cualquier otro
/// texto (INDETERMINADO, o basura heredada de columnas corridas como "PASTO")
/// se deja intacto -adivinarlo seria inventar el dato-; solo se recorta.
/// Null/vacio pasa sin cambios.
/// </summary>
public static class PacienteSexoHelper
{
    /// <summary>
    /// Devuelve la palabra completa (MASCULINO/FEMENINO) para las variantes
    /// reconocidas; para cualquier otro valor devuelve el texto recortado sin
    /// alterarlo. Es idempotente: "MASCULINO" -> "MASCULINO".
    /// </summary>
    public static string? ANombreCompleto(string? sexo)
    {
        if (string.IsNullOrWhiteSpace(sexo)) { return sexo; }

        var v = sexo.Trim();
        var u = v.ToUpperInvariant();

        if (u == "M" || u.StartsWith("MASC") || u == "HOMBRE") { return "MASCULINO"; }
        if (u == "F" || u.StartsWith("FEM") || u == "MUJER") { return "FEMENINO"; }

        return v;
    }
}

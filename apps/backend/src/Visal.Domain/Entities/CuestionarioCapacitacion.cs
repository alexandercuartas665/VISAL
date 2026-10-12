using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Evaluacion de una capacitacion: cabecera (titulo + puntaje minimo) que agrupa
/// preguntas de opcion multiple. Se reusa como evaluacion final de un curso.
/// </summary>
public class CuestionarioCapacitacion : TenantEntity
{
    /// <summary>Clave de agrupacion/identificacion de la evaluacion (texto libre).</summary>
    public string Modulo { get; set; } = "CAPACITACION";
    public string Titulo { get; set; } = null!;
    public string? Descripcion { get; set; }

    /// <summary>Puntaje sobre 100 necesario para aprobar.</summary>
    public int PuntajeMinimo { get; set; } = 60;

    public bool Activo { get; set; } = true;

    public ICollection<CuestionarioPregunta> Preguntas { get; set; } = new List<CuestionarioPregunta>();
}

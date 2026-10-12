using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Curso de capacitacion del personal: se divide en modulos (episodios), cada
/// modulo tiene lecciones (video/imagen/pdf/texto) y el curso cierra con una
/// evaluacion calificada que reusa el <see cref="CuestionarioCapacitacion"/>.
/// </summary>
public class Curso : TenantEntity
{
    public string Titulo { get; set; } = null!;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>Evaluacion final. Reusa un cuestionario existente (nota y minimo).</summary>
    public Guid? CuestionarioId { get; set; }
    public CuestionarioCapacitacion? Cuestionario { get; set; }

    public ICollection<CursoModulo> Modulos { get; set; } = new List<CursoModulo>();
}

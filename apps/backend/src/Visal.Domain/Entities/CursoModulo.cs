using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Modulo (episodio) de un curso. Agrupa lecciones en orden.</summary>
public class CursoModulo : TenantEntity
{
    public Guid CursoId { get; set; }
    public Curso Curso { get; set; } = null!;

    public string Titulo { get; set; } = null!;
    public string? Descripcion { get; set; }
    public int Orden { get; set; }

    public ICollection<CursoLeccion> Lecciones { get; set; } = new List<CursoLeccion>();
}

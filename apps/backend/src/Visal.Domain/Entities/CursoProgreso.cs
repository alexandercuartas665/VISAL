using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Avance de un usuario (miembro del personal, por platform_user_id) en un curso:
/// cuando lo inicio, cuantos intentos de evaluacion lleva, su mejor nota y si ya
/// lo aprobo. Una fila por (tenant, curso, usuario).
/// </summary>
public class CursoProgreso : TenantEntity
{
    public Guid CursoId { get; set; }
    public Curso Curso { get; set; } = null!;

    /// <summary>Aprendiz = usuario del tenant (platform_user_id).</summary>
    public Guid UsuarioId { get; set; }

    public DateTimeOffset? FechaInicio { get; set; }
    public DateTimeOffset? FechaAprobacion { get; set; }

    public int Intentos { get; set; }
    public int MejorNota { get; set; }
    public bool Aprobado { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Un intento de evaluacion de un usuario (platform_user_id): puntaje obtenido,
/// si aprobo y las respuestas marcadas (jsonb). Historico para auditoria y para
/// calcular la mejor nota del <see cref="CursoProgreso"/>.
/// </summary>
public class CuestionarioIntento : TenantEntity
{
    public Guid CuestionarioId { get; set; }
    public CuestionarioCapacitacion Cuestionario { get; set; } = null!;

    /// <summary>Usuario que presento el intento (platform_user_id).</summary>
    public Guid UsuarioId { get; set; }

    public int Puntaje { get; set; }
    public bool Aprobado { get; set; }

    /// <summary>jsonb: indices marcados por pregunta, ej. [0,2,1].</summary>
    public string RespuestasJson { get; set; } = "[]";

    public DateTimeOffset FechaIntento { get; set; }
}

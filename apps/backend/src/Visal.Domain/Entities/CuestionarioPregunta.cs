using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Pregunta de opcion multiple de una evaluacion. Las opciones viajan en jsonb
/// como arreglo de textos y <see cref="IndiceCorrecto"/> apunta a la posicion
/// (base 0) de la respuesta correcta.
/// </summary>
public class CuestionarioPregunta : TenantEntity
{
    public Guid CuestionarioId { get; set; }
    public CuestionarioCapacitacion Cuestionario { get; set; } = null!;

    public int Orden { get; set; }
    public string Enunciado { get; set; } = null!;

    /// <summary>jsonb: ["opcion a", "opcion b", ...].</summary>
    public string OpcionesJson { get; set; } = "[]";

    public int IndiceCorrecto { get; set; }

    /// <summary>Se muestra despues de responder, para que la formacion ensene.</summary>
    public string? Retroalimentacion { get; set; }
}

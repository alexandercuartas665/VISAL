using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Departamento (estado/provincia) dentro de un pais. Global; siembra inicial: 32 dptos
/// de Colombia desde api-colombia.com. Referencia para depto del paciente.
/// </summary>
public class Departamento : BaseEntity
{
    public Guid PaisId { get; set; }
    public Pais? Pais { get; set; }

    /// <summary>ID externo de la fuente (api-colombia.com Department.id) para reconciliar municipios.</summary>
    public int? ExternalId { get; set; }
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
}

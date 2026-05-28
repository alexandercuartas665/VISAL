using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Municipio (ciudad) dentro de un departamento. Global; siembra inicial desde
/// api-colombia.com (~1100 municipios de Colombia). Referencia para municipio del paciente.
/// </summary>
public class Municipio : BaseEntity
{
    public Guid DepartamentoId { get; set; }
    public Departamento? Departamento { get; set; }

    public int? ExternalId { get; set; }
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
}

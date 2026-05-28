using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Catalogo de tipos/categorias de profesional (ENFERMERIA, MEDICO, TERAPEUTA...). Tenant-scoped.</summary>
public class TipoProfesional : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public bool Activo { get; set; } = true;
    public int Orden { get; set; }
}

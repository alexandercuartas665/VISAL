using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Profesional de la entidad (medico, terapeuta, enfermeria...). Tenant-scoped.</summary>
public class Profesional : TenantEntity
{
    public string NumeroDocumento { get; set; } = null!;
    public string TipoDocumento { get; set; } = "CC";
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string NombreCompleto { get; set; } = null!;

    public Guid? TipoProfesionalId { get; set; }
    public TipoProfesional? TipoProfesional { get; set; }

    public string? RegistroMedico { get; set; }
    public string? Ciudad { get; set; }
    public string? Celular { get; set; }
    public string? FirmaUrl { get; set; }
}

/// <summary>Subcategorias asignadas a un profesional (N a N). Tenant-scoped.</summary>
public class ProfesionalSubCategoria : TenantEntity
{
    public Guid ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }
    public Guid SubCategoriaId { get; set; }
    public SubCategoriaProfesional? SubCategoria { get; set; }
}

/// <summary>Agencias / sedes activas de un profesional. Tenant-scoped.</summary>
public class ProfesionalAgencia : TenantEntity
{
    public Guid ProfesionalId { get; set; }
    public Profesional? Profesional { get; set; }
    public string Agencia { get; set; } = null!;
}

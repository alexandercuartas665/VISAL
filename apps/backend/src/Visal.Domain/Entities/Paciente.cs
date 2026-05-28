using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Paciente de la IPS (admision). Tenant-scoped. Raiz del dominio asistencial.</summary>
public class Paciente : TenantEntity
{
    public string NumeroDocumento { get; set; } = null!;
    public string TipoDocumento { get; set; } = "CC";
    public string? PrimerNombre { get; set; }
    public string? SegundoNombre { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string NombreCompleto { get; set; } = null!;

    public DateOnly? FechaNacimiento { get; set; }
    public string? Sexo { get; set; }
    public string? EstadoCivil { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public string? Ciudad { get; set; }
    public string? Zona { get; set; }
    public string? Ocupacion { get; set; }
    public string? Regimen { get; set; }

    public Guid? AseguradoraId { get; set; }
    public Aseguradora? Aseguradora { get; set; }

    public string? ContactoEmergencia { get; set; }
    public string? Parentesco { get; set; }
    public string? TelefonoEmergencia { get; set; }

    public bool Activo { get; set; } = true;
}

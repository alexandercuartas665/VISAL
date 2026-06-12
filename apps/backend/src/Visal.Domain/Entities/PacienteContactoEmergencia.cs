using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>Un contacto de emergencia de un paciente. Un paciente puede tener
/// varios (madre, padre, conyuge, hermano, etc.). El telefono se guarda separado
/// del codigo de pais para que la UI pueda renderizar un selector independiente.</summary>
public class PacienteContactoEmergencia : TenantEntity
{
    public Guid PacienteId { get; set; }
    public Paciente? Paciente { get; set; }

    /// <summary>Nombre completo del contacto.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Parentesco con el paciente (madre, padre, hijo, conyuge, etc.).</summary>
    public string? Parentesco { get; set; }

    /// <summary>Codigo de pais con prefijo "+". Default "+57" (Colombia).</summary>
    public string CodigoPais { get; set; } = "+57";

    /// <summary>Telefono sin codigo de pais, solo digitos preferiblemente.</summary>
    public string? Telefono { get; set; }

    /// <summary>Orden de presentacion (1 = principal). Se usa para mantener el
    /// orden que el usuario escribio en la UI.</summary>
    public int Orden { get; set; }
}

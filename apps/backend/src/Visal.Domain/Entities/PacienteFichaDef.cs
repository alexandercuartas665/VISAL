using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Un TAB configurable de la ficha de admision (gestor de campos del tenant). Agrupa
/// campos dinamicos (<see cref="PacienteCampoDef"/>) por su <see cref="TabKey"/>.
/// Tenant-scoped: cada agencia define los suyos. Calcado de ECOREX TerceroFichaDefinition.
/// </summary>
public class PacienteFichaDef : TenantEntity
{
    /// <summary>Clave estable (slug) del tab; los campos la referencian por convencion.</summary>
    public string TabKey { get; set; } = null!;

    /// <summary>Nombre visible del tab (editable).</summary>
    public string Titulo { get; set; } = null!;

    public string? Descripcion { get; set; }

    /// <summary>Color de acento (hex), opcional.</summary>
    public string? Color { get; set; }

    /// <summary>Orden del tab en la barra.</summary>
    public int Orden { get; set; }

    /// <summary>Oculta el tab sin borrarlo.</summary>
    public bool Oculto { get; set; }

    /// <summary>Tab sembrado por la plataforma (no se puede eliminar).</summary>
    public bool EsSistema { get; set; }
}

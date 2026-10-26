using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Subcategoria (segundo nivel) de una <see cref="TipologiaArchivo"/>. Ej. la tipologia
/// "Examen" puede tener subcategorias "Laboratorio" / "Imagenologia". Cuelga de la
/// tipologia (se borra en cascada si se borra la tipologia). Tenant-scoped.
/// </summary>
public class TipologiaArchivoSubcategoria : TenantEntity
{
    /// <summary>Tipologia padre.</summary>
    public Guid TipologiaArchivoId { get; set; }
    public TipologiaArchivo? TipologiaArchivo { get; set; }

    /// <summary>Nombre visible de la subcategoria (ej. "Laboratorio").</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Si esta inactiva no aparece en los dropdowns, pero se conserva el
    /// historial de documentos que la usaron.</summary>
    public bool Activo { get; set; } = true;
}

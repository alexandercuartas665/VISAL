using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Etiqueta (label) que se puede asignar a tarjetas dentro de un tablero. Cada tablero
/// tiene su propia taxonomia (ej. "Diseno", "Urgente", "Marketing"). TENANT-SCOPED.
/// </summary>
public class TaskCardTag : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Color del badge en la UI (hex). Default a gris si nulo.</summary>
    public string? Color { get; set; }

    public int SortOrder { get; set; }
}

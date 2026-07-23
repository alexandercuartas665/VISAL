using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Tablero Kanban del tenant para gestionar tareas colaborativas. Cada agencia puede tener
/// varios tableros. El acceso NO es abierto al tenant: solo el creador (Owner) y los usuarios
/// invitados via <see cref="TaskBoardMember"/> pueden verlo/editarlo. Entidad TENANT-SCOPED.
/// </summary>
public class TaskBoard : TenantEntity
{
    /// <summary>Nombre visible del tablero (ej. "Operacion Q1").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Descripcion opcional para que los miembros entiendan el proposito del tablero.</summary>
    public string? Description { get; set; }

    /// <summary>Color del tablero en la lista (hex). Solo visual.</summary>
    public string? Color { get; set; }

    /// <summary>Orden de visualizacion en la lista de tableros del tenant.</summary>
    public int SortOrder { get; set; }

    /// <summary>Tableros archivados quedan ocultos de la lista por defecto pero conservan sus datos.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// PlatformUser que creo el tablero. Es dueno automatico y no puede ser
    /// removido de la lista de miembros (protegido en el service).
    /// Denormalizado tambien en CreatedBy (BaseEntity) pero lo dejamos explicito
    /// aqui para claridad de la regla de negocio.
    /// </summary>
    public Guid OwnerPlatformUserId { get; set; }
}

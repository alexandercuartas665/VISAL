using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Asignacion de un usuario a una tarjeta. Una tarjeta puede tener varios asignados
/// (miembros responsables de avanzarla). Solo se puede asignar usuarios que sean
/// miembros del tablero (owner o invitado). TENANT-SCOPED.
/// </summary>
public class TaskCardAssignment : TenantEntity
{
    public Guid TaskCardId { get; set; }
    public TaskCard? TaskCard { get; set; }

    /// <summary>PlatformUser asignado. Consistente con TaskBoardMember (PlatformUserId)
    /// para que la asignacion solo sea posible si es miembro del tablero.</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Snapshot del nombre al asignar — sirve para mostrar el chip aun si
    /// el usuario cambia de nombre o pierde acceso.</summary>
    public string DisplayName { get; set; } = null!;
}

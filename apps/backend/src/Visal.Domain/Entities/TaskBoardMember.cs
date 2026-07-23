using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Membresia de un usuario a un tablero. Sin fila en esta tabla (ni ser
/// OwnerPlatformUserId del tablero) el usuario NO ve el tablero, aunque sea
/// admin del tenant. Es la unidad de control de acceso del modulo. TENANT-SCOPED.
///
/// Se crea automaticamente para el creador al crear el tablero, y el service
/// impide borrarla (borrar al owner deja el tablero huerfano — se archiva
/// como flujo alternativo).
/// </summary>
public class TaskBoardMember : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    /// <summary>PlatformUser invitado. Usamos PlatformUser (no TenantUser)
    /// para que la referencia sobreviva si el TenantUser cambia de tenant.</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Snapshot del nombre al invitar — sobrevive si el usuario cambia
    /// de nombre en su perfil o pierde acceso al tenant.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>PlatformUser que envio la invitacion (para auditoria).</summary>
    public Guid InvitedByPlatformUserId { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Ola 9 RC9c — staging table del channel <c>PreRevisionIaQueue</c>. Guarda
/// cada job encolado antes de que el worker lo procese, para no perderlo si
/// el proceso se reinicia. Al arrancar, el worker relee esta tabla y reencola
/// todo lo pendiente.
///
/// NO es tenant-scoped: es infra interna del worker, se filtra manualmente al
/// leer. Guarda TenantId como columna para que el worker recree el ambient
/// tenant antes de ejecutar el orquestador.
///
/// Ciclo de vida: INSERT al encolar → DELETE cuando el worker termina el item
/// (exito o error final). Si el proceso muere entre INSERT y DELETE, la fila
/// queda y se reencola al proximo startup — el orquestador es idempotente.
/// </summary>
public class PreRevisionIaPending : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid RevisionClinicaId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
}

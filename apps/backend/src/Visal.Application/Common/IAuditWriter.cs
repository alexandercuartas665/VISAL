using Visal.Domain.Enums;

namespace Visal.Application.Common;

/// <summary>
/// Registra acciones sensibles del Super Admin/sistema en super_admin_audit_logs.
/// Solo agrega la entrada al contexto; el caso de uso decide cuando persistir (SaveChanges).
/// </summary>
public interface IAuditWriter
{
    void Write(
        Guid actorUserId,
        string actionName,
        string entityName,
        Guid? entityId,
        object? previousValue,
        object? newValue,
        Guid? tenantId = null,
        string? reason = null,
        AuditActorType actorType = AuditActorType.Human);

    /// <summary>
    /// Igual que <see cref="Write(Guid,string,string,Guid?,object?,object?,Guid?,string?,AuditActorType)"/>
    /// pero agrega la entrada al <paramref name="db"/> indicado en vez del contexto scoped propio.
    /// Lo usan los servicios migrados al contexto CORTO por operacion (factory, Ola 2/4): ahi la
    /// auditoria debe entrar en el MISMO contexto que persiste la operacion para que el
    /// SaveChanges de ese contexto la guarde de forma atomica. Con el Write clasico la entrada
    /// quedaria en el contexto scoped del circuito (otro contexto) y no se persistiria con la operacion.
    /// </summary>
    void Write(
        IApplicationDbContext db,
        Guid actorUserId,
        string actionName,
        string entityName,
        Guid? entityId,
        object? previousValue,
        object? newValue,
        Guid? tenantId = null,
        string? reason = null,
        AuditActorType actorType = AuditActorType.Human);
}

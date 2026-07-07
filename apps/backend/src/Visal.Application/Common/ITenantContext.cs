namespace Visal.Application.Common;

/// <summary>
/// Expone el tenant y usuario del contexto de ejecucion actual (request HTTP, worker, etc.).
/// Lo resuelve la capa de presentacion desde el claim tenant_id del JWT. En procesos sin
/// tenant (seed, workers globales) TenantId puede ser null y el filtro de consulta es fail-closed.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }

    /// <summary>
    /// Sucursal / sede que el usuario eligio al ingresar. Null si el tenant tiene una sola
    /// sede, si el usuario aun no la eligio, o si es super admin de plataforma.
    /// La usan las validaciones que dependen de la sede activa (p.ej. exigir MIPRES en HC).
    /// </summary>
    Guid? SucursalId { get; }
}

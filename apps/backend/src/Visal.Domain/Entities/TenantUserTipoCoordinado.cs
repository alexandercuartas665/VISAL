using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Relacion N:N entre un TenantUser y los tipos de servicio (CatalogoTipoServicio)
/// que puede coordinar. Reemplaza los booleans hardcodeados CoordinaTerapias /
/// CoordinaConsultas / CoordinaEnfermeria / CoordinaEquipos que vivian en
/// <c>tenant_users</c>. La existencia de una fila (TenantUserId, Codigo) implica
/// que el usuario tiene permiso de coordinar ese modulo; su ausencia lo excluye.
/// El codigo apunta al Codigo del catalogo (no un FK duro) para que renombrar
/// un tipo no rompa asignaciones existentes.
/// </summary>
public class TenantUserTipoCoordinado : TenantEntity
{
    public Guid TenantUserId { get; set; }

    /// <summary>Codigo del CatalogoTipoServicio. En MAYUSCULAS.</summary>
    public string Codigo { get; set; } = null!;
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Reporte tabular configurable por tenant. Guarda un SELECT parametrizado
/// con tokens whitelisted (@tenantId, @sedeId, @desde, @hasta) que el
/// ReporteService inyecta como parametros seguros al ejecutar.
///
/// La lista de usuarios que pueden ver este reporte se maneja via
/// <see cref="ReporteUsuario"/> (N:N). Si no hay asignaciones explicitas,
/// el reporte es privado al creador (o admin puede verlos todos).
/// </summary>
public class ReporteConfig : TenantEntity
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public string QuerySql { get; set; } = null!;
    public bool FiltraSede { get; set; } = true;
    public bool FiltraFechas { get; set; } = true;
    public bool Habilitado { get; set; } = true;
    public int Orden { get; set; }
}

/// <summary>Asignacion N:N reporte ↔ usuario (permiso de visualizacion).</summary>
public class ReporteUsuario : TenantEntity
{
    public Guid ReporteConfigId { get; set; }
    public ReporteConfig? ReporteConfig { get; set; }
    public Guid PlatformUserId { get; set; }
}

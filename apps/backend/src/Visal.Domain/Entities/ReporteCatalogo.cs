using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Reporte del CATALOGO de plataforma. Lo crea y edita UNICAMENTE el Super Admin
/// de la plataforma. Aqui vive el <see cref="QuerySql"/> (un SELECT parametrizado con
/// tokens whitelisted: {tenantId} {sedeId} {desde} {hasta}). Los tenants NUNCA ven ni
/// editan el SQL: solo activan el reporte y asignan usuarios.
///
/// Es global (NO es ITenantScoped): un unico catalogo visible para todos los tenants,
/// cada uno decide que activar via <see cref="ReporteTenantActivacion"/>.
/// </summary>
public class ReporteCatalogo : BaseEntity
{
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public string? Categoria { get; set; }
    public string QuerySql { get; set; } = null!;
    public bool FiltraSede { get; set; } = true;
    public bool FiltraFechas { get; set; } = true;

    /// <summary>Interruptor global del Super Admin. Si es false, el reporte no aparece en ninguna galeria.</summary>
    public bool Habilitado { get; set; } = true;

    public int Orden { get; set; }
}

/// <summary>
/// Activacion de un reporte del catalogo POR UN TENANT. El admin del tenant enciende
/// o apaga cada reporte de la galeria. Si no existe fila (o Activo=false) el reporte
/// no se ve en ese tenant.
/// </summary>
public class ReporteTenantActivacion : TenantEntity
{
    public Guid ReporteCatalogoId { get; set; }
    public bool Activo { get; set; }
}

/// <summary>
/// Asignacion N:N reporte(catalogo) <-> usuario, dentro de un tenant. Define quien puede
/// ver un reporte activado. Si un reporte activo no tiene ninguna asignacion, lo ven
/// todos los usuarios del tenant con acceso al modulo.
/// </summary>
public class ReporteUsuario : TenantEntity
{
    public Guid ReporteCatalogoId { get; set; }
    public Guid PlatformUserId { get; set; }
}

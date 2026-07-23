using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Auditoria + snapshot de una actualizacion masiva sobre ServicioContrato.
/// Se conservan las ultimas 20 ejecuciones por tenant (retencion FIFO) para
/// permitir rollback. Tenant-scoped.
///
/// Uso: /config/entidades > "Actualizar en masa" → busca por Descripcion,
/// aplica ModalidadFacturacion/GrupoServicioFacturacion/ServicioFacturacion
/// a todos los servicios que matcheen, guarda snapshot de valores previos
/// aqui + en <see cref="ServicioBulkUpdateItem"/>.
/// </summary>
public class ServicioBulkUpdate : TenantEntity
{
    /// <summary>Operador de busqueda: Contiene, EmpiezaCon, TerminaCon, Exacto.</summary>
    public string OperadorBusqueda { get; set; } = "";
    /// <summary>Texto ingresado por el usuario para filtrar Descripcion.</summary>
    public string TextoBusqueda { get; set; } = "";

    // Valores nuevos aplicados. Null = no se toco ese campo.
    public string? NuevaModalidadFacturacion { get; set; }
    public string? NuevoGrupoServicioFacturacion { get; set; }
    public string? NuevoServicioFacturacion { get; set; }

    /// <summary>Motivo obligatorio de la actualizacion (para auditoria).</summary>
    public string Motivo { get; set; } = "";

    /// <summary>Cantidad de servicios afectados por esta ejecucion.</summary>
    public int TotalAfectados { get; set; }

    /// <summary>Estado: Aplicada, Revertida.</summary>
    public string Estado { get; set; } = "Aplicada";

    /// <summary>Fecha en que se hizo el rollback (null si no revertida).</summary>
    public DateTimeOffset? FechaReversion { get; set; }
    public Guid? RevertidoPor { get; set; }

    public List<ServicioBulkUpdateItem> Items { get; set; } = new();
}

/// <summary>
/// Snapshot compacto de un servicio antes del bulk update. Solo guarda los
/// 3 campos comerciales de facturacion; el rollback restaura estos valores.
/// </summary>
public class ServicioBulkUpdateItem : TenantEntity
{
    public Guid BulkUpdateId { get; set; }
    public ServicioBulkUpdate? BulkUpdate { get; set; }

    public Guid ServicioContratoId { get; set; }

    // Valores previos al bulk update.
    public string? ModalidadFacturacionAntes { get; set; }
    public string? GrupoServicioFacturacionAntes { get; set; }
    public string? ServicioFacturacionAntes { get; set; }
}

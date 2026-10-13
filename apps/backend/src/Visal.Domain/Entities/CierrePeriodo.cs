using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Cierre de un periodo (anio+mes) de coordinacion: al cerrarlo se inactivan las sesiones
/// pendientes de los servicios de ese periodo (ya no se atenderan) y queda un resumen
/// (detalle por paciente/servicio/cantidad). Tenant-scoped. Reversible con reabrir.
/// </summary>
public class CierrePeriodo : TenantEntity
{
    public int Anio { get; set; }
    public int Mes { get; set; }

    /// <summary>Si el cierre fue acotado a UN paciente (cierre parcial), su id; null = todo el
    /// periodo (todos los pacientes). Permite varios cierres parciales en el mismo periodo.</summary>
    public Guid? PacienteId { get; set; }

    /// <summary>Nombre del paciente del cierre parcial (denormalizado para el historial); null
    /// cuando el cierre cubrio todos los pacientes del periodo.</summary>
    public string? PacienteNombre { get; set; }

    /// <summary>Sede (nombre de la sucursal) a la que se acoto el cierre; null = todas las
    /// sedes del periodo. Coincide con <see cref="Asignacion.Sucursal"/> (texto).</summary>
    public string? Sucursal { get; set; }

    public DateTimeOffset CerradoEn { get; set; }

    /// <summary>Servicios (asignaciones) con sesiones pendientes anuladas en este cierre.</summary>
    public int TotalServicios { get; set; }

    /// <summary>Total de sesiones pendientes anuladas (sumatoria del detalle).</summary>
    public int TotalSesiones { get; set; }

    /// <summary>Resumen por paciente/servicio tomado al momento de cerrar.</summary>
    public List<CierrePeriodoDetalle> Detalles { get; set; } = new();
}

/// <summary>Fila del resumen de un cierre: paciente + servicio + cuantas sesiones quedaron sin atender.</summary>
public class CierrePeriodoDetalle : TenantEntity
{
    public Guid CierrePeriodoId { get; set; }
    public CierrePeriodo? CierrePeriodo { get; set; }

    public string PacienteNombre { get; set; } = "";
    public string? PacienteDocumento { get; set; }
    public string ServicioNombre { get; set; } = "";
    public string? CodigoServicio { get; set; }

    /// <summary>Sesiones no atendidas (pendientes) de ese servicio al cerrar el periodo.</summary>
    public int CantidadPendiente { get; set; }
}

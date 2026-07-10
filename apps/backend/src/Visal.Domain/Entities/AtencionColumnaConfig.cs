using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Configuracion (por tenant) de una columna de la tabla "MIS SERVICIOS ASIGNADOS"
/// del modulo /atencion. Le permite al administrador decidir que columnas se ven,
/// en que orden y con que nombre. Cuando una columna no tiene fila aca, la UI usa
/// su default hardcoded (visible=true, orden=indice del array, alias=nombre por
/// defecto). Una fila por (Tenant, ColumnaKey).
///
/// Deliberadamente NO por usuario: la disposicion la fija un admin y todos los
/// profesionales ven la misma tabla. Facilita capacitacion y consistencia entre
/// equipos.
/// </summary>
public class AtencionColumnaConfig : TenantEntity
{
    /// <summary>Identificador logico de la columna (ej. "historia_medica", "session_no").
    /// Debe coincidir con las keys del array _columnasDefault en Atencion.razor.</summary>
    public string ColumnaKey { get; set; } = null!;

    /// <summary>True para mostrar la columna. Default true.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Nombre alternativo mostrado en el header. Vacio -> nombre por defecto.</summary>
    public string? Alias { get; set; }

    /// <summary>Posicion de la columna (menor primero). Null usa el orden default.</summary>
    public int? Orden { get; set; }
}

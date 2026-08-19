using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Preferencia de una columna de la VISTA TABLA de un tablero, POR USUARIO. A diferencia de
/// <see cref="AtencionColumnaConfig"/> (que es por tenant / la fija un admin), esta es individual:
/// cada usuario ordena, oculta, renombra y ajusta el ancho de las columnas a su gusto sin afectar
/// a los demas. Cuando una columna no tiene fila aca, la UI usa su default (visible, orden del
/// catalogo de campos, sin alias, ancho automatico). Una fila por (Tenant, Usuario, Tablero, ColumnKey).
/// </summary>
public class TaskBoardColumnPref : TenantEntity
{
    /// <summary>Usuario dueno de la preferencia (PlatformUserId).</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Tablero al que aplica.</summary>
    public Guid BoardId { get; set; }

    /// <summary>Identificador logico de la columna: el FieldKey del campo dinamico, o una clave
    /// especial ("__titulo__", "__estado__") para las columnas fijas.</summary>
    public string ColumnKey { get; set; } = null!;

    /// <summary>True para mostrar la columna. Default true.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Nombre alternativo mostrado en el header. Vacio -> etiqueta por defecto.</summary>
    public string? Alias { get; set; }

    /// <summary>Posicion de la columna (menor primero). Null usa el orden default.</summary>
    public int? Orden { get; set; }

    /// <summary>Ancho fijo en px. Null usa ancho automatico.</summary>
    public int? Ancho { get; set; }
}

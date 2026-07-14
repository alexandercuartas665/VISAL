using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Catalogo de tipos de turno por tenant (Manana, Tarde, Noche, Dia,
/// Dia-Noche, Libre + los que el tenant agregue). Cada tipo trae su
/// codigo corto, etiqueta visible, horas default y paleta de colores
/// para renderizar la celda en el editor de la programacion.
///
/// En el legacy los 6 tipos estaban hardcoded en JS. En el destino
/// son catalogo editable para que un tenant pueda anadir "Guardia 24h"
/// o renombrar "Manana" a "AM" sin cambio de codigo.
///
/// Tenant-scoped. Unicidad: (TenantId, Codigo).
/// </summary>
public class TipoTurno : TenantEntity
{
    /// <summary>Codigo corto usado en el JSON de la grilla. Ej. "M","T","N","D","DN","L".
    /// varchar(8) — permite tenant agregar codigos como "G24" o "TN".</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>Etiqueta visible al usuario. Ej. "Manana", "Tarde", "Guardia 24h". varchar(40).</summary>
    public string Etiqueta { get; set; } = null!;

    /// <summary>Horas por defecto cuando se pinta una celda con este tipo. Puede
    /// sobrescribirse por celda desde el input "Horas" del editor. Rango 0..24 con step 0.5.</summary>
    public decimal HorasDefault { get; set; }

    /// <summary>Color de fondo de la celda en formato #RRGGBB. varchar(9) por si algun
    /// dia se agrega alpha #RRGGBBAA.</summary>
    public string ColorFondo { get; set; } = "#FFFFFF";

    /// <summary>Color del texto sobre la celda.</summary>
    public string ColorTexto { get; set; } = "#000000";

    /// <summary>Color del borde de la celda (usado para diferenciar mejor tipos con fondo similar).</summary>
    public string ColorBorde { get; set; } = "#CCCCCC";

    /// <summary>Orden de aparicion en el sidebar del editor. Menor primero.</summary>
    public int Orden { get; set; }

    /// <summary>Soft-disable. Un tipo inactivo no aparece en el sidebar del editor
    /// pero se sigue mostrando en programaciones historicas que lo usaron.</summary>
    public bool Activo { get; set; } = true;
}

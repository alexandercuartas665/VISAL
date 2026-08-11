using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Preferencia por tenant sobre como aparece cada columna del archivo de salida
/// (Excel/CSV) de un snapshot de facturacion. La lista canonica de columnas la
/// da el <c>ISnapshotBuilder</c> del tipo — esta entidad solo guarda overrides
/// del tenant (orden, visibilidad, alias). Si no hay override, se usa el orden
/// natural del builder, todas visibles y sin alias.
///
/// UNIQUE (TenantId, Tipo, ColumnaOriginal) — un solo override por columna.
/// Columnas nuevas que el builder agregue en versiones futuras aparecen al
/// final del listado (sin override) y visibles por default.
/// </summary>
public class FacturacionSnapshotColumnaConfig : TenantEntity
{
    /// <summary>Tipo de snapshot al que aplica esta config.</summary>
    public TipoSnapshot Tipo { get; set; }

    /// <summary>
    /// Nombre EXACTO de la columna tal como la publica el builder (incluidas
    /// tildes rotas del template EPS). Es la clave para localizar el override
    /// en el momento de exportar.
    /// </summary>
    public string ColumnaOriginal { get; set; } = string.Empty;

    /// <summary>Posicion en el archivo de salida (0-based). Menor va primero.</summary>
    public int Orden { get; set; }

    /// <summary>Si false, la columna NO se emite en el archivo de salida.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Header alternativo para el archivo de salida. Null o vacio = usar
    /// <see cref="ColumnaOriginal"/>. No afecta la clave interna del dato
    /// (que sigue siendo la columna original).
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>Descripcion humana del campo (para que sirve, que significa).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Ruta/origen del dato (ej. "Paciente.NumeroDocumento" o "Asignacion.ContratoCodigo -> ContratoAseguradora.Codigo").</summary>
    public string? RutaOrigen { get; set; }

    /// <summary>
    /// Formato de salida de la celda en el archivo (Excel/CSV). General = sin formato (tal cual).
    /// Los demas parsean el valor y aplican formato de fecha/numero/moneda al exportar.
    /// </summary>
    public SnapshotColumnaFormato FormatoTipo { get; set; } = SnapshotColumnaFormato.General;

    /// <summary>
    /// Patron Excel a usar cuando FormatoTipo = Personalizado (p.ej. "dd/mm/yyyy" o "#,##0.00").
    /// Ignorado para los demas tipos (que traen su patron predefinido).
    /// </summary>
    public string? FormatoPatron { get; set; }

    /// <summary>
    /// Marca de trabajo del tenant para senalar que esta columna esta "en error"
    /// (dato mal mapeado, formato equivocado, etc.). Es solo indicativa: se guarda
    /// y se muestra en el configurador, pero NO altera la exportacion.
    /// </summary>
    public bool EnError { get; set; }
}

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
}

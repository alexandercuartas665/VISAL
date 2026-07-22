using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Auditoria de cambios manuales sobre celdas de un snapshot de facturacion.
/// Append-only — cada UPDATE a un valor de columna genera una fila aqui con
/// el ValorAntes y ValorDespues, quien lo hizo y cuando. El operador puede
/// revisar el historial completo antes de radicar el archivo a la EPS.
///
/// Motivo es opcional pero recomendado; la UI puede pedirlo cuando el cambio
/// afecta columnas criticas (Autorizacion, TipoArchivoRips).
/// </summary>
public class FacturacionSnapshotFilaCambio : TenantEntity
{
    /// <summary>Snapshot padre — mismo tenant. FK con ON DELETE CASCADE.</summary>
    public Guid SnapshotId { get; set; }
    public FacturacionSnapshot? Snapshot { get; set; }

    /// <summary>Fila afectada. No usamos FK dura para no bloquear cascadas del snapshot.</summary>
    public Guid FilaId { get; set; }

    /// <summary>Numero de fila (denormalizado) — util para mostrar "fila 42" sin JOIN.</summary>
    public int NumeroFila { get; set; }

    /// <summary>Nombre exacto de la columna (tal cual la publica el builder).</summary>
    public string ColumnaOriginal { get; set; } = string.Empty;

    /// <summary>Valor previo serializado como string. Null si la celda estaba vacia.</summary>
    public string? ValorAntes { get; set; }

    /// <summary>Valor nuevo serializado como string. Null si el usuario limpio la celda.</summary>
    public string? ValorDespues { get; set; }

    /// <summary>Usuario que hizo el cambio (redundante con CreatedBy — dejamos ambos para claridad).</summary>
    public Guid ActorUserId { get; set; }

    /// <summary>Motivo opcional del cambio — recomendado para auditoria EPS.</summary>
    public string? Motivo { get; set; }
}

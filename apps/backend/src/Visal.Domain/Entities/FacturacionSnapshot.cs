using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Metadata de un snapshot transaccional del modulo de Facturacion. Una fila
/// por snapshot generado. Los datos de cada registro viven en <see cref="FacturacionSnapshotFila"/>
/// para poder paginar y descargar snapshots grandes sin cargar todo en memoria.
///
/// Filosofia:
///   - Instantanea inmutable del momento de la ejecucion (no es un reporte).
///   - Solo se archiva con motivo obligatorio, jamas se elimina desde la UI.
///   - El formato de descarga (Excel/CSV) es impuesto por la EPS/MinSalud —
///     los headers exactos los sabe el <c>ISnapshotBuilder</c> del tipo.
/// </summary>
public class FacturacionSnapshot : TenantEntity
{
    /// <summary>Nombre legible del snapshot. Ej: "ASMET SALUD - Junio 2026".</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Tipo de snapshot. Determina que <c>ISnapshotBuilder</c> lo produce.</summary>
    public TipoSnapshot Tipo { get; set; }

    /// <summary>
    /// Aseguradora (EPS) principal del snapshot. Se popula automatico desde el
    /// filtro <c>aseguradoraId</c> al generar. Sirve para mostrar columna en el
    /// listado y filtrar sin tener que parsear <see cref="FiltrosJson"/>.
    /// Null solo en tipos de snapshot que no aplican por EPS (Glosas globales, etc).
    /// </summary>
    public Guid? AseguradoraId { get; set; }
    public Aseguradora? Aseguradora { get; set; }

    /// <summary>
    /// Filtros aplicados serializados como JSON. La forma varia por tipo — ver
    /// el doc de cada tipo. Ejemplo Relacion de Facturas:
    /// <c>{"aseguradoraIds":[...],"sucursalIds":[...],"fechaInicio":"2026-06-01","fechaFin":"2026-06-30"}</c>.
    /// Se persiste como <c>jsonb</c> en Postgres.
    /// </summary>
    public string FiltrosJson { get; set; } = "{}";

    /// <summary>Estado actual del snapshot. Ver <see cref="EstadoSnapshot"/>.</summary>
    public EstadoSnapshot Estado { get; set; } = EstadoSnapshot.Ejecutando;

    /// <summary>Motivo del archivado. Obligatorio cuando <see cref="Estado"/> = Archivado (min 10 chars).</summary>
    public string? MotivoArchivado { get; set; }

    /// <summary>Fecha en que se archivo el snapshot. Coincide con el UPDATE que lo movio de Vigente a Archivado.</summary>
    public DateTimeOffset? FechaArchivado { get; set; }

    /// <summary>Id del usuario que archivo. Para auditoria.</summary>
    public Guid? ArchivadoPor { get; set; }

    /// <summary>Marca temporal de inicio de la generacion (server time).</summary>
    public DateTimeOffset FechaEjecucionInicio { get; set; }

    /// <summary>Marca temporal de fin de la generacion. Null mientras el estado sea Ejecutando.</summary>
    public DateTimeOffset? FechaEjecucionFin { get; set; }

    /// <summary>Duracion redondeada en milisegundos. Se calcula al terminar.</summary>
    public int? DuracionMs { get; set; }

    /// <summary>Cantidad final de filas producidas por el builder.</summary>
    public int TotalFilas { get; set; }

    /// <summary>Mensaje de error cuando <see cref="Estado"/> = Fallido.</summary>
    public string? ErrorMensaje { get; set; }

    /// <summary>Filas del snapshot (una por registro). Cascade en delete pero la UI nunca lo dispara.</summary>
    public List<FacturacionSnapshotFila> Filas { get; set; } = new();
}

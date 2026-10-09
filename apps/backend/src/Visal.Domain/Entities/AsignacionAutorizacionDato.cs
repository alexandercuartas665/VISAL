using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Datos extraidos por IA (DeepSeek) del PDF de la autorizacion de una asignacion.
/// 1:1 con la asignacion (unico por AsignacionId). Se revisan/editan en el modal de
/// autorizacion antes de guardar. No reemplazan la ficha del paciente: son el dato tal
/// como vino en la autorizacion, y desde el modal se puede proponer actualizar al paciente.
/// </summary>
public class AsignacionAutorizacionDato : TenantEntity
{
    public Guid AsignacionId { get; set; }

    public string? Nombre { get; set; }
    public string? TipoDocumento { get; set; }
    public string? Documento { get; set; }
    public string? NumeroAutorizacion { get; set; }

    /// <summary>Telefonos separados por coma (como vinieron en la autorizacion).</summary>
    public string? TelefonosCsv { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }

    /// <summary>JSON crudo devuelto por la IA (trazabilidad).</summary>
    public string? RawJson { get; set; }

    public DateTimeOffset ExtraidoEn { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Emision publica de una "Orden de Medicamentos" (ORD-MEDI). Cada fila es un
/// snapshot inmutable de la orden al momento de emitirla, con un codigo corto
/// legible que se codifica en un QR y sirve para verificar la formula en una
/// pagina publica (/verificar-orden/{codigo}).
///
/// La HC de origen puede cambiar despues; esta fila NO. Si la orden queda
/// obsoleta se revoca (RevocadaAt), no se edita ni se borra. Es tenant-scoped
/// para el uso interno; la lectura publica por codigo usa IgnoreQueryFilters
/// (el codigo es globalmente unico) y el tenant se lee de la propia fila.
/// </summary>
public class OrdenMedicamentoPublica : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>
    /// Token corto legible (12 caracteres alfanumericos, base32 sin caracteres
    /// ambiguos 0/O/1/I/L). Globalmente unico (indice unico). Es lo que va dentro
    /// del QR y lo que el receptor puede tipear en la pagina publica.
    /// </summary>
    public string CodigoVerificacion { get; set; } = null!;

    /// <summary>Momento de emision (firma) de la orden.</summary>
    public DateTimeOffset EmitidoAt { get; set; }

    /// <summary>Usuario (PlatformUser) que emitio la orden.</summary>
    public Guid EmitidoPor { get; set; }

    /// <summary>
    /// Snapshot inmutable de la orden serializado en jsonb: bag de valores del
    /// formulario ORD-MEDI (paciente, diagnosticos, items, profesional, firma) tal
    /// como quedaron al emitir. Es la fuente de verdad de la impresion y de la
    /// verificacion publica. No se toca despues de crear la fila.
    /// </summary>
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>Momento de revocacion. Null = la orden sigue vigente.</summary>
    public DateTimeOffset? RevocadaAt { get; set; }

    /// <summary>Usuario que revoco. Null si no ha sido revocada.</summary>
    public Guid? RevocadaPor { get; set; }

    /// <summary>Motivo textual de la revocacion (opcional).</summary>
    public string? RevocacionMotivo { get; set; }

    /// <summary>Helper de dominio: true si la orden esta revocada.</summary>
    public bool EstaRevocada => RevocadaAt is not null;
}

using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Un contenido dentro de un archivo/tipologia de la cuenta medica
/// (<see cref="AseguradoraInformeItem"/>). Un archivo agrupa VARIOS contenidos
/// que el generador (fase 2) fusiona en un solo PDF; por ejemplo el archivo
/// "SOPORTES" puede contener firmas + escalas + consentimientos.
///
/// Reemplaza el modelo anterior de "1 item = 1 origen": ahora el origen vive
/// aca (N por item). Los campos legacy Origen/TipologiaArchivoId/SoloUltimo del
/// item se conservan por compatibilidad pero ya no son la fuente de verdad.
/// </summary>
public class AseguradoraInformeContenido : TenantEntity
{
    /// <summary>Archivo (tipologia) al que pertenece este contenido.</summary>
    public Guid ItemId { get; set; }
    public AseguradoraInformeItem? Item { get; set; }

    /// <summary>Posicion del contenido dentro del archivo (0..N). Define el orden
    /// en que se fusionan las paginas del PDF resultante.</summary>
    public int Orden { get; set; }

    /// <summary>Que documento aporta este contenido (HC, firma, evolucion,
    /// escala, adjunto...).</summary>
    public OrigenInformeItem Origen { get; set; }

    /// <summary>Solo aplica cuando <see cref="Origen"/> filtra por catalogo
    /// TipologiaArchivo (DocumentoHc, DocumentoPacienteLibre, DocumentoNota).
    /// Null = cualquier tipologia.</summary>
    public Guid? TipologiaArchivoId { get; set; }

    /// <summary>Solo aplica a origenes multi-instancia. True = incluye SOLO el
    /// mas reciente. False = incluye todos.</summary>
    public bool SoloUltimo { get; set; }
}

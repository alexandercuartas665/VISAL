using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Item individual dentro de la <see cref="AseguradoraCuentaMedicaConfig"/>.
/// Describe QUE documento entra al informe, con QUE alias, en QUE posicion,
/// bajo QUE patron de nombre. El generador (fase 2) los recorre en orden.
/// </summary>
public class AseguradoraInformeItem : TenantEntity
{
    public Guid ConfigId { get; set; }

    /// <summary>Posicion (0..N). Usado por el drag&amp;drop del grid.</summary>
    public int Orden { get; set; }

    /// <summary>Titulo de la seccion en la que aparece este item (opcional).
    /// Ej: "Historia", "Documentos", "Autorizaciones". El generador agrupa
    /// contiguos y titula la seccion antes del primer item.</summary>
    public string? Seccion { get; set; }

    public OrigenInformeItem Origen { get; set; }

    /// <summary>Solo aplica cuando Origen filtra por catalogo TipologiaArchivo
    /// (DocumentoHc, DocumentoPacienteLibre, DocumentoNota). Null = cualquier
    /// tipologia.</summary>
    public Guid? TipologiaArchivoId { get; set; }

    /// <summary>Sigla corta que identifica al item en el informe (HC, FIR, AUT,
    /// EVO). Aparece en el indice y suele ser el prefijo del nombre de archivo
    /// via el token {sigla}.</summary>
    public string Alias { get; set; } = null!;

    /// <summary>Descripcion humana que aparece en el indice del informe. Ej:
    /// "Historia clinica completa" para HC.</summary>
    public string? Descripcion { get; set; }

    /// <summary>Patron especifico del item; si null usa el
    /// <see cref="AseguradoraCuentaMedicaConfig.PatronNombreDefault"/>.</summary>
    public string? PatronNombre { get; set; }

    /// <summary>Si true el generador debe reportar advertencia/error cuando el
    /// paciente no tiene el documento correspondiente.</summary>
    public bool Obligatorio { get; set; }

    /// <summary>Solo aplica a origenes multi-instancia (HistoriaClinicaPdf,
    /// DocumentoHc, DocumentoNota, Consentimiento, Evolucion, Escala,
    /// AutorizacionAsignacion). True = incluye SOLO la mas reciente. False =
    /// incluye todas.</summary>
    public bool SoloUltimo { get; set; }
}

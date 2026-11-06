using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Un ARCHIVO (tipologia de salida) dentro de la
/// <see cref="AseguradoraCuentaMedicaConfig"/>. Tiene un nombre (Descripcion) y
/// un patron de nombre de archivo, y agrupa VARIOS contenidos
/// (<see cref="AseguradoraInformeContenido"/>) que el generador (fase 2) fusiona
/// en un solo PDF. El generador recorre los archivos en orden.
///
/// Compatibilidad: los campos <see cref="Origen"/>, <see cref="TipologiaArchivoId"/>
/// y <see cref="SoloUltimo"/> son LEGACY (modelo anterior "1 item = 1 origen").
/// Se conservan pero la fuente de verdad del contenido es la coleccion
/// <see cref="Contenidos"/>.
/// </summary>
public class AseguradoraInformeItem : TenantEntity
{
    public Guid ConfigId { get; set; }

    /// <summary>Contenidos que componen este archivo (N origenes fusionados).</summary>
    public List<AseguradoraInformeContenido> Contenidos { get; set; } = new();

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

    /// <summary>Orden multinivel del contenido "Formulario por tipo" (Historias
    /// medicas) dentro del PDF del paciente. JSON: arreglo de niveles
    /// <c>[{"campo":"cup|fecha","desc":false}, ...]</c> aplicados en secuencia.
    /// Null/vacio = orden cronologico ascendente (comportamiento historico).</summary>
    public string? OrdenJson { get; set; }

    /// <summary>Si true, este archivo se genera como UN SOLO PDF unificado con todos
    /// los pacientes concatenados (en el orden del snapshot), en vez del ZIP con un PDF
    /// por paciente (comportamiento historico, false).</summary>
    public bool PdfUnico { get; set; }

    /// <summary>Si true, el archivo genera un PDF por CADA autorizacion del paciente
    /// (no uno fusionado por paciente): el ZIP trae un archivo por autorizacion, nombrado
    /// con el numero de autorizacion. Cada PDF trae el CONTENIDO configurado del archivo
    /// (firmas, historias por tipo, adjuntos, la propia autorizacion...) filtrado a lo que
    /// pertenece a esa autorizacion, atribuido por la HC de la que cuelga (cadena
    /// sesion-&gt;turno-&gt;asignacion). El contenido "libre" sin HC no entra en este modo.
    /// Default false (comportamiento historico: todas las autorizaciones en un PDF).</summary>
    public bool UnoPorAutorizacion { get; set; }

    /// <summary>Si true, dentro del PDF de cada paciente los documentos de historia/servicio
    /// se agrupan por CUP (CodigoRips de la asignacion) y se inserta una pagina "rotulo"
    /// separadora (1/4 de carta, con el logo del tenant + codigo CUP + nombre de servicio +
    /// sede) antes del primer documento de cada CUP. El CUP manda el orden: numericos
    /// ascendentes y los que empiezan por letra al final; dentro de cada CUP se respeta el
    /// orden configurado (fecha/sesion). Solo aplica al contenido con CUP (historias);
    /// autorizaciones, firmas y anexos quedan en su lugar. Default false.</summary>
    public bool RotuloSeparadorCup { get; set; }

    /// <summary>Si true, cada documento externo (anexo) que entra al archivo recibe un
    /// "membrete" estampado en la parte superior de su PRIMERA pagina con el codigo de la
    /// historia clinica de la que proviene (HC-xxxxxx), su CUP (CodigoRips de la asignacion)
    /// y el nombre del servicio. Pensado para los "Listados de firmas" y demas anexos, para
    /// identificar de que historia/servicio viene cada hoja. Solo aplica a los contenidos de
    /// documento externo (DocumentoHc/DocumentoPacienteLibre/DocumentoNota); las historias,
    /// autorizaciones y firmas no se tocan. Default false.</summary>
    public bool MembreteHcCup { get; set; }
}

using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Definicion de un formulario/plantilla clinica (modulo Motor de Formularios, 2.M10).
/// Entidad TENANT-SCOPED. La estructura (arbol de secciones y campos del disenador) se
/// guarda como JSON en <see cref="SchemaJson"/> (columna jsonb). El contenido diligenciado
/// vive aparte (form_respuestas, fase posterior). Esta es la cabecera + el esquema editable.
/// </summary>
public class FormDefinition : TenantEntity
{
    /// <summary>Codigo logico del formato (unico por tenant). Ej. "HC-GENERAL".</summary>
    public string Codigo { get; set; } = null!;

    /// <summary>
    /// Codigo secundario opcional (id alternativo, no unico). Sirve para mapear el
    /// formato a un identificador externo (codigo legacy, codigo del prestador,
    /// codigo de un sistema integrado, etc.). Texto libre, puede repetirse.
    /// </summary>
    public string? CodigoSecundario { get; set; }

    /// <summary>Nombre visible. Ej. "Historia Clinica General".</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>Version editable de la definicion (texto libre por ahora).</summary>
    public string? Version { get; set; }

    /// <summary>Tipo/categoria del formato (historia, nota, consentimiento, orden...).</summary>
    public string? Tipo { get; set; }

    /// <summary>Arbol completo del disenador (secciones + campos) serializado como JSON (jsonb).</summary>
    public string SchemaJson { get; set; } = "{\"children\":[]}";

    /// <summary>Si el formato esta activo/publicado.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Rutas de prefill: mapeo nombrado entre campos de otros modulos (paciente,
    /// profesional, contrato, etc.) y campos del schema de este formulario. Permite
    /// que cuando se inicia una historia/instancia se prefille automaticamente con
    /// datos del contexto. Estructura JSON: { "routes": [ { "name": "...", "sourceModule": "...", "mappings": [ { "source": "...", "target": "..." } ] } ] }.
    /// Null o vacio = no hay rutas configuradas (el consumidor cae al match por nombre).
    /// </summary>
    public string? PrefillRoutesJson { get; set; }

    /// <summary>
    /// Modo terapia: codigo del FormDefinition de EVOLUCION que se usa para la
    /// 2da sesion en adelante de un servicio cuyo formato de HC es ESTE. La primera
    /// sesion (cronologica) usa este formato completo; las siguientes usan el formato
    /// corto apuntado aqui. Sigue siendo una HistoriaClinica por debajo (factura,
    /// candado de orden, Completado y revision quedan intactos), solo cambia el schema
    /// que llena el profesional. Null = comportamiento normal (todas las sesiones usan
    /// este mismo formato).
    /// </summary>
    public string? FormatoEvolucionCodigo { get; set; }

    /// <summary>
    /// Modo terapia — "puente" entre asignaciones. Numero de meses durante los cuales
    /// el puente de evolucion se mantiene ABIERTO despues de la ultima atencion del
    /// paciente en este formato. Cuando una asignacion NUEVA del mismo paciente+formato
    /// arranca (su sesion 1) y la ultima atencion del paciente en este formato cae
    /// dentro de esta ventana (deslizante: se mide desde la ultima fecha_atencion),
    /// esa sesion 1 tambien se sirve con el <see cref="FormatoEvolucionCodigo"/> (sigue
    /// el puente) en vez de abrir una historia base nueva. Null o 0 = sin puente entre
    /// asignaciones (comportamiento historico: cada asignacion reinicia en formato base).
    /// Solo aplica si <see cref="FormatoEvolucionCodigo"/> esta configurado.
    /// </summary>
    public int? MesesPuente { get; set; }
}

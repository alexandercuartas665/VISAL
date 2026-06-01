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
}

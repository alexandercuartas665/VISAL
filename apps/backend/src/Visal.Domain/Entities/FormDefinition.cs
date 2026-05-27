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
}

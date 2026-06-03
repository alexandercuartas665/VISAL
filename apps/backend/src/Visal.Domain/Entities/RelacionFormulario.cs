using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Relacion configurable entre dos FormDefinitions del tenant. Sirve para
/// describir que formulario destino (ej: una nota de evolucion) le
/// corresponde a un formulario origen (ej: una historia clinica). El
/// sistema usa esta tabla para sugerir el formulario adecuado al iniciar
/// una nueva nota o evolucion a partir de la HC activa.
///
/// El significado del par origen → destino es semantico del usuario; el
/// modelo no obliga a que origen sea de tipo "Historia" ni destino sea
/// "Evolucion", aunque ese sea el caso de uso principal.
/// </summary>
public class RelacionFormulario : TenantEntity
{
    public Guid FormularioOrigenId { get; set; }
    public FormDefinition? FormularioOrigen { get; set; }

    public Guid FormularioDestinoId { get; set; }
    public FormDefinition? FormularioDestino { get; set; }

    /// <summary>
    /// Categoria de la relacion para que el sistema sepa con que proposito usarla:
    /// "EVOLUCION" cuando el destino es una nota de evolucion derivada del origen,
    /// "CONSENTIMIENTO" cuando es un consentimiento informado que se sugiere al
    /// abrir el origen. Valor libre — el modelo no obliga a usar estas dos cadenas
    /// pero la UI ofrece el dropdown.
    /// </summary>
    public string? TipoRelacion { get; set; }

    /// <summary>Si la relacion esta vigente. Las desactivadas no se usan para sugerir.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Comentario opcional para que el admin recuerde por que la creo.</summary>
    public string? Observacion { get; set; }
}

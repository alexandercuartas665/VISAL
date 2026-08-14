using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Definicion de un campo dinamico de un TABLERO (kanban). Portado del pipeline de
/// CUBOT.travels (PipelineFieldDefinition), atado aqui al <see cref="TaskBoard"/> en vez
/// de a una etapa: los campos aplican a todas las tarjetas del tablero. Los valores por
/// tarjeta se guardan en <see cref="TaskCard.FieldValuesJson"/> indexados por FieldKey.
/// Entidad TENANT-SCOPED.
/// </summary>
public class TaskFieldDefinition : TenantEntity
{
    public Guid BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    /// <summary>Clave estable del campo (no cambia), p.ej. "tipo_pqrs". Unica por tablero.</summary>
    public string FieldKey { get; set; } = null!;

    public string Label { get; set; } = null!;
    public TaskFieldType FieldType { get; set; } = TaskFieldType.Text;

    /// <summary>Si es true, el campo aparece como filtro (chips por valor) en la barra del tablero.</summary>
    public bool ShowInFilter { get; set; }

    /// <summary>Ancho del campo en el layout del modal de tarjeta (1 = angosto, 2 = medio, 3 = ancho).</summary>
    public int Column { get; set; } = 1;

    public int SortOrder { get; set; }

    /// <summary>Opciones para tipo Select, separadas por salto de linea.</summary>
    public string? Options { get; set; }

    /// <summary>
    /// Descripcion/contexto del campo: para que sirve. Se muestra como ayuda y queda
    /// disponible para que agentes de IA entiendan y llenen el campo a futuro.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Permite capturar varios valores en este campo. Se guardan como arreglo JSON.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>
    /// Solo para campos AllowMultiple: cada valor lleva ademas un texto de detalle.
    /// Se guarda como arreglo JSON de objetos {"d":detalle,"v":valor}.
    /// </summary>
    public bool MultiWithDetail { get; set; }

    /// <summary>
    /// Solo para FieldType=Total: lista de FieldKeys (separados por coma) de los campos
    /// numericos/moneda del mismo tablero que se suman para calcular el total.
    /// </summary>
    public string? TotalSourceKeys { get; set; }

    /// <summary>
    /// Si se indica el FieldKey de un campo numerico del mismo tablero, este campo se repite
    /// N veces segun el valor de ese campo. Los valores se guardan como arreglo JSON.
    /// </summary>
    public string? RepeatWithFieldKey { get; set; }
}

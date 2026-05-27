namespace Visal.Domain.Enums;

/// <summary>Tipo de un campo configurable del embudo (modulo 2.1). Define como se captura/renderiza.</summary>
public enum PipelineFieldType
{
    Text,
    Number,
    Currency,
    TextArea,
    Select,
    Date,
    Phone,
    /// <summary>Campo calculado de solo lectura: suma los valores de los campos origen indicados
    /// en TotalSourceKeys (si un origen es multiple/repetido, suma todos sus registros).</summary>
    Total
}

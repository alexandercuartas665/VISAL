namespace Visal.Domain.Entities;

/// <summary>
/// Tipo de un campo dinamico de un tablero. Define como se captura/renderiza en la
/// tarjeta. Portado del pipeline de CUBOT.travels (enum PipelineFieldType), adaptado
/// a los tableros de Visal (se agrega Email para casos como PQRS).
/// </summary>
public enum TaskFieldType
{
    Text,
    Number,
    Currency,
    TextArea,
    Select,
    Date,
    Email,
    Phone,

    /// <summary>Campo calculado de solo lectura: suma los valores de los campos origen
    /// indicados en TotalSourceKeys (si un origen es multiple, suma todos sus registros).</summary>
    Total,

    /// <summary>Hora simple (HH:mm).</summary>
    Time,

    /// <summary>Dos horas en un solo campo: hora de salida y hora de llegada
    /// (se guardan como "salida - llegada").</summary>
    TimeRange,

    /// <summary>Separador visual (titulo de grupo / linea divisoria). No captura ningun valor.</summary>
    Separator
}

namespace Visal.Domain.Enums;

/// <summary>
/// Formato de salida de una columna del archivo (Excel/CSV) de un snapshot de facturacion.
/// Lo elige el tenant en el configurador de columnas. General = sin formato (comportamiento
/// historico: el valor se escribe tal cual, tipado si es numero/bool). Los demas parsean el
/// valor y le aplican un formato de fecha/numero. Personalizado usa el patron Excel indicado
/// en FormatoPatron.
/// </summary>
public enum SnapshotColumnaFormato
{
    General = 0,
    Texto = 1,
    NumeroEntero = 2,
    NumeroDecimal = 3,
    Moneda = 4,
    Fecha = 5,
    FechaHora = 6,
    Porcentaje = 7,
    /// <summary>Fecha con el ano adelante: aaaa/mm/dd (ISO-like).</summary>
    FechaIso = 8,
    /// <summary>Fecha y hora con el ano adelante: aaaa/mm/dd hh:mm.</summary>
    FechaHoraIso = 9,
    Personalizado = 99
}

namespace Visal.Domain.Enums;

/// <summary>
/// Tipo de un campo dinamico de la ficha de paciente. Decide el input en la ficha de
/// admision y como se interpreta el valor (todos se guardan como string en el jsonb).
/// </summary>
public enum PacienteCampoTipo
{
    Texto,
    TextoLargo,
    Numero,
    Decimal,
    Moneda,
    Fecha,
    FechaHora,
    Seleccion,
    Booleano,
    Telefono,
    Email,
    Separador
}

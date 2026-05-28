namespace Visal.Domain.Enums;

/// <summary>
/// Estado del ciclo de vida de una asignacion de servicio:
/// - Pendiente: recien creada por M2 Asignacion, sin profesional asignado.
/// - Asignado: M3 Coordinacion le asigno profesional y turnos.
/// - Cerrado: el servicio termino (todos los turnos completados o cierre manual).
/// </summary>
public enum AsignacionEstado
{
    Pendiente,
    Asignado,
    Cerrado
}

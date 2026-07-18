namespace Visal.Domain.Enums;

/// <summary>
/// Estados del ciclo de vida de un snapshot. No existe "Eliminado" por politica:
/// un snapshot llega hasta Archivado con motivo obligatorio, jamas se borra.
/// </summary>
public enum EstadoSnapshot
{
    /// <summary>Generacion en curso. La fila metadata ya existe pero las filas de datos aun no.</summary>
    Ejecutando = 1,

    /// <summary>Terminado OK. Es la instantanea activa que se descarga y radica.</summary>
    Vigente = 2,

    /// <summary>Archivado con motivo obligatorio. Sigue consultable pero no se lista por defecto.</summary>
    Archivado = 3,

    /// <summary>La generacion aborto con error. La fila metadata guarda el mensaje.</summary>
    Fallido = 4
}

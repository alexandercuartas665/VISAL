namespace Visal.Domain.Enums;

/// <summary>
/// Tipo de novedad que reduce la disponibilidad de la agenda de un profesional
/// (Ola 4 del modulo de Agendas). Es el equivalente por-profesional de los dias
/// inactivos de sede: vacaciones, incapacidad, permiso, u otro cierre puntual.
/// Se persiste como texto.
/// </summary>
public enum TipoNovedadProfesional
{
    Vacaciones = 0,
    Incapacidad = 1,
    Permiso = 2,
    Otro = 3
}

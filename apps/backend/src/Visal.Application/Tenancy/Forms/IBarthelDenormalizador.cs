namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Vuelca (desnormaliza) los datos de la escala Barthel (PP-FO-46) al paciente:
/// puntaje total, clasificacion, snapshot completo y fecha. Gana siempre la escala
/// cerrada mas reciente del paciente. Lo usan dos disparadores:
///  1. <see cref="IEscalaService.CerrarAsync"/>, al marcar definitiva una escala Barthel.
///  2. El boton "Recalcular Barthel" del modulo Admision (recalculo de todo el tenant).
/// </summary>
public interface IBarthelDenormalizador
{
    /// <summary>
    /// Vuelca al paciente los datos de UNA escala recien cerrada, si es Barthel y si
    /// es mas reciente que lo ya guardado en el paciente. Devuelve true si actualizo.
    /// </summary>
    Task<bool> AplicarDesdeEscalaAsync(Guid escalaId, CancellationToken ct = default);

    /// <summary>
    /// Recalcula Barthel para TODOS los pacientes del tenant: por cada paciente toma su
    /// ultima escala Barthel cerrada y la vuelca. Devuelve cuantos pacientes se actualizaron.
    /// </summary>
    Task<int> RecalcularTodosAsync(CancellationToken ct = default);
}

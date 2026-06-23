namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Servicio que actualiza HistoriaClinica.ValoresJson aplicando las rutas de
/// prefill con sourceModule = "historiaMedica" segun los items actuales de la
/// HC (medicamentos, remisiones, incapacidades, certificaciones, ordenes de
/// servicio). Se llama dentro de la MISMA transaccion EF Core del Add/Delete
/// de cualquiera de esos items, asi el cambio se persiste atomicamente sin
/// race con el autosave del frontend.
/// </summary>
public interface IHistoriaPrefillService
{
    Task ActualizarValoresAsync(Guid historiaId, CancellationToken ct = default);
}

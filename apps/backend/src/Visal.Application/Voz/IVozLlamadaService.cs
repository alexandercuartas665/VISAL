namespace Visal.Application.Voz;

/// <summary>Estado resumido de la llamada de un paciente/tarjeta (para la UI de Seguimiento).</summary>
public sealed record LlamadaVozDto(
    Guid Id, Guid? SeguimientoEncuestaId, Guid PacienteId,
    string? CallId, string Estado, string? Error, DateTimeOffset CreadoEn);

/// <summary>Resumen de un disparo en lote.</summary>
public sealed record VozLoteResult(
    int Lanzadas, int Omitidas, int Errores, bool DryRun, IReadOnlyList<string> Mensajes);

public interface IVozLlamadaService
{
    /// <summary>
    /// Lanza una llamada IA a cada encuesta SIAU en estado Pendiente cuyo Mes cae en
    /// [desdeMes, hastaMes] (YYYYMM) y tenga telefono valido. Salta las que ya tienen
    /// una llamada activa. Si <paramref name="dryRun"/> es true, valida y cuenta sin llamar.
    /// </summary>
    Task<VozLoteResult> LlamarPendientesAsync(int desdeMes, int hastaMes, bool dryRun, Guid actor, CancellationToken ct = default);

    /// <summary>Procesa un evento de webhook ya parseado: actualiza la LlamadaVoz y la
    /// tarjeta de Seguimiento. Idempotente por call_id + evento.</summary>
    Task ProcesarWebhookEventoAsync(RetellWebhookEvento evento, CancellationToken ct = default);

    /// <summary>Estado de las llamadas de un mes, indexable por SeguimientoEncuestaId.</summary>
    Task<IReadOnlyList<LlamadaVozDto>> ListarPorMesAsync(int mes, CancellationToken ct = default);
}

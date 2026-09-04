namespace Visal.Application.Voz;

/// <summary>Estado resumido de la llamada de un paciente/tarjeta (para la UI de Seguimiento).</summary>
public sealed record LlamadaVozDto(
    Guid Id, Guid? SeguimientoEncuestaId, Guid PacienteId,
    string? CallId, string Estado, string? Error, DateTimeOffset CreadoEn);

/// <summary>Resumen de un disparo en lote.</summary>
public sealed record VozLoteResult(
    int Lanzadas, int Omitidas, int Errores, bool DryRun, IReadOnlyList<string> Mensajes);

/// <summary>Resultado de lanzar UNA llamada (individual o de prueba).</summary>
public sealed record VozLlamadaAccionResult(bool Ok, string? Error, string? CallId);

/// <summary>Detalle de una llamada para la UI (grabacion, transcripcion, etc.).</summary>
public sealed record LlamadaDetalleDto(
    Guid Id, string? CallId, string Estado, string? Error,
    string? ToNumber, int? DuracionSegundos, decimal? CostoUsd,
    string? RecordingUrl, string? Transcripcion, DateTimeOffset CreadoEn, bool EsPrueba);

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

    /// <summary>Lanza UNA llamada IA para la encuesta indicada. Si <paramref name="telefonoOverride"/>
    /// trae un numero, se llama a ese (para pruebas) en vez del telefono del paciente y la llamada
    /// se marca como prueba.</summary>
    Task<VozLlamadaAccionResult> LlamarUnaAsync(Guid encuestaId, string? telefonoOverride, Guid actor, CancellationToken ct = default);

    /// <summary>Lanza una llamada de PRUEBA a un numero arbitrario (desde la config de Voz IA).
    /// No se asocia a ninguna encuesta.</summary>
    Task<VozLlamadaAccionResult> LlamarPruebaAsync(string telefono, string? nombre, Guid actor, CancellationToken ct = default);

    /// <summary>Ultima llamada (con grabacion/transcripcion) de una encuesta, para mostrar en la UI.</summary>
    Task<LlamadaDetalleDto?> ObtenerUltimaLlamadaAsync(Guid encuestaId, CancellationToken ct = default);
}

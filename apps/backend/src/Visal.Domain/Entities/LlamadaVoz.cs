using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Registro de una llamada de voz IA (Retell sobre Telnyx) lanzada desde el
/// modulo Seguimiento (encuesta SIAU). Guarda el ciclo de vida, el resultado y
/// el analisis post-llamada. Tenant-scoped. Se correlaciona con Retell por
/// <see cref="CallId"/> (unico); el webhook la busca por ese id.
/// </summary>
public class LlamadaVoz : TenantEntity
{
    /// <summary>Encuesta de Seguimiento que origino la llamada (si aplica).</summary>
    public Guid? SeguimientoEncuestaId { get; set; }

    public Guid PacienteId { get; set; }

    /// <summary>call_id que devuelve Retell. Vacio si el intento fallo antes de crearse.</summary>
    public string? CallId { get; set; }

    public string FromNumber { get; set; } = null!;
    public string ToNumber { get; set; } = null!;

    public LlamadaVozEstado Estado { get; set; } = LlamadaVozEstado.Registrada;

    public string? AgentId { get; set; }

    /// <summary>Duracion en segundos (del webhook call_ended/analyzed).</summary>
    public int? DuracionSegundos { get; set; }

    /// <summary>Costo estimado en USD (si Retell lo reporta).</summary>
    public decimal? CostoUsd { get; set; }

    public string? Transcripcion { get; set; }

    /// <summary>URL publica de la grabacion de la llamada (recording_url de Retell).</summary>
    public string? RecordingUrl { get; set; }

    /// <summary>Analisis post-llamada crudo (JSON) tal cual lo manda Retell.</summary>
    public string? AnalisisJson { get; set; }

    /// <summary>true si fue una llamada de prueba (desde la config o con numero override).</summary>
    public bool EsPrueba { get; set; }

    public DateTimeOffset? InicioLlamada { get; set; }
    public DateTimeOffset? FinLlamada { get; set; }

    public string? Error { get; set; }
}

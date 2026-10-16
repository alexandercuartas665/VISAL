using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Log de UNA ejecucion del agente REVISOR CLINICO IA sobre una historia clinica:
/// captura QUE recibio (instrucciones + contexto enviado + HC legible campo->contenido),
/// QUE PROCESO hizo (tools ejecutadas con su payload/tokens) y QUE respondio (texto crudo
/// del modelo + veredicto). Permite auditar/entender la tarea del agente. Tenant-scoped.
/// Una fila por corrida (cada iteracion o reintento del agente genera su propio log).
/// Los campos de texto pueden ser grandes (HC completa en JSON) -> columnas text.
/// </summary>
public class RevisionAgenteLog : TenantEntity
{
    public Guid RevisionClinicaId { get; set; }
    public Guid HistoriaClinicaId { get; set; }

    /// <summary>Codigo/nombre del agente (ej. "REVISOR CLINICO IA").</summary>
    public string AgenteCodigo { get; set; } = null!;
    public string? Proveedor { get; set; }
    public string? Modelo { get; set; }

    /// <summary>True si la llamada al modelo fue exitosa y se obtuvo veredicto.</summary>
    public bool Ok { get; set; }
    /// <summary>Mensaje de error cuando Ok=false (fallo del proveedor o parseo).</summary>
    public string? Error { get; set; }

    // ── Veredicto (resumen) ──────────────────────────────────────────────────────
    /// <summary>Aprobado / Rechazado / Neutral (texto del enum).</summary>
    public string? Resultado { get; set; }
    public decimal? Confianza { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }

    /// <summary>Justificacion del veredicto (la 'nota' del modelo). Es el resumen de por que aprobo/rechazo.</summary>
    public string? Nota { get; set; }
    /// <summary>Hallazgos concretos del modelo, como array JSON de strings.</summary>
    public string? HallazgosJson { get; set; }

    // ── Log completo ─────────────────────────────────────────────────────────────
    /// <summary>Instrucciones (system prompt) que recibio el agente.</summary>
    public string? SystemPrompt { get; set; }
    /// <summary>Contexto exacto enviado al modelo (el mensaje user con todos los JSON de las tools).</summary>
    public string? ContextoEnviado { get; set; }
    /// <summary>HC legible: JSON { "label del campo": "contenido" } cruzando valores con el esquema.
    /// Solo para que el humano valide que vio el agente; NO es lo que se le envio (eso va crudo).</summary>
    public string? CamposLegiblesJson { get; set; }
    /// <summary>JSON con las tools ejecutadas: [{ tool, ok, tokens, error, payload }].</summary>
    public string? ToolsJson { get; set; }
    /// <summary>Respuesta cruda del modelo (antes de parsear el veredicto).</summary>
    public string? RespuestaCruda { get; set; }
}

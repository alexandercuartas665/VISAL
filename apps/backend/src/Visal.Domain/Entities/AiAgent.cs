using Visal.Domain.Common;
using Visal.Domain.Enums;

namespace Visal.Domain.Entities;

/// <summary>
/// Agente de IA configurable del tenant (capa 3). Entidad TENANT-SCOPED. Define proveedor,
/// modelo, prompt de sistema y si esta en produccion. Los recursos (AiAgentResource) son los
/// archivos/datos que el agente puede usar para responder al cliente.
/// </summary>
public class AiAgent : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Rol/tipo descriptivo (copiloto, clasificador, seguimiento, etc.). Libre.</summary>
    public string? Role { get; set; }

    public AiProvider Provider { get; set; } = AiProvider.Claude;

    /// <summary>Modelo concreto del proveedor (opcional; si vacio se usa el por defecto).</summary>
    public string? Model { get; set; }

    public string SystemPrompt { get; set; } = "";

    /// <summary>En produccion (encendido) o apagado.</summary>
    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Allow-list de tools MCP que el agente puede invocar, en CSV. Cuando el
    /// agente es <c>REVISOR CLINICO IA</c> (Capa 08 Ola 6), el orquestador filtra
    /// la lista de tools contra este valor. <c>null</c> o vacio = todas las
    /// tools estandar del agente (fallback conservador). Ver
    /// <c>Visal.Application.Revision.Ia.RevisionMcpToolNames</c>.
    /// </summary>
    public string? AllowedToolsCsv { get; set; }
}

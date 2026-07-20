using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>KPIs del dashboard del tablero Kanban (Capa 08 Ola 7 RC7a).</summary>
public sealed record RevisionDashboardDto(
    RevisionDashboardResumenDto Resumen,
    IReadOnlyList<RevisionRechazoPorProfesionalDto> TopRechazosPorProfesional,
    RevisionAdopcionAgenteDto AdopcionAgente,
    IReadOnlyList<RevisionTiempoEnColumnaDto> TiemposMediosPorColumna);

/// <summary>Numeros globales del tenant: totales y ratios.</summary>
public sealed record RevisionDashboardResumenDto(
    int TotalCiclos,
    int CiclosActivos,
    int CiclosTerminales,
    int Aprobadas,
    int Rechazadas,
    int ArchivadasOk,
    int Inactivadas,
    double PorcentajeRechazoGlobal);

/// <summary>Top-N profesionales con mas rechazos absolutos + su tasa.</summary>
public sealed record RevisionRechazoPorProfesionalDto(
    string EspecialistaNombre,
    int TotalCiclos,
    int Rechazados,
    double PorcentajeRechazo);

/// <summary>
/// Ratio de adopcion automatica del veredicto agente (Ola 6 RC6c). Cuenta cuantas
/// aprobaciones tuvieron <c>ActorTipo=Sistema</c> vs total de aprobaciones.
/// </summary>
public sealed record RevisionAdopcionAgenteDto(
    int TotalAprobaciones,
    int AprobacionesAutomaticas,
    double PorcentajeAdopcion);

/// <summary>Tiempo promedio que las HCs pasan en cada columna Kanban.</summary>
public sealed record RevisionTiempoEnColumnaDto(
    RevisionKanbanColumna Columna,
    int Muestras,
    TimeSpan? PromedioPermanencia);

/// <summary>Motor de metricas para el tab Dashboard de /ordenes.</summary>
public interface IRevisionDashboardService
{
    /// <summary>Devuelve todas las metricas del dashboard, aplicando el global tenant filter.</summary>
    Task<RevisionDashboardDto> GetAsync(CancellationToken ct = default);
}

using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Columnas del tablero Kanban de `/ordenes` (Ola 2, iteracion 3 del plan). Cada
/// una corresponde a un rango de <see cref="RevisionEstadoAgregado"/> mas la
/// consideracion de HCs abiertas que aun no entraron al ciclo.
/// </summary>
public enum RevisionKanbanColumna
{
    /// <summary>HC.Estado = Abierta. Todavia no hay revision.</summary>
    Abiertas = 0,

    /// <summary>HC cerrada + revision en <c>SinRevisar</c>, <c>PreRevision</c> o <c>EnRevision</c>.</summary>
    Cerradas = 1,

    /// <summary>Ultimo evento humano es Rechazado. Espera al reenvio del profesional.</summary>
    Rechazadas = 2,

    /// <summary>Ultimo evento humano es Aprobado. Espera al archivado con permiso final.</summary>
    Aprobadas = 3,
}

/// <summary>Card de una HC en el tablero. Minima informacion para el pintado + drag.</summary>
public sealed record RevisionKanbanCardDto(
    Guid HistoriaClinicaId,
    Guid? RevisionId,
    Guid PacienteId,
    string PacienteNombre,
    string PacienteTipoDoc,
    string PacienteDoc,
    string FormatoNombre,
    string? EspecialistaNombre,
    DateTimeOffset? FechaCierre,
    DateTimeOffset FechaApertura,
    RevisionKanbanColumna Columna,
    RevisionEstadoAgregado? EstadoAgregado,
    RevisionResultado? EstadoAgente,
    int Iteracion,
    /// <summary>Motivo del ultimo rechazo — tooltip en cards de Rechazadas.</summary>
    string? UltimoMotivo,
    /// <summary>Resumen del ultimo veredicto del agente — badge/tooltip.</summary>
    string? UltimoResumenAgente,
    DateTimeOffset UltimaAccionEn);

/// <summary>KPIs del header del tablero: totales + tiempos medios + tasa de rechazo.</summary>
public sealed record RevisionKanbanKpisDto(
    int TotalCards,
    int Cerradas,
    int Aprobadas,
    int Rechazadas,
    int Abiertas,
    TimeSpan? TiempoMedioEnCerradas,
    double? PorcentajeRechazadas);

/// <summary>Snapshot completo del tablero listo para pintar.</summary>
public sealed record RevisionKanbanBoardDto(
    IReadOnlyList<RevisionKanbanCardDto> Cards,
    RevisionKanbanKpisDto Kpis);

/// <summary>
/// Filtros del tab Kanban (Ola 7 RC7d). Todos opcionales — omitir un filtro
/// significa "no filtrar por eso". Se aplican con AND. Multi-tenant sigue vivo
/// via global filter, estos filtros se apilan encima.
/// </summary>
public sealed record RevisionKanbanFiltro(
    /// <summary>Nombre exacto del especialista (denormalizado en la HC).</summary>
    string? EspecialistaNombre = null,
    /// <summary>Rango de fecha (por FechaApertura de la HC).</summary>
    DateOnly? FechaDesde = null,
    /// <summary>Rango de fecha (por FechaApertura de la HC).</summary>
    DateOnly? FechaHasta = null);

/// <summary>Item del tab Archivo — HCs con revision en `ArchivadaOk` o `Inactivada`.</summary>
public sealed record RevisionArchivoItemDto(
    Guid HistoriaClinicaId,
    Guid RevisionId,
    Guid PacienteId,
    string PacienteNombre,
    string PacienteTipoDoc,
    string PacienteDoc,
    string FormatoNombre,
    string? EspecialistaNombre,
    RevisionEstadoAgregado Sabor,
    DateTimeOffset FechaArchivo,
    Guid? RevisorUsuarioId,
    string? Motivo,
    int IteracionesTotales);

/// <summary>Filtros para el tab Archivo.</summary>
public sealed record RevisionArchivoFiltro(
    string? PacienteTexto = null,
    RevisionEstadoAgregado? Sabor = null,
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null,
    Guid? RevisorUsuarioId = null);

/// <summary>Comando drag&amp;drop desde la UI.</summary>
public sealed record MoverCardCmd(
    Guid RevisionClinicaId,
    RevisionKanbanColumna ColumnaDestino,
    Guid RevisorUsuarioId,
    string? Motivo,
    string? Nota);

/// <summary>Sabor terminal al archivar. Coincide con las 2 opciones del radio del modal.</summary>
public enum ArchivarSabor
{
    /// <summary>Cierre limpio — la revision quedo aprobada, se saca del Kanban.</summary>
    Ok = 0,

    /// <summary>Baja logica — la HC fue capturada mal, se inactiva. Motivo obligatorio.</summary>
    Inactivar = 1,
}

/// <summary>Comando del boton "Archivar" en cards aprobadas.</summary>
public sealed record ArchivarKanbanCmd(
    Guid RevisionClinicaId,
    ArchivarSabor Sabor,
    Guid RevisorUsuarioId,
    string? Motivo);

/// <summary>
/// Motor del tablero Kanban + tab Archivo. Delega las transiciones reales en
/// <see cref="IRevisionClinicaService"/> — este servicio se limita a componer
/// las cards con datos de HC + Paciente + Formulario y aplicar los filtros de
/// pintado (que cards van a que columna, quien puede mover).
///
/// Multi-tenant obligatorio (aplica por el global query filter del DbContext).
/// </summary>
public interface IRevisionKanbanService
{
    /// <summary>Snapshot completo del tablero para el tab Kanban. Excluye terminales. Filtros opcionales (Ola 7 RC7d).</summary>
    Task<RevisionKanbanBoardDto> GetBoardAsync(RevisionKanbanFiltro? filtro = null, CancellationToken ct = default);

    /// <summary>Ejecuta la transicion pedida por drag&amp;drop. Valida transicion + permisos + motivo.</summary>
    Task MoverCardAsync(MoverCardCmd cmd, bool tienePermisoRevisar, CancellationToken ct = default);

    /// <summary>Archiva (ArchivadaOk) o inactiva la revision aprobada. Requiere permiso final.</summary>
    Task ArchivarAsync(ArchivarKanbanCmd cmd, bool tienePermisoFinal, CancellationToken ct = default);

    /// <summary>Listado del tab Archivo con filtros (paciente, sabor, rango de fechas, revisor).</summary>
    Task<IReadOnlyList<RevisionArchivoItemDto>> GetArchivoAsync(
        RevisionArchivoFiltro filtro, CancellationToken ct = default);

    /// <summary>
    /// Ola 9 RC9b — export streaming del tab Archivo. Devuelve las lineas CSV
    /// una a una (header + N rows) para que el endpoint las escriba directo al
    /// response body sin materializar todo en memoria. Sin cap de 500 rows —
    /// el caller escribe el BOM UTF-8 antes de la primera linea.
    /// </summary>
    IAsyncEnumerable<string> ExportarArchivoCsvLineasAsync(RevisionArchivoFiltro filtro, CancellationToken ct = default);

    /// <summary>Helper del boton "Enviar a revision" en la HC — crea el ciclo si no existe.</summary>
    Task<RevisionClinicaDto> SolicitarSiFaltaAsync(Guid historiaClinicaId, Guid actorUsuarioId, CancellationToken ct = default);
}

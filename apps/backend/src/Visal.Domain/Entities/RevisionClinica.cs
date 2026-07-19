using Visal.Domain.Common;

namespace Visal.Domain.Entities;

/// <summary>
/// Cabecera del ciclo de revision de una historia clinica. Una fila por HC que
/// entro al flujo. Su <see cref="EstadoAgregado"/> es un derivado de la ultima
/// entrada humana relevante en <see cref="RevisionClinicaEvento"/>; se persiste
/// aqui para consultas rapidas (chip en /atencion, filtros en /ordenes) sin scan
/// de la bitacora.
///
/// La revision NO reabre la HC clinicamente: <see cref="HistoriaClinica.Estado"/>
/// sigue mandando sobre lo clinico. Esta capa vive en paralelo.
///
/// Multi-tenant obligatorio. UNIQUE por <see cref="HistoriaClinicaId"/> — una sola
/// revision viva por HC. Nunca se DELETE — los estados terminales
/// (`ArchivadaOk`, `Inactivada`) marcan salida del ciclo.
/// </summary>
public class RevisionClinica : TenantEntity
{
    public Guid HistoriaClinicaId { get; set; }
    public HistoriaClinica? HistoriaClinica { get; set; }

    /// <summary>Estado derivado del ultimo evento humano relevante. Ver <see cref="RevisionEstadoAgregado"/>.</summary>
    public RevisionEstadoAgregado EstadoAgregado { get; set; } = RevisionEstadoAgregado.SinRevisar;

    /// <summary>Estado sugerido por el ultimo evento del agente IA. Null si el agente aun no corrio.</summary>
    public RevisionResultado? EstadoAgente { get; set; }

    /// <summary>Timestamp de la solicitud original (evento tipo <c>SolicitudCreada</c>).</summary>
    public DateTimeOffset SolicitadaEn { get; set; }

    /// <summary>PlatformUserId de quien solicito. Null cuando el trigger fue automatico al cerrar HC.</summary>
    public Guid? SolicitadaPor { get; set; }

    /// <summary>Timestamp del ultimo evento asociado a esta revision. Se actualiza en cada append.</summary>
    public DateTimeOffset UltimaAccionEn { get; set; }

    /// <summary>
    /// Contador de vueltas del ciclo. Empieza en 1 y se incrementa cada vez que un
    /// rechazo dispara una nueva iteracion. Sirve para pintar el chip "Iter 3/N" en
    /// las cards del Kanban.
    /// </summary>
    public int IteracionActual { get; set; } = 1;

    /// <summary>Bitacora append-only. Un registro por evento del ciclo.</summary>
    public List<RevisionClinicaEvento> Eventos { get; set; } = new();
}

/// <summary>
/// Estado agregado de la revision, derivado del ultimo evento humano relevante.
/// El agente IA nunca cambia este enum — su veredicto solo cambia
/// <see cref="RevisionClinica.EstadoAgente"/>.
/// </summary>
public enum RevisionEstadoAgregado
{
    /// <summary>La HC esta cerrada pero no hay <see cref="RevisionClinica"/> todavia. Chip gris.</summary>
    SinRevisar = 0,

    /// <summary>Hay evento de pre-revision del agente pero no ha entrado revisor humano. Chip azul.</summary>
    PreRevision = 1,

    /// <summary>Un revisor humano abrio la HC (evento <c>AsignacionRevisor</c>) pero no ha decidido. Chip amarillo.</summary>
    EnRevision = 2,

    /// <summary>Ultimo evento humano es <c>Aprobado</c>. Chip verde.</summary>
    Aprobada = 3,

    /// <summary>Ultimo evento humano es <c>Rechazado</c>. Chip rojo.</summary>
    Rechazada = 4,

    /// <summary>Revisor con permiso final archivo la HC aprobada. Terminal, sale del Kanban. Chip verde oscuro.</summary>
    ArchivadaOk = 5,

    /// <summary>Baja logica (HC duplicada, error de captura post-cierre). Terminal. Chip negro.</summary>
    Inactivada = 6,
}

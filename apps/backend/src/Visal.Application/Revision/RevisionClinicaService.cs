using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Revision;

/// <summary>
/// Motor del ciclo de revision. Coordinado por transiciones explicitas (ver
/// <see cref="EsTransicionValida"/>) — cualquier accion que no encaje se
/// rechaza con <see cref="InvalidOperationException"/>. La bitacora es la
/// fuente de verdad; la cabecera solo cachea el estado agregado.
///
/// Multi-tenant es implicito por el global query filter del DbContext.
/// </summary>
public sealed class RevisionClinicaService : IRevisionClinicaService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _clock;

    public RevisionClinicaService(IApplicationDbContext db, ITenantContext tenant, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<RevisionClinicaDto> SolicitarAsync(SolicitarRevisionCmd cmd, CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        // Si ya existe una revision viva para la HC (por UNIQUE index), devolverla
        // sin duplicar el evento SolicitudCreada.
        var existente = await _db.RevisionesClinica
            .FirstOrDefaultAsync(x => x.HistoriaClinicaId == cmd.HistoriaClinicaId, ct);
        if (existente is not null)
        {
            return ToDto(existente);
        }

        var now = _clock.GetUtcNow();
        var revision = new RevisionClinica
        {
            TenantId = tenantId,
            HistoriaClinicaId = cmd.HistoriaClinicaId,
            EstadoAgregado = RevisionEstadoAgregado.SinRevisar,
            SolicitadaEn = now,
            SolicitadaPor = cmd.SolicitadaPor,
            UltimaAccionEn = now,
            IteracionActual = 1,
        };
        _db.RevisionesClinica.Add(revision);

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.SolicitudCreada,
            Resultado = RevisionResultado.Neutral,
            ActorTipo = cmd.SolicitadaPor.HasValue ? RevisionActorTipo.Humano : RevisionActorTipo.Sistema,
            ActorUsuarioId = cmd.SolicitadaPor,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto?> GetPorHistoriaAsync(Guid historiaClinicaId, CancellationToken ct = default)
    {
        var r = await _db.RevisionesClinica.AsNoTracking()
            .FirstOrDefaultAsync(x => x.HistoriaClinicaId == historiaClinicaId, ct);
        return r is null ? null : ToDto(r);
    }

    public async Task<IReadOnlyList<RevisionEventoDto>> ListarEventosAsync(Guid revisionClinicaId, CancellationToken ct = default)
    {
        var eventos = await _db.RevisionClinicaEventos.AsNoTracking()
            .Where(x => x.RevisionClinicaId == revisionClinicaId)
            .OrderBy(x => x.OcurridoEn)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return eventos.Select(ToDto).ToList();
    }

    public async Task<RevisionClinicaDto> AsignarRevisorAsync(AsignarRevisorCmd cmd, CancellationToken ct = default)
    {
        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        // La asignacion vuelve el estado a EnRevision. Puede llegar desde SinRevisar
        // (revisor humano toma sin pre-revision) o PreRevision (el humano toma tras el
        // veredicto del agente). Un rechazado tambien puede reasignarse — hasta que
        // el profesional autor haga Reenvio, no aplica.
        if (!EsTransicionValida(revision.EstadoAgregado, RevisionEstadoAgregado.EnRevision))
        {
            throw new InvalidOperationException(
                $"Transicion invalida: no se puede asignar revisor desde {revision.EstadoAgregado}.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = cmd.Automatica ? RevisionTipoEvento.AsignacionAutomatica : RevisionTipoEvento.AsignacionRevisor,
            Resultado = RevisionResultado.Neutral,
            ActorTipo = cmd.Automatica ? RevisionActorTipo.Sistema : RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.Automatica ? null : cmd.RevisorUsuarioId,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        revision.EstadoAgregado = RevisionEstadoAgregado.EnRevision;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> RegistrarVeredictoAgenteAsync(VeredictoAgenteCmd cmd, CancellationToken ct = default)
    {
        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        // El agente puede opinar en cualquier momento antes de un terminal.
        // Nunca cambia EstadoAgregado. Solo actualiza EstadoAgente. Si es la
        // primera opinion sobre un ciclo aun SinRevisar, adelanta el chip a PreRevision.
        if (revision.EstadoAgregado is RevisionEstadoAgregado.ArchivadaOk or RevisionEstadoAgregado.Inactivada)
        {
            throw new InvalidOperationException("Ciclo terminado: el agente no puede opinar sobre revisiones cerradas.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.PreRevisionAgente,
            Resultado = cmd.Resultado,
            ActorTipo = RevisionActorTipo.Agente,
            ActorAgenteCodigo = cmd.AgenteCodigo,
            Nota = cmd.Nota,
            PayloadJson = cmd.PayloadJson,
            OcurridoEn = now,
        });

        revision.EstadoAgente = cmd.Resultado;
        if (revision.EstadoAgregado == RevisionEstadoAgregado.SinRevisar)
        {
            revision.EstadoAgregado = RevisionEstadoAgregado.PreRevision;
        }
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> AprobarAsync(AprobarCmd cmd, CancellationToken ct = default)
    {
        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        if (!EsTransicionValida(revision.EstadoAgregado, RevisionEstadoAgregado.Aprobada))
        {
            throw new InvalidOperationException(
                $"Transicion invalida: no se puede aprobar desde {revision.EstadoAgregado}.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.Aprobado,
            Resultado = RevisionResultado.Aprobado,
            ActorTipo = RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.RevisorUsuarioId,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        revision.EstadoAgregado = RevisionEstadoAgregado.Aprobada;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> RechazarAsync(RechazarCmd cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Motivo))
        {
            throw new ArgumentException("El motivo es obligatorio para rechazar.", nameof(cmd.Motivo));
        }

        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        if (!EsTransicionValida(revision.EstadoAgregado, RevisionEstadoAgregado.Rechazada))
        {
            throw new InvalidOperationException(
                $"Transicion invalida: no se puede rechazar desde {revision.EstadoAgregado}.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.Rechazado,
            Resultado = RevisionResultado.Rechazado,
            ActorTipo = RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.RevisorUsuarioId,
            Motivo = cmd.Motivo,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        revision.EstadoAgregado = RevisionEstadoAgregado.Rechazada;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> ReenviarAsync(ReenviarCmd cmd, CancellationToken ct = default)
    {
        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        if (revision.EstadoAgregado != RevisionEstadoAgregado.Rechazada)
        {
            throw new InvalidOperationException(
                $"Transicion invalida: solo se puede reenviar desde Rechazada (esta en {revision.EstadoAgregado}).");
        }

        revision.IteracionActual += 1;

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.Reenvio,
            Resultado = RevisionResultado.Neutral,
            ActorTipo = RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.ProfesionalUsuarioId,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        // Vuelve al inicio del ciclo — sin revisar en la iteracion nueva.
        revision.EstadoAgregado = RevisionEstadoAgregado.SinRevisar;
        // El agente debe re-opinar sobre la iteracion nueva; limpiar su veredicto anterior.
        revision.EstadoAgente = null;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> ArchivarOkAsync(ArchivarOkCmd cmd, bool tienePermisoFinal, CancellationToken ct = default)
    {
        if (!tienePermisoFinal)
        {
            throw new UnauthorizedAccessException(
                "Se requiere permiso historias.revisar.aprobar_final para archivar.");
        }

        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        if (!EsTransicionValida(revision.EstadoAgregado, RevisionEstadoAgregado.ArchivadaOk))
        {
            throw new InvalidOperationException(
                $"Transicion invalida: no se puede archivar desde {revision.EstadoAgregado}.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.ArchivadoOk,
            Resultado = RevisionResultado.Neutral,
            ActorTipo = RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.RevisorUsuarioId,
            Nota = cmd.Nota,
            OcurridoEn = now,
        });

        revision.EstadoAgregado = RevisionEstadoAgregado.ArchivadaOk;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    public async Task<RevisionClinicaDto> InactivarAsync(InactivarCmd cmd, bool tienePermisoFinal, CancellationToken ct = default)
    {
        if (!tienePermisoFinal)
        {
            throw new UnauthorizedAccessException(
                "Se requiere permiso historias.revisar.aprobar_final para inactivar.");
        }
        if (string.IsNullOrWhiteSpace(cmd.Motivo))
        {
            throw new ArgumentException("El motivo es obligatorio para inactivar.", nameof(cmd.Motivo));
        }

        var (revision, tenantId, now) = await LoadForMutationAsync(cmd.RevisionClinicaId, ct);

        if (!EsTransicionValida(revision.EstadoAgregado, RevisionEstadoAgregado.Inactivada))
        {
            throw new InvalidOperationException(
                $"Transicion invalida: no se puede inactivar desde {revision.EstadoAgregado}.");
        }

        AppendEvento(revision, new RevisionClinicaEvento
        {
            TenantId = tenantId,
            Tipo = RevisionTipoEvento.Inactivacion,
            Resultado = RevisionResultado.Neutral,
            ActorTipo = RevisionActorTipo.Humano,
            ActorUsuarioId = cmd.RevisorUsuarioId,
            Motivo = cmd.Motivo,
            OcurridoEn = now,
        });

        revision.EstadoAgregado = RevisionEstadoAgregado.Inactivada;
        revision.UltimaAccionEn = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(revision);
    }

    // ---- Internos ----

    /// <summary>
    /// Tabla explicita de transiciones validas del estado agregado.
    /// Consultar §2.b del [[2. Modelo de dominio (revision + bitacora)]].
    /// </summary>
    public static bool EsTransicionValida(RevisionEstadoAgregado from, RevisionEstadoAgregado to)
    {
        // Terminales no dejan salir.
        if (from is RevisionEstadoAgregado.ArchivadaOk or RevisionEstadoAgregado.Inactivada)
        {
            return false;
        }

        return (from, to) switch
        {
            // Un revisor puede entrar en cualquier momento del ciclo pre-terminal.
            (RevisionEstadoAgregado.SinRevisar, RevisionEstadoAgregado.EnRevision) => true,
            (RevisionEstadoAgregado.PreRevision, RevisionEstadoAgregado.EnRevision) => true,
            (RevisionEstadoAgregado.Rechazada, RevisionEstadoAgregado.EnRevision) => true,
            (RevisionEstadoAgregado.EnRevision, RevisionEstadoAgregado.EnRevision) => true,

            // Aprobar / Rechazar solo desde EnRevision.
            (RevisionEstadoAgregado.EnRevision, RevisionEstadoAgregado.Aprobada) => true,
            (RevisionEstadoAgregado.EnRevision, RevisionEstadoAgregado.Rechazada) => true,

            // Terminales solo desde Aprobada (archivar) o cualquier no-terminal (inactivar).
            (RevisionEstadoAgregado.Aprobada, RevisionEstadoAgregado.ArchivadaOk) => true,
            (_, RevisionEstadoAgregado.Inactivada) => true,

            _ => false,
        };
    }

    private Guid RequireTenant()
    {
        if (_tenant.TenantId is not { } t)
        {
            throw new InvalidOperationException("Se requiere TenantId en el contexto para operar el ciclo de revision.");
        }
        return t;
    }

    private async Task<(RevisionClinica revision, Guid tenantId, DateTimeOffset now)> LoadForMutationAsync(
        Guid revisionId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var revision = await _db.RevisionesClinica.FirstOrDefaultAsync(x => x.Id == revisionId, ct)
            ?? throw new InvalidOperationException($"Revision {revisionId} no existe o pertenece a otro tenant.");
        return (revision, tenantId, _clock.GetUtcNow());
    }

    private void AppendEvento(RevisionClinica revision, RevisionClinicaEvento evento)
    {
        evento.RevisionClinicaId = revision.Id;
        evento.Iteracion = revision.IteracionActual;
        _db.RevisionClinicaEventos.Add(evento);
    }

    private static RevisionClinicaDto ToDto(RevisionClinica r) => new(
        r.Id, r.TenantId, r.HistoriaClinicaId, r.EstadoAgregado, r.EstadoAgente,
        r.SolicitadaEn, r.SolicitadaPor, r.UltimaAccionEn, r.IteracionActual);

    private static RevisionEventoDto ToDto(RevisionClinicaEvento e) => new(
        e.Id, e.RevisionClinicaId, e.Tipo, e.Resultado, e.ActorTipo, e.ActorUsuarioId,
        e.ActorAgenteCodigo, e.Iteracion, e.Motivo, e.Nota, e.PayloadJson, e.OcurridoEn);
}

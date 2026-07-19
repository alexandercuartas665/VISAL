using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Tests del motor del ciclo de revision. Cubren:
///   - Aislamiento tenant (3 tests).
///   - Validador de transiciones (1 test bloqueando invalido).
///   - Gate del permiso final para archivar (1 test rechazando).
///   - Ciclo happy path completo + bitacora + iteracion tras rechazo/reenvio.
/// Backend: EF Core InMemory. El global query filter por TenantId aplica igual
/// que en Postgres.
/// </summary>
public sealed class RevisionClinicaServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RevisorA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ProfesionalA = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

    private static (VisalDbContext ctx, RevisionClinicaService svc, Guid hcId)
        Setup(Guid tenantId, string? dbName = null)
    {
        var name = dbName ?? Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var tenant = new FakeTenantContext { TenantId = tenantId, UserId = RevisorA };
        var ctx = new VisalDbContext(opts, tenant);
        var clock = new FakeTimeProvider(FixedNow);
        var svc = new RevisionClinicaService(ctx, tenant, clock);

        // Sembrar una HC minima en el mismo tenant para satisfacer la FK.
        var hc = new HistoriaClinica
        {
            TenantId = tenantId,
            PacienteId = Guid.NewGuid(),
            FormDefinitionId = Guid.NewGuid(),
            ProfesionalId = ProfesionalA,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = FixedNow.AddDays(-1),
            FechaCierre = FixedNow,
        };
        ctx.HistoriasClinicas.Add(hc);
        ctx.SaveChanges();
        return (ctx, svc, hc.Id);
    }

    // ---- Aislamiento tenant ----

    [Fact]
    public async Task Solicitar_EnTenantA_NoEsVisibleDesdeTenantB()
    {
        var db = Guid.NewGuid().ToString();
        var (ctxA, svcA, hcA) = Setup(TenantA, db);
        await svcA.SolicitarAsync(new SolicitarRevisionCmd(hcA, RevisorA, null));

        // El mismo DB InMemory pero visto desde otro tenant: filter global oculta.
        var (ctxB, svcB, _) = Setup(TenantB, db);
        var vistaB = await svcB.GetPorHistoriaAsync(hcA);
        Assert.Null(vistaB);
    }

    [Fact]
    public async Task ListarEventos_SoloTraeEventosDelTenantActual()
    {
        var db = Guid.NewGuid().ToString();
        var (_, svcA, hcA) = Setup(TenantA, db);
        var solicitada = await svcA.SolicitarAsync(new SolicitarRevisionCmd(hcA, RevisorA, null));

        var (_, svcB, _) = Setup(TenantB, db);
        var eventosB = await svcB.ListarEventosAsync(solicitada.Id);
        Assert.Empty(eventosB);
    }

    [Fact]
    public async Task Aprobar_SiendoTenantAjeno_LanzaPorRevisionInexistente()
    {
        var db = Guid.NewGuid().ToString();
        var (_, svcA, hcA) = Setup(TenantA, db);
        var revA = await svcA.SolicitarAsync(new SolicitarRevisionCmd(hcA, RevisorA, null));
        await svcA.AsignarRevisorAsync(new AsignarRevisorCmd(revA.Id, RevisorA, false, null));

        var (_, svcB, _) = Setup(TenantB, db);
        // El global filter deja LoadForMutation viendo null → InvalidOperation.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svcB.AprobarAsync(new AprobarCmd(revA.Id, RevisorA, null)));
    }

    // ---- Validador de transiciones ----

    [Fact]
    public async Task Aprobar_SinAsignacionPrevia_EsRechazadaComoTransicionInvalida()
    {
        var (_, svc, hcId) = Setup(TenantA);
        var rev = await svc.SolicitarAsync(new SolicitarRevisionCmd(hcId, RevisorA, null));
        // Estado = SinRevisar. Aprobar requiere venir de EnRevision.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, null)));
        Assert.Contains("Transicion invalida", ex.Message);
    }

    // ---- Gate del permiso final ----

    [Fact]
    public async Task Archivar_SinPermisoFinal_LanzaUnauthorized()
    {
        var (_, svc, hcId) = Setup(TenantA);
        var rev = await svc.SolicitarAsync(new SolicitarRevisionCmd(hcId, RevisorA, null));
        await svc.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await svc.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.ArchivarOkAsync(new ArchivarOkCmd(rev.Id, RevisorA, null), tienePermisoFinal: false));
    }

    // ---- Happy path completo + bitacora + iteracion ----

    [Fact]
    public async Task CicloCompleto_RechazoReenvioAprobacionArchivo_MantieneBitacoraYAvanzaIteracion()
    {
        var (_, svc, hcId) = Setup(TenantA);

        var rev = await svc.SolicitarAsync(new SolicitarRevisionCmd(hcId, RevisorA, "arranque"));
        Assert.Equal(RevisionEstadoAgregado.SinRevisar, rev.EstadoAgregado);
        Assert.Equal(1, rev.IteracionActual);

        // Iter 1: agente opina neutral -> humano toma -> rechaza.
        await svc.RegistrarVeredictoAgenteAsync(new VeredictoAgenteCmd(
            rev.Id, "agent-review-hc-v1", RevisionResultado.Neutral, "sin banderas rojas", null));
        await svc.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        var rechazada = await svc.RechazarAsync(new RechazarCmd(rev.Id, RevisorA, "falta consentimiento", null));
        Assert.Equal(RevisionEstadoAgregado.Rechazada, rechazada.EstadoAgregado);

        // Reenvio: incrementa iteracion, limpia estado agente.
        var reenviada = await svc.ReenviarAsync(new ReenviarCmd(rev.Id, ProfesionalA, "consentimiento agregado"));
        Assert.Equal(2, reenviada.IteracionActual);
        Assert.Equal(RevisionEstadoAgregado.SinRevisar, reenviada.EstadoAgregado);
        Assert.Null(reenviada.EstadoAgente);

        // Iter 2: humano toma y aprueba.
        await svc.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        var aprobada = await svc.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, "todo ok"));
        Assert.Equal(RevisionEstadoAgregado.Aprobada, aprobada.EstadoAgregado);

        // Archivar con permiso final.
        var archivada = await svc.ArchivarOkAsync(
            new ArchivarOkCmd(rev.Id, RevisorA, "cerrada por revisor"),
            tienePermisoFinal: true);
        Assert.Equal(RevisionEstadoAgregado.ArchivadaOk, archivada.EstadoAgregado);

        // Bitacora completa: 8 eventos apend-only en orden.
        var eventos = await svc.ListarEventosAsync(rev.Id);
        Assert.Collection(eventos,
            e => Assert.Equal(RevisionTipoEvento.SolicitudCreada, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.PreRevisionAgente, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.AsignacionRevisor, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.Rechazado, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.Reenvio, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.AsignacionRevisor, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.Aprobado, e.Tipo),
            e => Assert.Equal(RevisionTipoEvento.ArchivadoOk, e.Tipo));

        // Iteracion queda copiada en cada evento — el reenvio marca la frontera.
        var iterPorTipo = eventos
            .GroupBy(e => e.Tipo)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Iteracion).ToArray());
        Assert.Equal(new[] { 1 }, iterPorTipo[RevisionTipoEvento.Rechazado]);
        Assert.Equal(new[] { 2 }, iterPorTipo[RevisionTipoEvento.Aprobado]);
    }

    [Fact]
    public void EsTransicionValida_TerminalesNoSalen()
    {
        Assert.False(RevisionClinicaService.EsTransicionValida(
            RevisionEstadoAgregado.ArchivadaOk, RevisionEstadoAgregado.EnRevision));
        Assert.False(RevisionClinicaService.EsTransicionValida(
            RevisionEstadoAgregado.Inactivada, RevisionEstadoAgregado.EnRevision));
    }

    [Fact]
    public async Task Rechazar_SinMotivo_LanzaArgumentException()
    {
        var (_, svc, hcId) = Setup(TenantA);
        var rev = await svc.SolicitarAsync(new SolicitarRevisionCmd(hcId, RevisorA, null));
        await svc.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RechazarAsync(new RechazarCmd(rev.Id, RevisorA, "   ", null)));
    }

    // ---- Fakes ----

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow()
        {
            // Avanza 1 segundo por cada lectura para que la bitacora quede
            // en orden estable dentro de un test (los eventos aparecen consecutivos).
            var v = _now;
            _now = _now.AddSeconds(1);
            return v;
        }
    }
}

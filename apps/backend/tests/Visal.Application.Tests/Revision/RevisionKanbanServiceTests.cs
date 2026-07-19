using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Tests del tablero Kanban de /ordenes (Ola 2). Verifican:
///   - Clasificacion correcta de HCs en columnas segun estado agregado.
///   - Gate de permisos en drag&amp;drop (mover requiere historias.revisar,
///     archivar requiere historias.revisar.aprobar_final).
///   - Rechazar exige motivo.
///   - Tab Archivo solo trae terminales.
///   - Aislamiento tenant en el board y en el archivo.
/// </summary>
public sealed class RevisionKanbanServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RevisorA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static (VisalDbContext ctx, IRevisionKanbanService kanban, IRevisionClinicaService revisiones, Guid hcId)
        Setup(Guid tenantId, HistoriaClinicaEstado estadoHc = HistoriaClinicaEstado.Cerrada, string? dbName = null)
    {
        var name = dbName ?? Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var tenant = new FakeTenantContext { TenantId = tenantId, UserId = RevisorA };
        var ctx = new VisalDbContext(opts, tenant);
        var clock = new FakeTimeProvider(FixedNow);
        var revisiones = new RevisionClinicaService(ctx, tenant, clock);
        var kanban = new RevisionKanbanService(ctx, revisiones, clock);

        // Sembrar Paciente + FormDefinition + HC minimos.
        var paciente = new Paciente
        {
            TenantId = tenantId,
            NombreCompleto = "PACIENTE PRUEBA",
            TipoDocumento = "CC",
            NumeroDocumento = "1000",
        };
        var form = new FormDefinition
        {
            TenantId = tenantId,
            Nombre = "HC-FO-TEST",
            Codigo = "HC-FO-TEST",
        };
        var hc = new HistoriaClinica
        {
            TenantId = tenantId,
            PacienteId = paciente.Id,
            FormDefinitionId = form.Id,
            Estado = estadoHc,
            FechaApertura = FixedNow.AddDays(-2),
            FechaCierre = estadoHc == HistoriaClinicaEstado.Cerrada ? FixedNow.AddDays(-1) : null,
            EspecialistaNombre = "DR. TEST",
        };
        ctx.Pacientes.Add(paciente);
        ctx.FormDefinitions.Add(form);
        ctx.HistoriasClinicas.Add(hc);
        ctx.SaveChanges();
        return (ctx, kanban, revisiones, hc.Id);
    }

    // ---- Clasificacion de columnas ----

    [Fact]
    public void MapearColumna_HcAbierta_SinRevision_VaAAbiertas()
    {
        var col = RevisionKanbanService.MapearColumna(HistoriaClinicaEstado.Abierta, null);
        Assert.Equal(RevisionKanbanColumna.Abiertas, col);
    }

    [Fact]
    public void MapearColumna_HcCerrada_SinRevision_VaACerradas()
    {
        var col = RevisionKanbanService.MapearColumna(HistoriaClinicaEstado.Cerrada, null);
        Assert.Equal(RevisionKanbanColumna.Cerradas, col);
    }

    [Fact]
    public void MapearColumna_EnRevision_VaACerradas()
    {
        var col = RevisionKanbanService.MapearColumna(
            HistoriaClinicaEstado.Cerrada, RevisionEstadoAgregado.EnRevision);
        Assert.Equal(RevisionKanbanColumna.Cerradas, col);
    }

    [Fact]
    public void MapearColumna_Rechazada_VaARechazadas()
    {
        var col = RevisionKanbanService.MapearColumna(
            HistoriaClinicaEstado.Cerrada, RevisionEstadoAgregado.Rechazada);
        Assert.Equal(RevisionKanbanColumna.Rechazadas, col);
    }

    [Fact]
    public void MapearColumna_Aprobada_VaAAprobadas()
    {
        var col = RevisionKanbanService.MapearColumna(
            HistoriaClinicaEstado.Cerrada, RevisionEstadoAgregado.Aprobada);
        Assert.Equal(RevisionKanbanColumna.Aprobadas, col);
    }

    // ---- Board completo ----

    [Fact]
    public async Task GetBoard_HcCerradaSinRevision_ApareceEnCerradas()
    {
        var (_, kanban, _, _) = Setup(TenantA);
        var board = await kanban.GetBoardAsync();
        Assert.Single(board.Cards);
        Assert.Equal(RevisionKanbanColumna.Cerradas, board.Cards[0].Columna);
        Assert.Null(board.Cards[0].RevisionId);
    }

    [Fact]
    public async Task GetBoard_HcConRevisionAprobada_TerminalArchivadaNoAparece()
    {
        var (_, kanban, revisiones, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);
        await revisiones.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await revisiones.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, "ok"));
        await revisiones.ArchivarOkAsync(new ArchivarOkCmd(rev.Id, RevisorA, null), tienePermisoFinal: true);

        var board = await kanban.GetBoardAsync();
        // ArchivadaOk es terminal → sale del tablero.
        Assert.Empty(board.Cards);
    }

    [Fact]
    public async Task GetBoard_HcRechazada_ApareceEnRechazadasConMotivo()
    {
        var (_, kanban, revisiones, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);
        await revisiones.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await revisiones.RechazarAsync(new RechazarCmd(rev.Id, RevisorA, "falta consentimiento", null));

        var board = await kanban.GetBoardAsync();
        var card = Assert.Single(board.Cards);
        Assert.Equal(RevisionKanbanColumna.Rechazadas, card.Columna);
        Assert.Equal("falta consentimiento", card.UltimoMotivo);
    }

    // ---- Drag&drop ----

    [Fact]
    public async Task MoverCard_SinPermiso_LanzaUnauthorized()
    {
        var (_, kanban, _, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            kanban.MoverCardAsync(
                new MoverCardCmd(rev.Id, RevisionKanbanColumna.Aprobadas, RevisorA, null, null),
                tienePermisoRevisar: false));
    }

    [Fact]
    public async Task MoverCard_DesdeCerradasAAprobadas_AsignaYAprueba()
    {
        var (_, kanban, revisiones, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);

        await kanban.MoverCardAsync(
            new MoverCardCmd(rev.Id, RevisionKanbanColumna.Aprobadas, RevisorA, null, "todo ok"),
            tienePermisoRevisar: true);

        var actualizada = await revisiones.GetPorHistoriaAsync(hcId);
        Assert.NotNull(actualizada);
        Assert.Equal(RevisionEstadoAgregado.Aprobada, actualizada!.EstadoAgregado);
    }

    [Fact]
    public async Task MoverCard_ARechazadas_SinMotivo_LanzaArgumentException()
    {
        var (_, kanban, _, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            kanban.MoverCardAsync(
                new MoverCardCmd(rev.Id, RevisionKanbanColumna.Rechazadas, RevisorA, null, null),
                tienePermisoRevisar: true));
    }

    [Fact]
    public async Task MoverCard_ARechazadasConMotivo_RegistraEvento()
    {
        var (_, kanban, revisiones, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);

        await kanban.MoverCardAsync(
            new MoverCardCmd(rev.Id, RevisionKanbanColumna.Rechazadas, RevisorA, "sin firmas", null),
            tienePermisoRevisar: true);

        var actualizada = await revisiones.GetPorHistoriaAsync(hcId);
        Assert.Equal(RevisionEstadoAgregado.Rechazada, actualizada!.EstadoAgregado);
        var eventos = await revisiones.ListarEventosAsync(rev.Id);
        Assert.Contains(eventos, e => e.Tipo == RevisionTipoEvento.Rechazado && e.Motivo == "sin firmas");
    }

    // ---- Archivar ----

    [Fact]
    public async Task Archivar_SinPermisoFinal_LanzaUnauthorized()
    {
        var (_, kanban, _, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);
        await kanban.MoverCardAsync(
            new MoverCardCmd(rev.Id, RevisionKanbanColumna.Aprobadas, RevisorA, null, null),
            tienePermisoRevisar: true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            kanban.ArchivarAsync(
                new ArchivarKanbanCmd(rev.Id, ArchivarSabor.Ok, RevisorA, null),
                tienePermisoFinal: false));
    }

    [Fact]
    public async Task Archivar_Inactivar_SinMotivo_LanzaArgumentException()
    {
        var (_, kanban, _, hcId) = Setup(TenantA);
        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            kanban.ArchivarAsync(
                new ArchivarKanbanCmd(rev.Id, ArchivarSabor.Inactivar, RevisorA, null),
                tienePermisoFinal: true));
    }

    // ---- Tab Archivo ----

    [Fact]
    public async Task GetArchivo_TraeSoloTerminales()
    {
        var db = Guid.NewGuid().ToString();

        // HC1 aprobada + archivada.
        var (ctx1, kanban1, revisiones1, hc1) = Setup(TenantA, dbName: db);
        var r1 = await kanban1.SolicitarSiFaltaAsync(hc1, RevisorA);
        await kanban1.MoverCardAsync(
            new MoverCardCmd(r1.Id, RevisionKanbanColumna.Aprobadas, RevisorA, null, null),
            tienePermisoRevisar: true);
        await kanban1.ArchivarAsync(
            new ArchivarKanbanCmd(r1.Id, ArchivarSabor.Ok, RevisorA, null),
            tienePermisoFinal: true);

        // HC2 rechazada — NO debe aparecer en el archivo (no es terminal).
        var paciente2 = new Paciente
        {
            TenantId = TenantA,
            NombreCompleto = "OTRO PACIENTE",
            TipoDocumento = "CC",
            NumeroDocumento = "2000",
        };
        var form2 = new FormDefinition { TenantId = TenantA, Nombre = "HC-FO-2", Codigo = "HC-FO-2" };
        var hc2 = new HistoriaClinica
        {
            TenantId = TenantA,
            PacienteId = paciente2.Id,
            FormDefinitionId = form2.Id,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = FixedNow.AddDays(-2),
            FechaCierre = FixedNow.AddDays(-1),
        };
        ctx1.Pacientes.Add(paciente2);
        ctx1.FormDefinitions.Add(form2);
        ctx1.HistoriasClinicas.Add(hc2);
        await ctx1.SaveChangesAsync();

        var r2 = await kanban1.SolicitarSiFaltaAsync(hc2.Id, RevisorA);
        await kanban1.MoverCardAsync(
            new MoverCardCmd(r2.Id, RevisionKanbanColumna.Rechazadas, RevisorA, "razon", null),
            tienePermisoRevisar: true);

        var archivo = await kanban1.GetArchivoAsync(new RevisionArchivoFiltro());
        Assert.Single(archivo);
        Assert.Equal(RevisionEstadoAgregado.ArchivadaOk, archivo[0].Sabor);
        Assert.Equal(hc1, archivo[0].HistoriaClinicaId);
    }

    // ---- Aislamiento tenant ----

    [Fact]
    public async Task GetBoard_TenantB_NoVeTablaDeTenantA()
    {
        var db = Guid.NewGuid().ToString();
        var (_, kanbanA, _, _) = Setup(TenantA, dbName: db);
        var boardA = await kanbanA.GetBoardAsync();
        Assert.Single(boardA.Cards);

        // Compartir DB, cambiar tenant → global filter oculta la HC de TenantA.
        var (_, kanbanB, _, _) = Setup(TenantB, dbName: db);
        var boardB = await kanbanB.GetBoardAsync();
        // TenantB tiene solo su propia HC (la creada en Setup con TenantB).
        Assert.Single(boardB.Cards);
        // Verificar que las HCs no se cruzan.
        Assert.NotEqual(boardA.Cards[0].HistoriaClinicaId, boardB.Cards[0].HistoriaClinicaId);
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
            var v = _now;
            _now = _now.AddSeconds(1);
            return v;
        }
    }
}

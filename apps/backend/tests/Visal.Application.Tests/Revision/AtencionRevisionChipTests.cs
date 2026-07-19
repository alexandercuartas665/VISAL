using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Tests del chip de revision en /atencion (Capa 08 Ola 3). Verifican que
/// <see cref="AtencionProfesionalService.GetMisServiciosAsync"/> resuelve
/// correctamente el estado del ciclo de revision para la HC mas reciente del
/// paciente:
///   - Sin HC en el paciente -> chip null.
///   - HC sin revision -> chip null.
///   - HC con revision aprobada -> chip Aprobada.
///   - HC con revision rechazada -> chip Rechazada + motivo del ultimo evento.
///   - Tenant B no ve la revision del paciente sembrada en Tenant A.
/// </summary>
public sealed class AtencionRevisionChipTests
{
    private static readonly Guid TenantA = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TenantB = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid RevisorA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class FakeConfigClinica : IConfiguracionClinicaService
    {
        public Task<int> GetMesesValidezHistoriaClinicaAsync(CancellationToken ct = default) => Task.FromResult(3);
        public Task SetMesesValidezHistoriaClinicaAsync(int meses, Guid actor, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> GetBloquearOverloadTurnosAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task SetBloquearOverloadTurnosAsync(bool bloquear, Guid actor, CancellationToken ct = default) => Task.CompletedTask;
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

    /// <summary>
    /// Siembra un tenant con: PlatformUser Owner + TenantUser + Rol Administrador,
    /// 1 Paciente, 1 FormDefinition, 1 Asignacion + 1 AsignacionTurno.
    /// Devuelve DbContext, servicios necesarios, PacienteId y platformUserId.
    /// </summary>
    private static (VisalDbContext ctx, AtencionProfesionalService atencion,
                    RevisionClinicaService revisiones, RevisionKanbanService kanban,
                    Guid platformUserId, Guid pacienteId, Guid formDefinitionId)
        Setup(Guid tenantId, string? dbName = null)
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
        var atencion = new AtencionProfesionalService(ctx, tenant, new FakeConfigClinica());

        // TenantUser Owner + PlatformUser: Owner ve todo, no requiere ProfesionalId.
        var pu = new PlatformUser { Email = $"owner-{tenantId}@test", DisplayName = "OWNER", AuthProvider = "local" };
        ctx.PlatformUsers.Add(pu);
        var tu = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = pu.Id,
            Email = pu.Email,
            TenantRole = TenantRole.Owner,
        };
        ctx.TenantUsers.Add(tu);

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
        ctx.Pacientes.Add(paciente);
        ctx.FormDefinitions.Add(form);

        var asig = new Asignacion
        {
            TenantId = tenantId,
            PacienteId = paciente.Id,
            TipoServicio = "TERAPIA",
            NombreServicio = "TERAPIA FISICA",
            FormatoHistoria = form.Codigo,
            ContratoCodigo = "CT-001",
            ServicioId = "S-001",
            Sucursal = "SEDE-1",
        };
        ctx.Asignaciones.Add(asig);
        var turno = new AsignacionTurno
        {
            TenantId = tenantId,
            AsignacionId = asig.Id,
            ProfesionalId = Guid.Empty,
            Cantidad = 1,
        };
        ctx.AsignacionTurnos.Add(turno);

        ctx.SaveChanges();
        return (ctx, atencion, revisiones, kanban, pu.Id, paciente.Id, form.Id);
    }

    private static async Task<Guid> SembrarHcCerradaAsync(VisalDbContext ctx, Guid tenantId, Guid pacienteId, Guid formDefId)
    {
        var hc = new HistoriaClinica
        {
            TenantId = tenantId,
            PacienteId = pacienteId,
            FormDefinitionId = formDefId,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = FixedNow.AddDays(-2),
            FechaCierre = FixedNow.AddDays(-1),
        };
        ctx.HistoriasClinicas.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    [Fact]
    public async Task GetMisServicios_SinHc_DevuelveChipNulo()
    {
        var (_, atencion, _, _, platformUserId, _, _) = Setup(TenantA);
        var rows = await atencion.GetMisServiciosAsync(platformUserId);
        var row = Assert.Single(rows);
        Assert.Null(row.RevisionEstado);
        Assert.Null(row.RevisionUltimaAccionEn);
        Assert.Null(row.RevisionMotivoRechazo);
    }

    [Fact]
    public async Task GetMisServicios_HcSinRevision_DevuelveChipNulo()
    {
        var (ctx, atencion, _, _, platformUserId, pacienteId, formId) = Setup(TenantA);
        await SembrarHcCerradaAsync(ctx, TenantA, pacienteId, formId);

        var rows = await atencion.GetMisServiciosAsync(platformUserId);
        var row = Assert.Single(rows);
        Assert.Null(row.RevisionEstado);
        Assert.Null(row.RevisionMotivoRechazo);
    }

    [Fact]
    public async Task GetMisServicios_HcConRevisionAprobada_DevuelveChipAprobada()
    {
        var (ctx, atencion, revisiones, kanban, platformUserId, pacienteId, formId) = Setup(TenantA);
        var hcId = await SembrarHcCerradaAsync(ctx, TenantA, pacienteId, formId);

        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);
        await revisiones.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await revisiones.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, "listo"));

        var rows = await atencion.GetMisServiciosAsync(platformUserId);
        var row = Assert.Single(rows);
        Assert.Equal(RevisionEstadoAgregado.Aprobada, row.RevisionEstado);
        Assert.NotNull(row.RevisionUltimaAccionEn);
        Assert.Null(row.RevisionMotivoRechazo);
    }

    [Fact]
    public async Task GetMisServicios_HcConRevisionRechazada_DevuelveChipRojoConMotivo()
    {
        var (ctx, atencion, revisiones, kanban, platformUserId, pacienteId, formId) = Setup(TenantA);
        var hcId = await SembrarHcCerradaAsync(ctx, TenantA, pacienteId, formId);

        var rev = await kanban.SolicitarSiFaltaAsync(hcId, RevisorA);
        await revisiones.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await revisiones.RechazarAsync(new RechazarCmd(rev.Id, RevisorA, "falta firma paciente", null));

        var rows = await atencion.GetMisServiciosAsync(platformUserId);
        var row = Assert.Single(rows);
        Assert.Equal(RevisionEstadoAgregado.Rechazada, row.RevisionEstado);
        Assert.Equal("falta firma paciente", row.RevisionMotivoRechazo);
    }

    [Fact]
    public async Task GetMisServicios_TenantB_NoVeRevisionDeTenantA()
    {
        var db = Guid.NewGuid().ToString();
        var (ctxA, atA, revA, kanbanA, uidA, pacA, formA) = Setup(TenantA, db);
        var hcA = await SembrarHcCerradaAsync(ctxA, TenantA, pacA, formA);
        var rev = await kanbanA.SolicitarSiFaltaAsync(hcA, RevisorA);
        await revA.AsignarRevisorAsync(new AsignarRevisorCmd(rev.Id, RevisorA, false, null));
        await revA.AprobarAsync(new AprobarCmd(rev.Id, RevisorA, null));

        var (_, atB, _, _, uidB, _, _) = Setup(TenantB, db);
        var rows = await atB.GetMisServiciosAsync(uidB);
        var row = Assert.Single(rows);
        // TenantB tiene su propio paciente + turno pero sin revision.
        Assert.Null(row.RevisionEstado);
    }
}

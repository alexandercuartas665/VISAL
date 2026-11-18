using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Patron de cierre de HC por tipo de servicio (por agencia) + exencion de coordinadores +
/// solicitud de autorizacion sin token que aprueba/rechaza el coordinador en /autorizaciones.
/// </summary>
public sealed class PatronCierreHcTests
{
    private static readonly Guid Tenant = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
    // 17/11/2026 15:00 UTC = 10:00 hora Colombia.
    private static readonly DateTimeOffset Ahora = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) { }
        public void Write(IApplicationDbContext db, Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) { }
    }

    private sealed class FakeTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static TestDbContextFactory Factory(VisalDbContext ctx) =>
        new(ctx, new FakeTenantContext { TenantId = Tenant });

    private static HistoriaClinicaService HcSvc(VisalDbContext ctx) =>
        new(new FakeTenantContext { TenantId = Tenant }, new NoopAudit(),
            null!, null!, null!, null!, null!, null!, Factory(ctx), new FakeTime());

    private static AutorizacionTokenService TokenSvc(VisalDbContext ctx) =>
        new(Factory(ctx), new FakeTenantContext { TenantId = Tenant }, new NoopAudit(), new FakeTime());

    /// <summary>HC ligada a una sesion de una asignacion del tipo dado, con fecha de atencion.</summary>
    private static async Task<Guid> SembrarHcAsync(VisalDbContext ctx, string tipoAsig, DateTimeOffset fechaAtencion,
        string codigoCatalogo, string? patron, int? horas = null, int numeroSesion = 2)
    {
        ctx.CatalogosTipoServicio.Add(new CatalogoTipoServicio
        {
            TenantId = Tenant, Codigo = codigoCatalogo, Nombre = codigoCatalogo, Orden = 1, Activo = true,
            PatronCierre = patron, HorasLimiteCierre = horas,
        });
        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = Guid.NewGuid(),
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "SERVICIO", TipoServicio = tipoAsig,
            Cantidad = 1, ContratoCodigo = "C1", MesVigencia = 11, FechaInicio = new DateOnly(2026, 11, 1),
            Estado = AsignacionEstado.Pendiente,
        };
        ctx.Asignaciones.Add(asig);
        var turno = new AsignacionTurno
        {
            TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.NewGuid(), Cantidad = 1,
            NumeroSesion = numeroSesion,
        };
        ctx.AsignacionTurnos.Add(turno);
        var ses = new AsignacionTurnoSesion
        {
            TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = 1, FechaAtencion = new DateOnly(2026, 11, 1),
        };
        ctx.AsignacionTurnoSesiones.Add(ses);
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = asig.PacienteId, FormDefinitionId = Guid.NewGuid(),
            FechaApertura = fechaAtencion, FechaAtencion = fechaAtencion,
        };
        ctx.HistoriasClinicas.Add(hc);
        ctx.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc { TenantId = Tenant, SesionId = ses.Id, HistoriaClinicaId = hc.Id });
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    /// <summary>Usuario del tenant con un rol que tiene (o no) el permiso indicado.</summary>
    private static async Task<Guid> SembrarUsuarioAsync(VisalDbContext ctx, string? permiso, TenantRole tr = TenantRole.Advisor)
    {
        var rol = new Rol { TenantId = Tenant, Nombre = "R" + Guid.NewGuid().ToString("N")[..6], Activo = true };
        ctx.Roles.Add(rol);
        if (permiso is not null)
        {
            ctx.RolPermisos.Add(new RolPermiso { TenantId = Tenant, RolId = rol.Id, Modulo = permiso, Ver = true });
        }
        var uid = Guid.NewGuid();
        ctx.TenantUsers.Add(new TenantUser { TenantId = Tenant, PlatformUserId = uid, Email = $"{uid:N}@x.co", TenantRole = tr, RolId = rol.Id });
        await ctx.SaveChangesAsync();
        return uid;
    }

    [Theory]
    [InlineData(null, null, null, "LIBRE")]
    [InlineData(null, 4, null, "OPORTUNIDAD")]
    [InlineData("mismo_dia", null, null, "MISMO_DIA")]
    [InlineData("INVENTADO", null, 48, "OPORTUNIDAD")]
    public void Patron_efectivo_respeta_configurado_y_legado(string? patron, int? dias, int? horas, string esperado)
        => Assert.Equal(esperado, PatronCierreHc.Efectivo(patron, dias, horas));

    [Fact]
    public async Task Mismo_dia_bloquea_al_dia_siguiente_y_no_el_mismo_dia()
    {
        await using var ctx = NewCtx();
        // Ayer 16/11 20:00 Colombia (= 17/11 01:00 UTC) -> hoy 17/11: fuera del patron.
        var ayer = await SembrarHcAsync(ctx, "ENFERMERIA", new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero), "ENFERMERIA", PatronCierreHc.MismoDia);
        var r1 = await HcSvc(ctx).EvaluarControlFechaAsync(ayer);
        Assert.True(r1.RequiereToken);

        await using var ctx2 = NewCtx();
        // Hoy 17/11 07:00 Colombia -> dentro del patron.
        var hoy = await SembrarHcAsync(ctx2, "ENFERMERIA", new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), "ENFERMERIA", PatronCierreHc.MismoDia);
        Assert.False((await HcSvc(ctx2).EvaluarControlFechaAsync(hoy)).RequiereToken);
    }

    [Fact]
    public async Task Horas_desde_atencion_y_tipo_en_plural_usa_el_catalogo_singular()
    {
        await using var ctx = NewCtx();
        // Asignacion "TERAPIAS", catalogo "TERAPIA" con max 24 h; atencion hace 30 h.
        var hc = await SembrarHcAsync(ctx, "TERAPIAS", Ahora.AddHours(-30), "TERAPIA", PatronCierreHc.HorasAtencion, horas: 24);
        Assert.True((await HcSvc(ctx).EvaluarControlFechaAsync(hc)).RequiereToken);

        await using var ctx2 = NewCtx();
        var hc2 = await SembrarHcAsync(ctx2, "TERAPIAS", Ahora.AddHours(-10), "TERAPIA", PatronCierreHc.HorasAtencion, horas: 24);
        Assert.False((await HcSvc(ctx2).EvaluarControlFechaAsync(hc2)).RequiereToken);
    }

    [Fact]
    public async Task Libre_no_bloquea()
    {
        await using var ctx = NewCtx();
        var hc = await SembrarHcAsync(ctx, "CONSULTA", Ahora.AddDays(-40), "CONSULTA", PatronCierreHc.Libre);
        Assert.False((await HcSvc(ctx).EvaluarControlFechaAsync(hc)).RequiereToken);
    }

    [Fact]
    public async Task Coordinador_con_permiso_queda_exento_y_profesional_no()
    {
        await using var ctx = NewCtx();
        var hc = await SembrarHcAsync(ctx, "ENFERMERIA", Ahora.AddDays(-3), "ENFERMERIA", PatronCierreHc.MismoDia);
        var coord = await SembrarUsuarioAsync(ctx, PermisosUsuario.SinControlCierre);
        var prof = await SembrarUsuarioAsync(ctx, null);
        var admin = await SembrarUsuarioAsync(ctx, null, TenantRole.Admin);

        Assert.False((await HcSvc(ctx).EvaluarControlFechaAsync(hc, coord)).RequiereToken);
        Assert.False((await HcSvc(ctx).EvaluarControlFechaAsync(hc, admin)).RequiereToken);
        Assert.True((await HcSvc(ctx).EvaluarControlFechaAsync(hc, prof)).RequiereToken);
    }

    [Fact]
    public async Task Solicitud_pendiente_aprobada_por_coordinador_autoriza_la_hc()
    {
        await using var ctx = NewCtx();
        var hc = await SembrarHcAsync(ctx, "ENFERMERIA", Ahora.AddDays(-2), "ENFERMERIA", PatronCierreHc.MismoDia);
        var prof = await SembrarUsuarioAsync(ctx, null);
        var coord = await SembrarUsuarioAsync(ctx, PermisosUsuario.Autorizaciones);
        var svc = HcSvc(ctx);

        var res = await svc.SolicitarAutorizacionFechaAsync(hc, "Registro tardio", "Otro", "PROF UNO", prof);
        Assert.True(res.Ok);
        var ctrl = await svc.EvaluarControlFechaAsync(hc, prof);
        Assert.True(ctrl.RequiereToken);
        Assert.Equal("Pendiente", ctrl.SolicitudEstado);

        // No duplica una segunda solicitud pendiente.
        await svc.SolicitarAutorizacionFechaAsync(hc, "otra vez", null, "PROF UNO", prof);
        Assert.Equal(1, await ctx.AutorizacionSolicitudes.CountAsync());

        var tok = TokenSvc(ctx);
        Assert.Equal(1, await tok.ContarSolicitudesPendientesAsync());
        var sol = (await tok.ListarSolicitudesAsync(soloPendientes: true)).Single();

        // Un profesional sin permiso no puede aprobar.
        Assert.False((await tok.AprobarSolicitudAsync(sol.Id, null, "PROF UNO", prof)).Ok);

        Assert.True((await tok.AprobarSolicitudAsync(sol.Id, null, "COORD", coord)).Ok);
        Assert.False((await svc.EvaluarControlFechaAsync(hc, prof)).RequiereToken);
        var token = await ctx.AutorizacionTokens.AsNoTracking().SingleAsync();
        Assert.Equal(AutorizacionTokenEstado.Usado, token.Estado);
        Assert.Equal(hc, token.HistoriaClinicaId);
        Assert.Equal(0, await tok.ContarSolicitudesPendientesAsync());
    }

    [Fact]
    public async Task Solicitud_rechazada_queda_visible_y_sigue_bloqueando()
    {
        await using var ctx = NewCtx();
        var hc = await SembrarHcAsync(ctx, "ENFERMERIA", Ahora.AddDays(-2), "ENFERMERIA", PatronCierreHc.MismoDia);
        var prof = await SembrarUsuarioAsync(ctx, null);
        var coord = await SembrarUsuarioAsync(ctx, PermisosUsuario.Autorizaciones);
        var svc = HcSvc(ctx);
        await svc.SolicitarAutorizacionFechaAsync(hc, "Registro tardio", null, "PROF", prof);
        var tok = TokenSvc(ctx);
        var sol = (await tok.ListarSolicitudesAsync(true)).Single();

        Assert.False((await tok.RechazarSolicitudAsync(sol.Id, "  ", "COORD", coord)).Ok); // motivo obligatorio
        Assert.True((await tok.RechazarSolicitudAsync(sol.Id, "Fecha incorrecta", "COORD", coord)).Ok);

        var ctrl = await svc.EvaluarControlFechaAsync(hc, prof);
        Assert.True(ctrl.RequiereToken);
        Assert.Equal("Rechazada", ctrl.SolicitudEstado);
        Assert.Equal("Fecha incorrecta", ctrl.SolicitudRespuesta);
    }
}

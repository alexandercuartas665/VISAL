using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del validador de orden secuencial (Ola 3). Verifica:
///   - Regla: para atender SessionNo N deben estar Completadas 1..N-1
///     dentro del mismo AsignacionTurno.
///   - Escape por rol Owner/Admin del tenant.
///   - Escape por permiso "atencion.saltar-orden".
///   - Fail-open cuando el usuario no tiene TenantUser (super admin puro
///     o proceso de sistema).
/// </summary>
public sealed class AtencionOrdenServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private static VisalDbContext NewCtx(string? dbName = null)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });
    }

    /// <summary>Siembra el user con el rol dado, un turno con N sesiones donde
    /// las primeras `completadasHasta` estan Completado=true.</summary>
    private static async Task<(Guid userId, Guid turnoId)> SembrarAsync(
        VisalDbContext ctx,
        TenantRole tenantRole,
        int totalSesiones,
        int completadasHasta,
        bool conPermisoSaltarOrden = false)
    {
        var pu = new PlatformUser { Email = "u@test", DisplayName = "U", AuthProvider = "local" };
        ctx.PlatformUsers.Add(pu);

        Guid? rolId = null;
        if (tenantRole == TenantRole.Advisor)
        {
            var rol = new Rol { TenantId = Tenant, Nombre = "Coordinador" };
            ctx.Roles.Add(rol);
            if (conPermisoSaltarOrden)
            {
                ctx.RolPermisos.Add(new RolPermiso
                {
                    TenantId = Tenant, RolId = rol.Id,
                    Modulo = "atencion.saltar-orden",
                    Ver = true, Crear = false, Editar = false, Eliminar = false
                });
            }
            rolId = rol.Id;
        }

        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = Tenant, PlatformUserId = pu.Id, Email = pu.Email,
            TenantRole = tenantRole, RolId = rolId
        });

        var paciente = new Paciente
        {
            TenantId = Tenant, NombreCompleto = "P", TipoDocumento = "CC", NumeroDocumento = "1"
        };
        ctx.Pacientes.Add(paciente);

        var asig = new Asignacion
        {
            TenantId = Tenant, PacienteId = paciente.Id,
            TipoServicio = "TERAPIA", NombreServicio = "T", FormatoHistoria = "HC",
            ContratoCodigo = "C", ServicioId = "S", Sucursal = "SD"
        };
        ctx.Asignaciones.Add(asig);

        var turno = new AsignacionTurno
        {
            TenantId = Tenant, AsignacionId = asig.Id,
            ProfesionalId = Guid.Empty, Cantidad = totalSesiones
        };
        ctx.AsignacionTurnos.Add(turno);

        for (var i = 1; i <= totalSesiones; i++)
        {
            ctx.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
            {
                TenantId = Tenant,
                AsignacionTurnoId = turno.Id,
                SessionNo = i,
                FechaAtencion = DateOnly.FromDateTime(DateTime.UtcNow),
                Completado = i <= completadasHasta
            });
        }

        await ctx.SaveChangesAsync();
        return (pu.Id, turno.Id);
    }

    private static async Task<Guid> SesionIdAsync(VisalDbContext ctx, Guid turnoId, int sessionNo)
    {
        return await ctx.AsignacionTurnoSesiones
            .Where(s => s.AsignacionTurnoId == turnoId && s.SessionNo == sessionNo)
            .Select(s => s.Id).FirstAsync();
    }

    [Fact]
    public async Task SessionNo1_SiemprePasa()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 0);
        var s1 = await SesionIdAsync(ctx, turnoId, 1);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s1, userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Advisor_Sesion2_ConSesion1Pendiente_Bloquea()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 0);
        var s2 = await SesionIdAsync(ctx, turnoId, 2);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s2, userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(1, bloqueo!.SessionNoPendiente);
        Assert.Contains("sesion 1", bloqueo.Motivo);
    }

    [Fact]
    public async Task Advisor_Sesion2_ConSesion1Completada_Pasa()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 1);
        var s2 = await SesionIdAsync(ctx, turnoId, 2);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s2, userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Advisor_Sesion3_ConSesion2Pendiente_BloqueaEnSesion2()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 1);
        var s3 = await SesionIdAsync(ctx, turnoId, 3);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s3, userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(2, bloqueo!.SessionNoPendiente);
    }

    [Fact]
    public async Task Owner_SiemprePasaAunConSesionesPendientes()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Owner, totalSesiones: 3, completadasHasta: 0);
        var s3 = await SesionIdAsync(ctx, turnoId, 3);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s3, userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Admin_SiemprePasa()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Admin, totalSesiones: 3, completadasHasta: 0);
        var s3 = await SesionIdAsync(ctx, turnoId, 3);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s3, userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Advisor_ConPermisoSaltarOrden_Pasa()
    {
        var ctx = NewCtx();
        var (userId, turnoId) = await SembrarAsync(ctx, TenantRole.Advisor,
            totalSesiones: 3, completadasHasta: 0, conPermisoSaltarOrden: true);
        var s3 = await SesionIdAsync(ctx, turnoId, 3);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s3, userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task SinTenantUser_FailOpen()
    {
        var ctx = NewCtx();
        // Sembrado sin usuario: creamos solo el turno con sesiones.
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "P", TipoDocumento = "CC", NumeroDocumento = "1" };
        ctx.Pacientes.Add(paciente);
        var asig = new Asignacion { TenantId = Tenant, PacienteId = paciente.Id, TipoServicio = "T", NombreServicio = "T", FormatoHistoria = "H", ContratoCodigo = "C", ServicioId = "S", Sucursal = "SD" };
        ctx.Asignaciones.Add(asig);
        var turno = new AsignacionTurno { TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.Empty, Cantidad = 2 };
        ctx.AsignacionTurnos.Add(turno);
        for (var i = 1; i <= 2; i++)
        {
            ctx.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
            {
                TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = i,
                FechaAtencion = DateOnly.FromDateTime(DateTime.UtcNow), Completado = false
            });
        }
        await ctx.SaveChangesAsync();

        var s2 = await SesionIdAsync(ctx, turno.Id, 2);
        var userDesconocido = Guid.NewGuid();

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(s2, userDesconocido);

        // Sin TenantUser -> fail-open (super admin o servicio de sistema).
        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task SesionInexistente_DevuelveNull()
    {
        var ctx = NewCtx();
        var (userId, _) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 1, completadasHasta: 0);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(Guid.NewGuid(), userId);

        Assert.Null(bloqueo);
    }
}

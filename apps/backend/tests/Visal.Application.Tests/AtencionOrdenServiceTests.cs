using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del validador de orden secuencial (Ola 3, reforzado en el commit de
/// "bloqueo estricto de orden"). Verifica:
///   - Regla: para atender la sesion en la posicion cronologica N deben estar
///     Completadas las sesiones 1..N-1 de la misma Asignacion.
///   - El "orden de sesion" vive en la posicion cronologica del AsignacionTurno
///     dentro de su Asignacion (una fila por turno individual, pivote SessionNo=1),
///     NO en AsignacionTurnoSesion.SessionNo.
///   - Sin exencion por TenantRole: Owner/Admin/Advisor se bloquean por igual.
///     Solo releva la regla el permiso explicito "atencion.saltar-orden".
///   - Fail-open cuando el usuario no tiene TenantUser (super admin puro
///     o proceso de sistema).
/// </summary>
public sealed class AtencionOrdenServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // Base de tiempo determinista para CreatedAt de los turnos. El interceptor de
    // auditoria NO se engancha en el constructor de 2 argumentos que usan estos
    // tests, asi que CreatedAt queda tal cual lo asignamos y OrderBy(CreatedAt)
    // reproduce el orden de siembra de forma estable (sin depender del orden
    // sub-milisegundo de los Guid v7).
    private static readonly DateTimeOffset BaseCreatedAt =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// Siembra el user con el rol dado y una Asignacion con <paramref name="totalSesiones"/>
    /// turnos individuales (uno por sesion, pivote SessionNo=1), donde las primeras
    /// <paramref name="completadasHasta"/> quedan Completado=true. Devuelve los ids de
    /// los pivotes AsignacionTurnoSesion en orden cronologico: sesionIds[N-1] es la
    /// "sesion N".
    /// </summary>
    private static async Task<(Guid userId, List<Guid> sesionIds)> SembrarAsync(
        VisalDbContext ctx,
        TenantRole tenantRole,
        int totalSesiones,
        int completadasHasta,
        bool conPermisoSaltarOrden = false)
    {
        var pu = new PlatformUser { Email = "u@test", DisplayName = "U", AuthProvider = "local" };
        ctx.PlatformUsers.Add(pu);

        Guid? rolId = null;
        // Solo el rol operativo (Advisor) recibe un Rol con permisos configurables;
        // Owner/Admin no traen Rol para probar que YA NO escapan por TenantRole.
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

        var sesionIds = new List<Guid>();
        for (var i = 1; i <= totalSesiones; i++)
        {
            // Un turno por sesion (modelo vigente: task #147). CreatedAt estrictamente
            // creciente => la posicion cronologica coincide con el orden de siembra.
            var turno = new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asig.Id,
                ProfesionalId = Guid.Empty, Cantidad = 1,
                CreatedAt = BaseCreatedAt.AddMinutes(i)
            };
            ctx.AsignacionTurnos.Add(turno);

            var pivote = new AsignacionTurnoSesion
            {
                TenantId = Tenant,
                AsignacionTurnoId = turno.Id,
                SessionNo = 1,
                FechaAtencion = DateOnly.FromDateTime(DateTime.UtcNow),
                Completado = i <= completadasHasta
            };
            ctx.AsignacionTurnoSesiones.Add(pivote);
            sesionIds.Add(pivote.Id);
        }

        await ctx.SaveChangesAsync();
        return (pu.Id, sesionIds);
    }

    [Fact]
    public async Task Sesion1_SiemprePasa()
    {
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 0);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[0], userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Advisor_Sesion2_ConSesion1Pendiente_Bloquea()
    {
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 0);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[1], userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(1, bloqueo!.SessionNoPendiente);
        Assert.Contains("sesion 1", bloqueo.Motivo);
    }

    [Fact]
    public async Task Advisor_Sesion2_ConSesion1Completada_Pasa()
    {
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 1);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[1], userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task Advisor_Sesion3_ConSesion2Pendiente_BloqueaEnSesion2()
    {
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Advisor, totalSesiones: 3, completadasHasta: 1);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[2], userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(2, bloqueo!.SessionNoPendiente);
    }

    [Fact]
    public async Task Owner_SinPermiso_TambienSeBloquea()
    {
        // Regresion del "bloqueo estricto de orden": Owner ya NO escapa por
        // TenantRole. Sin el permiso atencion.saltar-orden se bloquea igual.
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Owner, totalSesiones: 3, completadasHasta: 0);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[2], userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(1, bloqueo!.SessionNoPendiente);
    }

    [Fact]
    public async Task Admin_SinPermiso_TambienSeBloquea()
    {
        // Igual que Owner: Admin no tiene exencion por rol.
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Admin, totalSesiones: 3, completadasHasta: 0);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[2], userId);

        Assert.NotNull(bloqueo);
        Assert.Equal(1, bloqueo!.SessionNoPendiente);
    }

    [Fact]
    public async Task Advisor_ConPermisoSaltarOrden_Pasa()
    {
        var ctx = NewCtx();
        var (userId, sesionIds) = await SembrarAsync(ctx, TenantRole.Advisor,
            totalSesiones: 3, completadasHasta: 0, conPermisoSaltarOrden: true);

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[2], userId);

        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task SinTenantUser_FailOpen()
    {
        var ctx = NewCtx();
        // Sembrado sin usuario: creamos solo la asignacion con dos turnos/sesiones.
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "P", TipoDocumento = "CC", NumeroDocumento = "1" };
        ctx.Pacientes.Add(paciente);
        var asig = new Asignacion { TenantId = Tenant, PacienteId = paciente.Id, TipoServicio = "T", NombreServicio = "T", FormatoHistoria = "H", ContratoCodigo = "C", ServicioId = "S", Sucursal = "SD" };
        ctx.Asignaciones.Add(asig);

        var sesionIds = new List<Guid>();
        for (var i = 1; i <= 2; i++)
        {
            var turno = new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.Empty,
                Cantidad = 1, CreatedAt = BaseCreatedAt.AddMinutes(i)
            };
            ctx.AsignacionTurnos.Add(turno);
            var pivote = new AsignacionTurnoSesion
            {
                TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = 1,
                FechaAtencion = DateOnly.FromDateTime(DateTime.UtcNow), Completado = false
            };
            ctx.AsignacionTurnoSesiones.Add(pivote);
            sesionIds.Add(pivote.Id);
        }
        await ctx.SaveChangesAsync();

        var userDesconocido = Guid.NewGuid();

        var sut = new AtencionOrdenService(ctx);
        var bloqueo = await sut.ValidarAperturaAsync(sesionIds[1], userDesconocido);

        // Sin TenantUser -> fail-open (super admin o servicio de sistema).
        Assert.Null(bloqueo);
    }

    [Fact]
    public async Task PorProfesional_CadaProfLlevaSuPropiaSecuencia()
    {
        // Juan tiene las sesiones 1..2, Mariano las 3..4 (ninguna completada).
        // Mariano puede iniciar la 3 (su primera) sin que Juan cierre 1/2, pero
        // NO puede saltar a la 4 sin cerrar la 3. Al cerrar la 3, la 4 pasa aunque
        // 1/2 (de Juan) sigan pendientes.
        var ctx = NewCtx();
        var juan = Guid.NewGuid();
        var mariano = Guid.NewGuid();

        var pu = new PlatformUser { Email = "u@test", DisplayName = "U", AuthProvider = "local" };
        ctx.PlatformUsers.Add(pu);
        var rol = new Rol { TenantId = Tenant, Nombre = "Coordinador" };
        ctx.Roles.Add(rol);
        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = Tenant, PlatformUserId = pu.Id, Email = pu.Email,
            TenantRole = TenantRole.Advisor, RolId = rol.Id
        });
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "P", TipoDocumento = "CC", NumeroDocumento = "1" };
        ctx.Pacientes.Add(paciente);
        var asig = new Asignacion
        {
            TenantId = Tenant, PacienteId = paciente.Id, TipoServicio = "TERAPIA",
            NombreServicio = "T", FormatoHistoria = "HC", ContratoCodigo = "C",
            ServicioId = "S", Sucursal = "SD"
        };
        ctx.Asignaciones.Add(asig);

        var profPorSesion = new[] { juan, juan, mariano, mariano };
        var sesionIds = new List<Guid>();
        for (var i = 1; i <= 4; i++)
        {
            var turno = new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asig.Id,
                ProfesionalId = profPorSesion[i - 1], Cantidad = 1,
                CreatedAt = BaseCreatedAt.AddMinutes(i)
            };
            ctx.AsignacionTurnos.Add(turno);
            ctx.AsignacionTurnoSesiones.Add(new AsignacionTurnoSesion
            {
                TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = 1,
                FechaAtencion = DateOnly.FromDateTime(DateTime.UtcNow), Completado = false
            });
            sesionIds.Add(ctx.AsignacionTurnoSesiones.Local.Last().Id);
        }
        await ctx.SaveChangesAsync();

        var sut = new AtencionOrdenService(ctx);

        // Sesion 3 (primera de Mariano) pasa aunque 1 y 2 (Juan) esten pendientes.
        Assert.Null(await sut.ValidarAperturaAsync(sesionIds[2], pu.Id));

        // Sesion 4 (Mariano) bloquea porque su sesion 3 esta pendiente. Mensaje: sesion 3.
        var b4 = await sut.ValidarAperturaAsync(sesionIds[3], pu.Id);
        Assert.NotNull(b4);
        Assert.Equal(3, b4!.SessionNoPendiente);
        Assert.Contains("sesion 3", b4.Motivo);

        // Cerrar la 3 -> la 4 pasa aunque 1 y 2 (Juan) sigan pendientes.
        var s3 = await ctx.AsignacionTurnoSesiones.FirstAsync(s => s.Id == sesionIds[2]);
        s3.Completado = true;
        await ctx.SaveChangesAsync();
        Assert.Null(await sut.ValidarAperturaAsync(sesionIds[3], pu.Id));
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

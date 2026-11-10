using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Cierre de periodos (Coordinacion). Cubre:
///  - El periodo se selecciona por FECHA DE ASIGNACION (FechaInicio), MISMO criterio que
///    el filtro de Mes de Atencion. Una asignacion con FechaInicio en octubre pero
///    MesFinal=11 (dato inconsistente) se cierra en OCTUBRE, no en noviembre.
///  - Cerrar anula las sesiones pendientes (marca sus turnos) y Reabrir las devuelve a
///    pendientes y borra el cierre.
/// </summary>
public sealed class CierrePeriodoServiceTests
{
    // Guid.Empty a proposito: el estampado de TenantId lo hace un interceptor de DI que no
    // existe en el harness de tests, asi que las entidades que crea el SERVICIO (el cierre)
    // quedan con TenantId vacio. Usando el mismo valor en la siembra y en el ITenantContext,
    // el filtro global (e.TenantId == tenant) casa tanto lo sembrado como lo que crea el
    // servicio, y el test ejercita la logica real de cerrar/reabrir.
    private static readonly Guid Tenant = Guid.Empty;

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static CierrePeriodoService NewSvc(VisalDbContext ctx) =>
        new(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }));

    // Siembra 1 paciente + 1 asignacion (FechaInicio dada, MesVigencia/MesFinal dados) + 1 turno
    // pendiente (cantidad 2, sin sesion completada). Devuelve (asignacionId, turnoId).
    private static async Task<(Guid asigId, Guid turnoId)> SembrarAsync(
        VisalDbContext ctx, DateOnly fechaInicio, short mesVigencia, short? mesFinal)
    {
        var pacienteId = Guid.NewGuid();
        ctx.Pacientes.Add(new Paciente
        {
            Id = pacienteId, TenantId = Tenant,
            PrimerNombre = "ANA", PrimerApellido = "GOMEZ", NombreCompleto = "ANA GOMEZ",
            TipoDocumento = "CC", NumeroDocumento = "99887766"
        });

        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = pacienteId,
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "ATENCION ENFERMERIA",
            TipoServicio = "ENFERMERIA", Cantidad = 2, ContratoCodigo = "C1",
            AnioServicio = (short)fechaInicio.Year, MesVigencia = mesVigencia, MesFinal = mesFinal,
            FechaInicio = fechaInicio, Estado = AsignacionEstado.Asignado
        };
        ctx.Asignaciones.Add(asig);

        var turno = new AsignacionTurno
        {
            TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.NewGuid(), Cantidad = 2,
            CreatedAt = new DateTimeOffset(fechaInicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        };
        ctx.AsignacionTurnos.Add(turno);

        await ctx.SaveChangesAsync();
        return (asig.Id, turno.Id);
    }

    [Fact]
    public async Task Periodo_SeSeleccionaPorFechaInicio_NoPorMesFinal()
    {
        await using var ctx = NewCtx();
        // FechaInicio octubre, pero MesFinal=11 (dato inconsistente estilo "los 58").
        await SembrarAsync(ctx, new DateOnly(2026, 10, 7), mesVigencia: 10, mesFinal: 11);
        var svc = NewSvc(ctx);

        var octubre = await svc.PrevisualizarAsync(2026, 10);
        var noviembre = await svc.PrevisualizarAsync(2026, 11);

        // Se cierra en OCTUBRE (mes de FechaInicio), NO en noviembre (MesFinal).
        Assert.Equal(1, octubre.TotalServicios);
        Assert.Equal(2, octubre.TotalSesiones);
        Assert.Equal(0, noviembre.TotalServicios);
        Assert.Equal(0, noviembre.TotalSesiones);
    }

    [Fact]
    public async Task Cerrar_AnulaPendientes_Y_Reabrir_LasRestaura()
    {
        await using var ctx = NewCtx();
        var (_, turnoId) = await SembrarAsync(ctx, new DateOnly(2026, 10, 7), mesVigencia: 10, mesFinal: 10);
        var svc = NewSvc(ctx);

        // 1) Cerrar octubre: el turno queda marcado por el cierre.
        var cierre = await svc.CerrarAsync(2026, 10, pacienteId: null, sede: null, actor: Guid.NewGuid());
        Assert.Equal(2, cierre.TotalSesiones);

        var turnoCerrado = await ctx.AsignacionTurnos.AsNoTracking().FirstAsync(t => t.Id == turnoId);
        Assert.Equal(cierre.Id, turnoCerrado.CierrePeriodoId);

        // Ya no aparece como pendiente.
        var trasCierre = await svc.PrevisualizarAsync(2026, 10);
        Assert.Equal(0, trasCierre.TotalSesiones);

        // 2) Reabrir: el turno vuelve a pendiente y el cierre se borra.
        var reabierto = await svc.ReabrirAsync(cierre.Id, actor: Guid.NewGuid());
        Assert.True(reabierto);

        var turnoReabierto = await ctx.AsignacionTurnos.AsNoTracking().FirstAsync(t => t.Id == turnoId);
        Assert.Null(turnoReabierto.CierrePeriodoId);
        Assert.False(await ctx.CierrePeriodos.AsNoTracking().AnyAsync(c => c.Id == cierre.Id));

        var trasReabrir = await svc.PrevisualizarAsync(2026, 10);
        Assert.Equal(2, trasReabrir.TotalSesiones);
    }
}

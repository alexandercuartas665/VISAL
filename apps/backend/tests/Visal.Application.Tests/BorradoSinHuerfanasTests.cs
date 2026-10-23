using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Agendas;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Refuerzo 5: ningun borrado de turno/sesion/asignacion puede dejar una HC
/// Abierta/Cerrada huerfana. Verifica los GUARDS de aplicacion (coordinacion y
/// cancelacion de cita). El backstop de BD (FK sesion_id ON DELETE RESTRICT) se
/// valida aparte contra postgres — el provider InMemory no enforce FKs.
///
/// Politica (decidida con el usuario): sin forzar, una HC Abierta/Cerrada ligada
/// BLOQUEA el borrado; si las unicas ligadas son Inactivas (descartadas) se permite
/// y se limpian (pivote + HC), sin dejar huerfanas.
/// </summary>
public sealed class BorradoSinHuerfanasTests
{
    private static readonly Guid Tenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

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
    }

    private sealed class FakeFestivos : IFestivosColombiaService
    {
        public IReadOnlyList<FestivoColombiaDto> DeAnio(int anio) => Array.Empty<FestivoColombiaDto>();
        public IReadOnlyDictionary<DateOnly, string> MapaDeAnio(int anio) => new Dictionary<DateOnly, string>();
    }

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static AsignacionService Svc(VisalDbContext ctx) =>
        new(ctx, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

    // Siembra 1 asignacion + 1 turno + 1 sesion + 1 HC (con el estado dado) + pivote.
    private static async Task<(Guid asigId, Guid turnoId, Guid hcId)> SembrarConHcAsync(
        VisalDbContext ctx, HistoriaClinicaEstado estado)
    {
        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = Guid.NewGuid(),
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "X", TipoServicio = "ENFERMERIA",
            Cantidad = 1, ContratoCodigo = "C1", MesVigencia = 8, FechaInicio = new DateOnly(2026, 8, 1),
            Estado = AsignacionEstado.Asignado
        };
        ctx.Asignaciones.Add(asig);
        var turno = new AsignacionTurno
        {
            TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.NewGuid(),
            Cantidad = 1, CreatedAt = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero)
        };
        ctx.AsignacionTurnos.Add(turno);
        var ses = new AsignacionTurnoSesion
        {
            TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = 1,
            FechaAtencion = new DateOnly(2026, 8, 1), Completado = true
        };
        ctx.AsignacionTurnoSesiones.Add(ses);
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = asig.PacienteId, FormDefinitionId = Guid.NewGuid(),
            Estado = estado, ValoresJson = "{}", FechaApertura = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)
        };
        ctx.HistoriasClinicas.Add(hc);
        ctx.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc
        {
            TenantId = Tenant, SesionId = ses.Id, HistoriaClinicaId = hc.Id
        });
        await ctx.SaveChangesAsync();
        return (asig.Id, turno.Id, hc.Id);
    }

    [Fact]
    public async Task EliminarCoordinacion_sin_forzar_con_HC_cerrada_bloquea_y_no_borra()
    {
        await using var ctx = NewCtx();
        var (asigId, _, hcId) = await SembrarConHcAsync(ctx, HistoriaClinicaEstado.Cerrada);

        var svc = Svc(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EliminarCoordinacionAsync(asigId, Guid.NewGuid()));

        // Nada se borro: la HC Cerrada, su pivote y la asignacion siguen intactos.
        Assert.True(await ctx.HistoriasClinicas.AnyAsync(h => h.Id == hcId));
        Assert.True(await ctx.AsignacionTurnoSesionHcs.AnyAsync(pv => pv.HistoriaClinicaId == hcId));
        Assert.True(await ctx.Asignaciones.AnyAsync(a => a.Id == asigId));
    }

    [Fact]
    public async Task EliminarCoordinacion_sin_forzar_con_HC_inactiva_borra_y_limpia()
    {
        await using var ctx = NewCtx();
        var (asigId, turnoId, hcId) = await SembrarConHcAsync(ctx, HistoriaClinicaEstado.Inactiva);

        var svc = Svc(ctx);
        var ok = await svc.EliminarCoordinacionAsync(asigId, Guid.NewGuid());
        Assert.True(ok);

        // La HC inactiva y su pivote se limpiaron (sin huerfanas); se fueron sesion/turno/asignacion.
        Assert.False(await ctx.HistoriasClinicas.AnyAsync(h => h.Id == hcId));
        Assert.False(await ctx.AsignacionTurnoSesionHcs.AnyAsync(pv => pv.HistoriaClinicaId == hcId));
        Assert.False(await ctx.AsignacionTurnoSesiones.AnyAsync(s => s.AsignacionTurnoId == turnoId));
        Assert.False(await ctx.Asignaciones.AnyAsync(a => a.Id == asigId));
    }

    [Fact]
    public async Task CancelarCita_con_HC_cerrada_bloquea_y_no_borra()
    {
        await using var ctx = NewCtx();
        var (_, turnoId, hcId) = await SembrarConHcAsync(ctx, HistoriaClinicaEstado.Cerrada);

        var ag = new AsignacionAgendasService(ctx, new FakeFestivos(), Svc(ctx));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ag.CancelarCitaAsync(turnoId, Guid.NewGuid()));

        Assert.True(await ctx.HistoriasClinicas.AnyAsync(h => h.Id == hcId));
        Assert.True(await ctx.AsignacionTurnos.AnyAsync(t => t.Id == turnoId));
    }

    [Fact]
    public async Task CancelarCita_con_HC_inactiva_borra_y_limpia()
    {
        await using var ctx = NewCtx();
        var (_, turnoId, hcId) = await SembrarConHcAsync(ctx, HistoriaClinicaEstado.Inactiva);

        var ag = new AsignacionAgendasService(ctx, new FakeFestivos(), Svc(ctx));
        var ok = await ag.CancelarCitaAsync(turnoId, Guid.NewGuid());
        Assert.True(ok);

        // La HC inactiva y su pivote se limpiaron; el turno se fue.
        Assert.False(await ctx.HistoriasClinicas.AnyAsync(h => h.Id == hcId));
        Assert.False(await ctx.AsignacionTurnoSesionHcs.AnyAsync(pv => pv.HistoriaClinicaId == hcId));
        Assert.False(await ctx.AsignacionTurnos.AnyAsync(t => t.Id == turnoId));
    }
}

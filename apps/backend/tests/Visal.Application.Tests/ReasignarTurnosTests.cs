using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica la reasignacion de doctor sobre los turnos de un servicio coordinado
/// (AsignacionService.ReasignarTurnosAsync / ListarTurnosReasignablesAsync).
/// Reglas cubiertas:
///  - Solo se mueven los turnos SIN historia clinica CERRADA (los atendidos se
///    quedan con el doctor original -> historial intacto).
///  - El doctor destino debe ser del tipo de profesional del servicio.
///  - ListarTurnosReasignablesAsync marca cerrada/abierta por turno.
/// </summary>
public sealed class ReasignarTurnosTests
{
    private static readonly Guid Tenant = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

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

    // Siembra: 1 asignacion ENFERMERIA + 3 turnos del doctor origen. Si cerrarPrimero,
    // el turno #1 recibe una HC CERRADA (via sesion + pivote). Devuelve (asignacionId,
    // turnoIds ordenados, origenId, destinoId).
    private static async Task<(Guid asigId, List<Guid> turnoIds, Guid origen, Guid destino)>
        SembrarAsync(VisalDbContext ctx, bool cerrarPrimero, string tipoDestino = "ENFERMERIA")
    {
        var tipoEnf = new TipoProfesional { TenantId = Tenant, Nombre = "ENFERMERIA", Activo = true };
        var tipoOtro = new TipoProfesional { TenantId = Tenant, Nombre = "TERAPIA", Activo = true };
        ctx.TiposProfesional.AddRange(tipoEnf, tipoOtro);

        var origen = new Profesional
        {
            TenantId = Tenant, NumeroDocumento = "1001", NombreCompleto = "DR ORIGEN",
            TipoProfesionalId = tipoEnf.Id
        };
        var destino = new Profesional
        {
            TenantId = Tenant, NumeroDocumento = "1002", NombreCompleto = "DR DESTINO",
            TipoProfesionalId = tipoDestino == "ENFERMERIA" ? tipoEnf.Id : tipoOtro.Id
        };
        ctx.Profesionales.AddRange(origen, destino);

        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = Guid.NewGuid(),
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "ATENCION ENFERMERIA",
            TipoServicio = "ENFERMERIA", Cantidad = 3, ContratoCodigo = "C1",
            MesVigencia = 8, FechaInicio = new DateOnly(2026, 8, 1),
            Estado = AsignacionEstado.Asignado
        };
        ctx.Asignaciones.Add(asig);

        var turnos = new List<AsignacionTurno>();
        for (int i = 0; i < 3; i++)
        {
            var t = new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = origen.Id, Cantidad = 1,
                CreatedAt = new DateTimeOffset(2026, 8, 1, 8, 0, i, TimeSpan.Zero)
            };
            turnos.Add(t);
        }
        ctx.AsignacionTurnos.AddRange(turnos);

        if (cerrarPrimero)
        {
            var hc = new HistoriaClinica
            {
                TenantId = Tenant, PacienteId = asig.PacienteId, FormDefinitionId = Guid.NewGuid(),
                Estado = HistoriaClinicaEstado.Cerrada, ValoresJson = "{}",
                FechaApertura = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)
            };
            ctx.HistoriasClinicas.Add(hc);
            var ses = new AsignacionTurnoSesion
            {
                TenantId = Tenant, AsignacionTurnoId = turnos[0].Id, SessionNo = 1,
                FechaAtencion = new DateOnly(2026, 8, 1), Completado = true
            };
            ctx.AsignacionTurnoSesiones.Add(ses);
            ctx.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc
            {
                TenantId = Tenant, SesionId = ses.Id, HistoriaClinicaId = hc.Id
            });
        }

        await ctx.SaveChangesAsync();
        return (asig.Id, turnos.Select(t => t.Id).ToList(), origen.Id, destino.Id);
    }

    [Fact]
    public async Task Reasigna_los_pendientes_y_respeta_el_turno_cerrado()
    {
        await using var ctx = NewCtx();
        var (_, turnoIds, origen, destino) = await SembrarAsync(ctx, cerrarPrimero: true);

        var svc = new AsignacionService(ctx, new FakeTenantContext { TenantId = Tenant });
        var res = await svc.ReasignarTurnosAsync(
            new ReasignarTurnosRequest(turnoIds, destino), Guid.NewGuid());

        Assert.Equal(2, res.Reasignados);          // turno 2 y 3
        Assert.Single(res.Omitidos);               // turno 1 (cerrado)

        var turnos = await ctx.AsignacionTurnos.AsNoTracking()
            .OrderBy(t => t.CreatedAt).ToListAsync();
        Assert.Equal(origen, turnos[0].ProfesionalId);   // cerrado -> intacto
        Assert.Equal(destino, turnos[1].ProfesionalId);
        Assert.Equal(destino, turnos[2].ProfesionalId);
    }

    [Fact]
    public async Task Reasigna_todos_cuando_ninguno_esta_cerrado()
    {
        await using var ctx = NewCtx();
        var (_, turnoIds, _, destino) = await SembrarAsync(ctx, cerrarPrimero: false);

        var svc = new AsignacionService(ctx, new FakeTenantContext { TenantId = Tenant });
        var res = await svc.ReasignarTurnosAsync(
            new ReasignarTurnosRequest(turnoIds, destino), Guid.NewGuid());

        Assert.Equal(3, res.Reasignados);
        Assert.Empty(res.Omitidos);
        Assert.True(await ctx.AsignacionTurnos.AsNoTracking().AllAsync(t => t.ProfesionalId == destino));
    }

    [Fact]
    public async Task Rechaza_destino_de_tipo_incompatible()
    {
        await using var ctx = NewCtx();
        // Destino es TERAPIA, el servicio es ENFERMERIA -> no elegible.
        var (_, turnoIds, _, destino) = await SembrarAsync(ctx, cerrarPrimero: false, tipoDestino: "TERAPIA");

        var svc = new AsignacionService(ctx, new FakeTenantContext { TenantId = Tenant });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ReasignarTurnosAsync(new ReasignarTurnosRequest(turnoIds, destino), Guid.NewGuid()));
    }

    [Fact]
    public async Task Listar_marca_el_turno_con_HC_cerrada()
    {
        await using var ctx = NewCtx();
        var (asigId, turnoIds, _, _) = await SembrarAsync(ctx, cerrarPrimero: true);

        var svc = new AsignacionService(ctx, new FakeTenantContext { TenantId = Tenant });
        var lista = await svc.ListarTurnosReasignablesAsync(asigId);

        Assert.Equal(3, lista.Count);
        var primero = lista.First(x => x.Id == turnoIds[0]);
        Assert.True(primero.TieneHcCerrada);
        Assert.All(lista.Where(x => x.Id != turnoIds[0]), x => Assert.False(x.TieneHcCerrada));
    }
}

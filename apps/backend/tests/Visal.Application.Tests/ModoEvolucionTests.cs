using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica el "modo terapia" (FormDefinition.FormatoEvolucionCodigo):
/// AtencionProfesionalService.GetMisServiciosAsync devuelve el formato de HC completo
/// para la 1ra sesion cronologica y el formato de EVOLUCION para la 2da en adelante,
/// cuando el formato de HC del servicio lo tiene configurado. Si no lo tiene (null),
/// todas las sesiones usan el mismo formato (comportamiento historico intacto).
/// </summary>
public sealed class ModoEvolucionTests
{
    private static readonly Guid Tenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

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

    // Siembra: usuario Owner (ve todos los turnos), 1 asignacion TERAPIAS con formato
    // de HC "HC-FO-14" y 2 turnos (nGlobal 1 y 2 por CreatedAt). Si evolucionCodigo != null,
    // HC-FO-14 apunta a ese formato de evolucion. Devuelve el platformUserId del usuario.
    private static async Task<Guid> SembrarAsync(VisalDbContext ctx, string? evolucionCodigo)
    {
        var prof = new Profesional
        {
            TenantId = Tenant, NumeroDocumento = "2001", NombreCompleto = "DR TERAPEUTA"
        };
        ctx.Profesionales.Add(prof);

        var platformUserId = Guid.NewGuid();
        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = Tenant, PlatformUserId = platformUserId, Email = "admin@t.co",
            TenantRole = TenantRole.Owner
        });

        ctx.FormDefinitions.AddRange(
            new FormDefinition
            {
                TenantId = Tenant, Codigo = "HC-FO-14", Nombre = "HC TERAPIAS", Tipo = "HISTORIA CLINICA",
                SchemaJson = "{\"children\":[]}", FormatoEvolucionCodigo = evolucionCodigo
            },
            new FormDefinition
            {
                TenantId = Tenant, Codigo = "EVOL-14", Nombre = "EVOLUCION TERAPIAS", Tipo = "HISTORIA CLINICA",
                SchemaJson = "{\"children\":[]}"
            });

        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = Guid.NewGuid(),
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "TERAPIA FISICA",
            TipoServicio = "TERAPIAS", Modulo = "TERAPIAS", Cantidad = 2, ContratoCodigo = "C1",
            FormatoHistoria = "HC-FO-14",
            MesVigencia = 8, FechaInicio = new DateOnly(2026, 8, 1),
            Estado = AsignacionEstado.Asignado
        };
        ctx.Asignaciones.Add(asig);

        for (int i = 0; i < 2; i++)
        {
            ctx.AsignacionTurnos.Add(new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = prof.Id, Cantidad = 1,
                CreatedAt = new DateTimeOffset(2026, 8, 1, 8, 0, i, TimeSpan.Zero)
            });
        }

        await ctx.SaveChangesAsync();
        return platformUserId;
    }

    [Fact]
    public async Task Sesion1_usa_HC_completo_y_sesion2_usa_evolucion()
    {
        await using var ctx = NewCtx();
        var userId = await SembrarAsync(ctx, evolucionCodigo: "EVOL-14");

        var svc = new AtencionProfesionalService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }), new FakeTenantContext { TenantId = Tenant }, null!);
        var filas = await svc.GetMisServiciosAsync(userId);

        Assert.Equal(2, filas.Count);
        var s1 = filas.Single(f => f.NumeroSesionMostrar == 1);
        var s2 = filas.Single(f => f.NumeroSesionMostrar == 2);
        Assert.Equal("HC-FO-14", s1.FormatoHistoria);   // 1ra sesion: HC completo
        Assert.Equal("EVOL-14", s2.FormatoHistoria);    // 2da en adelante: evolucion
    }

    [Fact]
    public async Task Sin_formato_evolucion_todas_las_sesiones_usan_el_mismo()
    {
        await using var ctx = NewCtx();
        var userId = await SembrarAsync(ctx, evolucionCodigo: null);

        var svc = new AtencionProfesionalService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }), new FakeTenantContext { TenantId = Tenant }, null!);
        var filas = await svc.GetMisServiciosAsync(userId);

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.Equal("HC-FO-14", f.FormatoHistoria));
    }

    /// <summary>
    /// Asignacion multi-profesional: cada profesional tiene su propio hilo, asi que la
    /// PRIMERA sesion de CADA profesional usa el formato base y las siguientes evolucion.
    /// Antes se contaba por asignacion (global), de modo que la 1ra sesion del 2do
    /// profesional (3er turno global) se evaluaba como ">=2" y abria evolucion sin base
    /// —o, con numero_sesion NULL, se abrian dos bases—. Con numero_sesion NULL para
    /// forzar el camino de fallback por orden real.
    /// </summary>
    [Fact]
    public async Task MultiProfesional_cada_profesional_tiene_su_propia_base()
    {
        await using var ctx = NewCtx();

        var platformUserId = Guid.NewGuid();
        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = Tenant, PlatformUserId = platformUserId, Email = "admin@t.co",
            TenantRole = TenantRole.Owner
        });

        var profA = new Profesional { TenantId = Tenant, NumeroDocumento = "A1", NombreCompleto = "DR A" };
        var profB = new Profesional { TenantId = Tenant, NumeroDocumento = "B1", NombreCompleto = "DR B" };
        ctx.Profesionales.AddRange(profA, profB);

        ctx.FormDefinitions.AddRange(
            new FormDefinition
            {
                TenantId = Tenant, Codigo = "HC-FO-14", Nombre = "HC", Tipo = "HISTORIA CLINICA",
                SchemaJson = "{\"children\":[]}", FormatoEvolucionCodigo = "EVOL-14"
            },
            new FormDefinition
            {
                TenantId = Tenant, Codigo = "EVOL-14", Nombre = "EVO", Tipo = "HISTORIA CLINICA",
                SchemaJson = "{\"children\":[]}"
            });

        var asig = new Asignacion
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = Guid.NewGuid(),
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "TERAPIA", TipoServicio = "TERAPIAS",
            Modulo = "TERAPIAS", Cantidad = 4, ContratoCodigo = "C1", FormatoHistoria = "HC-FO-14",
            MesVigencia = 8, FechaInicio = new DateOnly(2026, 8, 1), Estado = AsignacionEstado.Asignado
        };
        ctx.Asignaciones.Add(asig);

        // Turnos intercalados A,B,A,B por CreatedAt (NumeroSesion NULL) para que el orden
        // GLOBAL sea 1,2,3,4 y el bug viejo marcara B-1 (global 2) como evolucion.
        AsignacionTurno Turno(Guid profId, int seg) => new()
        {
            TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = profId, Cantidad = 1,
            CreatedAt = new DateTimeOffset(2026, 8, 1, 8, 0, seg, TimeSpan.Zero)
        };
        var tA1 = Turno(profA.Id, 0);
        var tB1 = Turno(profB.Id, 1);
        var tA2 = Turno(profA.Id, 2);
        var tB2 = Turno(profB.Id, 3);
        ctx.AsignacionTurnos.AddRange(tA1, tB1, tA2, tB2);
        await ctx.SaveChangesAsync();

        var svc = new AtencionProfesionalService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }), new FakeTenantContext { TenantId = Tenant }, null!);
        var filas = (await svc.GetMisServiciosAsync(platformUserId)).ToList();

        string Fmt(Guid turnoId) => filas.Single(f => f.AsignacionTurnoId == turnoId).FormatoHistoria!;
        Assert.Equal("HC-FO-14", Fmt(tA1.Id));  // base del profesional A
        Assert.Equal("EVOL-14", Fmt(tA2.Id));   // evolucion de A
        Assert.Equal("HC-FO-14", Fmt(tB1.Id));  // base de B (aunque sea el 2do turno global)
        Assert.Equal("EVOL-14", Fmt(tB2.Id));   // evolucion de B
    }

    // ── Puente por meses (FormDefinition.MesesPuente) — Opcion A ─────────────────────
    // Siembra: terapia HC-FO-14 (evolucion EVOL-14, puente 6 meses), un profesional y una
    // asignacion NUEVA de la terapia con 1 turno (sesion 1) el 2026-08-10. La atencion
    // PREVIA del mismo profesional (2026-08-01) se hace en "fmtPrevia" y queda vinculada a
    // una asignacion cuyo formato base es "baseAsigPrevia" (null = HC sin vinculo/huerfana).
    private static async Task<(Guid userId, Guid turnoNuevo)> SembrarPuenteAsync(
        VisalDbContext ctx, string fmtPrevia, string? baseAsigPrevia)
    {
        var platformUserId = Guid.NewGuid();
        ctx.TenantUsers.Add(new TenantUser
        {
            TenantId = Tenant, PlatformUserId = platformUserId, Email = "admin@t.co", TenantRole = TenantRole.Owner
        });
        var prof = new Profesional { TenantId = Tenant, NumeroDocumento = "P1", NombreCompleto = "DR PUENTE" };
        ctx.Profesionales.Add(prof);
        var fBase = new FormDefinition
        {
            TenantId = Tenant, Codigo = "HC-FO-14", Nombre = "HC TERAPIA", Tipo = "HISTORIA CLINICA",
            SchemaJson = "{\"children\":[]}", FormatoEvolucionCodigo = "EVOL-14", MesesPuente = 6
        };
        var fEvo = new FormDefinition
        {
            TenantId = Tenant, Codigo = "EVOL-14", Nombre = "EVOLUCION", Tipo = "HISTORIA CLINICA",
            SchemaJson = "{\"children\":[]}"
        };
        ctx.FormDefinitions.AddRange(fBase, fEvo);
        var pacienteId = Guid.NewGuid();

        Asignacion NuevaAsig(string formato, string servicio) => new()
        {
            TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = pacienteId, Sucursal = "CALI",
            ServicioId = servicio, NombreServicio = servicio, TipoServicio = "TERAPIAS", Modulo = "TERAPIAS",
            Cantidad = 1, ContratoCodigo = "C1", FormatoHistoria = formato, MesVigencia = 8,
            FechaInicio = new DateOnly(2026, 8, 1), Estado = AsignacionEstado.Asignado
        };

        var hcPrevia = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = pacienteId, ProfesionalId = prof.Id,
            FormDefinitionId = fmtPrevia == "HC-FO-14" ? fBase.Id : fEvo.Id,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            FechaAtencion = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        };
        ctx.HistoriasClinicas.Add(hcPrevia);
        if (baseAsigPrevia is not null)
        {
            var asigPrevia = NuevaAsig(baseAsigPrevia, "SV-PREVIO");
            ctx.Asignaciones.Add(asigPrevia);
            var turnoPrevio = new AsignacionTurno
            {
                TenantId = Tenant, AsignacionId = asigPrevia.Id, ProfesionalId = prof.Id, Cantidad = 1,
                CreatedAt = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero)
            };
            ctx.AsignacionTurnos.Add(turnoPrevio);
            var sesion = new AsignacionTurnoSesion
            {
                TenantId = Tenant, AsignacionTurnoId = turnoPrevio.Id, SessionNo = 1, FechaAtencion = new DateOnly(2026, 8, 1)
            };
            ctx.AsignacionTurnoSesiones.Add(sesion);
            ctx.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc
            {
                TenantId = Tenant, SesionId = sesion.Id, HistoriaClinicaId = hcPrevia.Id
            });
        }

        var asigNueva = NuevaAsig("HC-FO-14", "SV-TERAPIA");
        ctx.Asignaciones.Add(asigNueva);
        var turnoNuevo = new AsignacionTurno
        {
            TenantId = Tenant, AsignacionId = asigNueva.Id, ProfesionalId = prof.Id, Cantidad = 1,
            FechaInicio = new DateOnly(2026, 8, 10),
            CreatedAt = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero)
        };
        ctx.AsignacionTurnos.Add(turnoNuevo);
        await ctx.SaveChangesAsync();
        return (platformUserId, turnoNuevo.Id);
    }

    /// <summary>
    /// El puente de la sesion 1 solo lo abre una atencion previa de la MISMA terapia.
    /// Caso real (paciente de prueba 104578855): "Limpieza y cuidados de traqueostomia"
    /// esta configurado directo con el formato de EVOLUCION de la terapia respiratoria;
    /// su nota abria el puente y la terapia arrancaba en evolucion sin valoracion inicial.
    /// </summary>
    [Theory]
    [InlineData("EVOL-14", "HC-FO-14", "EVOL-14")]  // evolucion de la MISMA terapia -> puente abierto
    [InlineData("EVOL-14", "EVOL-14", "HC-FO-14")]  // evolucion de OTRO servicio (traqueostomia) -> base
    [InlineData("HC-FO-14", null, "EVOL-14")]       // HC base previa (formato exclusivo de la terapia) -> abierto
    [InlineData("EVOL-14", null, "EVOL-14")]        // evolucion huerfana/legacy -> comportamiento anterior
    public async Task Puente_por_meses_solo_cuenta_la_misma_terapia(string fmtPrevia, string? baseAsigPrevia, string esperado)
    {
        await using var ctx = NewCtx();
        var (userId, turnoNuevo) = await SembrarPuenteAsync(ctx, fmtPrevia, baseAsigPrevia);

        var svc = new AtencionProfesionalService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }), new FakeTenantContext { TenantId = Tenant }, null!);
        var filas = await svc.GetMisServiciosAsync(userId);

        Assert.Equal(esperado, filas.Single(f => f.AsignacionTurnoId == turnoNuevo).FormatoHistoria);
    }
}

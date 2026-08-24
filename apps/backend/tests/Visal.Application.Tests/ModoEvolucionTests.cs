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

        var svc = new AtencionProfesionalService(ctx, new FakeTenantContext { TenantId = Tenant }, null!);
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

        var svc = new AtencionProfesionalService(ctx, new FakeTenantContext { TenantId = Tenant }, null!);
        var filas = await svc.GetMisServiciosAsync(userId);

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.Equal("HC-FO-14", f.FormatoHistoria));
    }
}

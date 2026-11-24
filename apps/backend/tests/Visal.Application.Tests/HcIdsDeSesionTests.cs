using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>IMP-65: HC vinculadas a la sesion desde la que se abre el modal en /atencion.</summary>
public sealed class HcIdsDeSesionTests
{
    private static readonly Guid Tenant = Guid.Parse("abababab-abab-abab-abab-abababababab");

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

    private static HistoriaClinicaService Svc(VisalDbContext ctx) =>
        new(new FakeTenantContext { TenantId = Tenant }, null!, null!, null!, null!, null!, null!, null!,
            new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }), TimeProvider.System);

    private static Guid Hc(VisalDbContext ctx, Guid sesionId)
    {
        var hc = new HistoriaClinica { TenantId = Tenant, PacienteId = Guid.NewGuid(), FormDefinitionId = Guid.NewGuid(), FechaApertura = DateTimeOffset.UtcNow };
        ctx.HistoriasClinicas.Add(hc);
        ctx.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc { TenantId = Tenant, SesionId = sesionId, HistoriaClinicaId = hc.Id });
        return hc.Id;
    }

    [Fact]
    public async Task Devuelve_solo_las_hc_de_esa_sesion_en_cualquier_estado()
    {
        await using var ctx = NewCtx();
        var turno = Guid.NewGuid();
        var otroTurno = Guid.NewGuid();
        var s1 = new AsignacionTurnoSesion { TenantId = Tenant, AsignacionTurnoId = turno, SessionNo = 1 };
        var s2 = new AsignacionTurnoSesion { TenantId = Tenant, AsignacionTurnoId = turno, SessionNo = 2 };
        var s3 = new AsignacionTurnoSesion { TenantId = Tenant, AsignacionTurnoId = otroTurno, SessionNo = 1 };
        ctx.AsignacionTurnoSesiones.AddRange(s1, s2, s3);
        var a = Hc(ctx, s1.Id);
        var b = Hc(ctx, s1.Id);
        Hc(ctx, s2.Id);
        Hc(ctx, s3.Id);
        await ctx.SaveChangesAsync();

        var ids = await Svc(ctx).ListarHcIdsDeSesionAsync(turno, 1);

        Assert.Equal(new[] { a, b }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task Sin_sesion_valida_devuelve_vacio()
    {
        await using var ctx = NewCtx();
        Assert.Empty(await Svc(ctx).ListarHcIdsDeSesionAsync(Guid.Empty, 1));
        Assert.Empty(await Svc(ctx).ListarHcIdsDeSesionAsync(Guid.NewGuid(), 0));
        Assert.Empty(await Svc(ctx).ListarHcIdsDeSesionAsync(Guid.NewGuid(), 1));
    }
}

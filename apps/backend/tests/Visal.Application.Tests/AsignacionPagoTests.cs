using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Pago del paciente (cuota moderadora / copago) registrado desde el modal de Autorizacion:
/// AsignacionService.ActualizarPagoAsync + filtro "Solo sin copago / cuota moderadora" del Listado.
/// </summary>
public sealed class AsignacionPagoTests
{
    private static readonly Guid Tenant = Guid.Parse("abababab-abab-abab-abab-abababababab");

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

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static AsignacionService Svc(VisalDbContext ctx) =>
        new(new FakeTenantContext { TenantId = Tenant }, new NoopAudit(),
            new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }));

    private static Asignacion NuevaAsig(Guid pacienteId, string? tipo = null, decimal? real = null) => new()
    {
        TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = pacienteId,
        Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "CONSULTA",
        TipoServicio = "CONSULTA", Cantidad = 1, ContratoCodigo = "C1",
        MesVigencia = 11, FechaInicio = new DateOnly(2026, 11, 1),
        Estado = AsignacionEstado.Pendiente,
        TipoPago = tipo, ValorPagoReal = real,
    };

    [Fact]
    public async Task Guarda_copago_y_luego_sin_cargo_limpia_valores()
    {
        await using var ctx = NewCtx();
        var a = NuevaAsig(Guid.NewGuid());
        ctx.Asignaciones.Add(a);
        await ctx.SaveChangesAsync();
        var svc = Svc(ctx);

        Assert.True(await svc.ActualizarPagoAsync(a.Id, "copago", "NIVEL 1", 3000m, 3500m, Guid.NewGuid()));
        var row = await ctx.Asignaciones.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal("COPAGO", row.TipoPago);
        Assert.Equal("NIVEL 1", row.CategoriaCopago);
        Assert.Equal(3000m, row.ValorPagoSugerido);
        Assert.Equal(3500m, row.ValorPagoReal);

        Assert.True(await svc.ActualizarPagoAsync(a.Id, "SIN_CARGO", "X", 1m, 1m, Guid.NewGuid()));
        row = await ctx.Asignaciones.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal("SIN_CARGO", row.TipoPago);
        Assert.Null(row.CategoriaCopago);
        Assert.Null(row.ValorPagoSugerido);
        Assert.Null(row.ValorPagoReal);
    }

    [Theory]
    [InlineData("OTRO", 1000)]
    [InlineData("CUOTA", null)]
    [InlineData("COPAGO", -5)]
    public async Task Rechaza_tipo_invalido_o_cuota_copago_sin_valor(string tipo, int? valor)
    {
        await using var ctx = NewCtx();
        var a = NuevaAsig(Guid.NewGuid());
        ctx.Asignaciones.Add(a);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Svc(ctx).ActualizarPagoAsync(a.Id, tipo, null, null, valor, Guid.NewGuid()));
    }

    [Fact]
    public async Task Filtro_solo_sin_pago_trae_sin_registrar_y_cuota_copago_sin_valor()
    {
        await using var ctx = NewCtx();
        var pac = new Paciente { Id = Guid.NewGuid(), TenantId = Tenant, TipoDocumento = "CC", NumeroDocumento = "77", NombreCompleto = "PAC 77" };
        ctx.Pacientes.Add(pac);
        var sinRegistrar = NuevaAsig(pac.Id);
        var copagoSinValor = NuevaAsig(pac.Id, "COPAGO", null);
        var cuotaOk = NuevaAsig(pac.Id, "CUOTA", 4500m);
        var sinCargo = NuevaAsig(pac.Id, "SIN_CARGO");
        ctx.Asignaciones.AddRange(sinRegistrar, copagoSinValor, cuotaOk, sinCargo);
        await ctx.SaveChangesAsync();

        var filtro = new AsignacionListadoFiltro(null, null, null, null, null, null, null, SoloSinPago: true);
        var res = await Svc(ctx).ListarAsignacionesAsync(filtro);

        var ids = res.Select(r => r.Id).ToHashSet();
        Assert.Equal(2, ids.Count);
        Assert.Contains(sinRegistrar.Id, ids);
        Assert.Contains(copagoSinValor.Id, ids);

        var todos = await Svc(ctx).ListarAsignacionesAsync(filtro with { SoloSinPago = false });
        var fila = todos.Single(r => r.Id == cuotaOk.Id);
        Assert.Equal("CUOTA", fila.TipoPago);
        Assert.Equal(4500m, fila.ValorPagoReal);
    }
}

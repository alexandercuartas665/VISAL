using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.CamposDinamicos;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests de la colocacion de campos FIJOS de la ficha de admision (Fase 2): siembra, ocultar
/// con guarda de obligatorios, ancho fijo en compuestos, mover de tab con orden unificado y
/// reorden mixto (fijos + dinamicos).
/// </summary>
public sealed class CamposRigidosServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private static VisalDbContext NewCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });
    }

    private static PacienteCampoRigidoService Svc(VisalDbContext db) =>
        new(db, new FakeTenantContext { TenantId = Tenant });

    [Fact]
    public async Task EnsureDefaults_siembra_una_fila_por_campo_del_catalogo_y_es_idempotente()
    {
        await using var db = NewCtx(nameof(EnsureDefaults_siembra_una_fila_por_campo_del_catalogo_y_es_idempotente));
        var svc = Svc(db);

        await svc.EnsureDefaultsAsync();
        await svc.EnsureDefaultsAsync(); // segunda vez: no duplica

        var filas = await svc.ListAsync();
        Assert.Equal(CamposRigidosCatalogo.Todos.Count, filas.Count);

        var doc = filas.Single(f => f.CampoKey == "numero_documento");
        Assert.Equal("generales", doc.TabKey);
        Assert.False(doc.Oculto);
        Assert.False(doc.Ocultable); // obligatorio

        var contratos = filas.Single(f => f.CampoKey == "contratos");
        Assert.True(contratos.Compuesto);
        Assert.Equal(3, contratos.Ancho); // compuesto => completo
    }

    [Fact]
    public async Task SetOculto_rechaza_obligatorio_y_permite_ocultable()
    {
        await using var db = NewCtx(nameof(SetOculto_rechaza_obligatorio_y_permite_ocultable));
        var svc = Svc(db);
        await svc.EnsureDefaultsAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetOcultoAsync("numero_documento", true));

        await svc.SetOcultoAsync("tutela", true);
        var tutela = (await svc.ListAsync()).Single(f => f.CampoKey == "tutela");
        Assert.True(tutela.Oculto);
    }

    [Fact]
    public async Task CicloAncho_ignora_compuesto_y_cicla_simple()
    {
        await using var db = NewCtx(nameof(CicloAncho_ignora_compuesto_y_cicla_simple));
        var svc = Svc(db);
        await svc.EnsureDefaultsAsync();

        await svc.CicloAnchoAsync("contratos"); // compuesto: sin efecto
        Assert.Equal(3, (await svc.ListAsync()).Single(f => f.CampoKey == "contratos").Ancho);

        // tipo_documento arranca en 1 -> 2
        await svc.CicloAnchoAsync("tipo_documento");
        Assert.Equal(2, (await svc.ListAsync()).Single(f => f.CampoKey == "tipo_documento").Ancho);
    }

    [Fact]
    public async Task MoverATab_coloca_al_final_del_orden_unificado_del_destino()
    {
        await using var db = NewCtx(nameof(MoverATab_coloca_al_final_del_orden_unificado_del_destino));
        // Tab destino + un campo dinamico ya colocado en Orden 5.
        db.PacienteFichaDefs.Add(new PacienteFichaDef { TenantId = Tenant, TabKey = "extra", Titulo = "Extra", Orden = 9 });
        db.PacienteCampoDefs.Add(new PacienteCampoDef { TenantId = Tenant, TabKey = "extra", CampoKey = "din1", Label = "Din 1", Orden = 5 });
        await db.SaveChangesAsync();

        var svc = Svc(db);
        await svc.EnsureDefaultsAsync();

        await svc.MoverATabAsync("grupo_rh", "extra");

        var rh = (await svc.ListAsync()).Single(f => f.CampoKey == "grupo_rh");
        Assert.Equal("extra", rh.TabKey);
        Assert.Equal(6, rh.Orden); // max(din=5) + 1
    }

    [Fact]
    public async Task ReordenarTab_reescribe_orden_mixto_de_fijos_y_dinamicos()
    {
        await using var db = NewCtx(nameof(ReordenarTab_reescribe_orden_mixto_de_fijos_y_dinamicos));
        db.PacienteFichaDefs.Add(new PacienteFichaDef { TenantId = Tenant, TabKey = "generales", Titulo = "Generales", Orden = 0 });
        var din = new PacienteCampoDef { TenantId = Tenant, TabKey = "generales", CampoKey = "din1", Label = "Din 1", Orden = 99 };
        db.PacienteCampoDefs.Add(din);
        await db.SaveChangesAsync();

        var svc = Svc(db);
        await svc.EnsureDefaultsAsync();

        // Nuevo orden: dinamico primero, luego numero_documento, luego tipo_documento.
        var items = new List<ItemColocacion>
        {
            new(false, din.Id, null),
            new(true, null, "numero_documento"),
            new(true, null, "tipo_documento"),
        };
        await svc.ReordenarTabAsync("generales", items);

        var dinRow = await db.PacienteCampoDefs.SingleAsync(c => c.CampoKey == "din1");
        var docRow = await db.PacienteCampoRigidoDefs.SingleAsync(r => r.CampoKey == "numero_documento");
        var tipoRow = await db.PacienteCampoRigidoDefs.SingleAsync(r => r.CampoKey == "tipo_documento");
        Assert.Equal(0, dinRow.Orden);
        Assert.Equal(1, docRow.Orden);
        Assert.Equal(2, tipoRow.Orden);
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del modelo N:M paciente_contratos (post-PC4, sin slots viejos).
/// Verifica:
///   - Save con lista N crea filas en paciente_contratos por Orden.
///   - Resave con lista distinta borra + recrea (dedup por unique).
///   - Save con lista vacia limpia todas las filas.
///   - GetPacienteAsync de /asignacion devuelve la lista N ordenada.
///   - Paciente sin contratos devuelve Contratos vacio (sin fallback a slots
///     porque los slots ya no existen).
/// </summary>
public sealed class PacienteContratosTests
{
    private static readonly Guid Tenant = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue,
            Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) { }
    }

    private static VisalDbContext NewCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });
    }

    private static async Task<List<Guid>> SembrarContratos(VisalDbContext db, int n)
    {
        var ase = new Aseguradora { Id = Guid.NewGuid(), TenantId = Tenant, Codigo = "EPS-1", Nombre = "EPS Test" };
        db.Aseguradoras.Add(ase);
        var ids = new List<Guid>();
        for (int i = 1; i <= n; i++)
        {
            var c = new ContratoAseguradora
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                AseguradoraId = ase.Id,
                CodigoContrato = $"C-{i:D3}",
                Estado = "Activo",
                RequierePdfAutorizacion = false
            };
            db.ContratosAseguradora.Add(c);
            ids.Add(c.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    }

    private static SavePacienteRequest Req(Guid? id, string doc,
        IReadOnlyList<PacienteContratoDto> contratos)
    {
        return new SavePacienteRequest(
            id, doc, "CC",
            "JUAN", null, "PEREZ", null, "JUAN PEREZ",
            null, null,
            null, null, null,
            null, null, null,
            null, null,
            null, null, null, null,
            null, null,
            null, null, null, null, null, null,
            contratos,
            null, null, null,
            null, null, null,
            null, null, null, null,
            null, null, null,
            null, null, null,
            null,
            null, null, null,
            Array.Empty<PacienteContactoEmergenciaDto>(),
            true);
    }

    [Fact]
    public async Task Save_persiste_lista_N_por_orden()
    {
        using var db = NewCtx(nameof(Save_persiste_lista_N_por_orden));
        var cs = await SembrarContratos(db, 3);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        // Envio los contratos en orden 3, 1, 2 -> el DTO devuelve en ese mismo orden.
        var lista = new List<PacienteContratoDto>
        {
            new(null, cs[2], null, null, null, 1),
            new(null, cs[0], null, null, null, 2),
            new(null, cs[1], null, null, null, 3),
        };
        var saved = await svc.SaveAsync(Req(null, "111", lista), Actor);

        Assert.NotNull(saved);
        Assert.Equal(3, saved!.Contratos.Count);
        Assert.Equal(cs[2], saved.Contratos[0].ContratoAseguradoraId);
        Assert.Equal(cs[0], saved.Contratos[1].ContratoAseguradoraId);
        Assert.Equal(cs[1], saved.Contratos[2].ContratoAseguradoraId);
    }

    [Fact]
    public async Task Save_lista_de_5_persiste_los_5()
    {
        using var db = NewCtx(nameof(Save_lista_de_5_persiste_los_5));
        var cs = await SembrarContratos(db, 5);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var lista = cs.Select((cid, i) => new PacienteContratoDto(null, cid, null, null, null, i + 1)).ToList();
        var saved = await svc.SaveAsync(Req(null, "222", lista), Actor);

        Assert.Equal(5, saved!.Contratos.Count);
    }

    [Fact]
    public async Task Resave_con_lista_distinta_reemplaza_filas_previas()
    {
        using var db = NewCtx(nameof(Resave_con_lista_distinta_reemplaza_filas_previas));
        var cs = await SembrarContratos(db, 3);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var lista1 = new List<PacienteContratoDto> { new(null, cs[0], null, null, null, 1), new(null, cs[1], null, null, null, 2) };
        var saved1 = await svc.SaveAsync(Req(null, "333", lista1), Actor);
        Assert.Equal(2, saved1!.Contratos.Count);

        // Segundo save: cambia el orden y elimina cs[1], agrega cs[2].
        var lista2 = new List<PacienteContratoDto> { new(null, cs[2], null, null, null, 1), new(null, cs[0], null, null, null, 2) };
        var saved2 = await svc.SaveAsync(Req(saved1.Id, "333", lista2), Actor);

        Assert.Equal(2, saved2!.Contratos.Count);
        Assert.Equal(cs[2], saved2.Contratos[0].ContratoAseguradoraId);
        Assert.Equal(cs[0], saved2.Contratos[1].ContratoAseguradoraId);
    }

    [Fact]
    public async Task Save_lista_vacia_limpia_todas_las_filas()
    {
        using var db = NewCtx(nameof(Save_lista_vacia_limpia_todas_las_filas));
        var cs = await SembrarContratos(db, 2);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var saved1 = await svc.SaveAsync(Req(null, "444",
            new List<PacienteContratoDto> { new(null, cs[0], null, null, null, 1) }), Actor);
        Assert.Single(saved1!.Contratos);

        var saved2 = await svc.SaveAsync(Req(saved1.Id, "444",
            Array.Empty<PacienteContratoDto>()), Actor);
        Assert.Empty(saved2!.Contratos);
    }

    [Fact]
    public async Task Asignacion_lee_lista_N_por_orden()
    {
        using var db = NewCtx(nameof(Asignacion_lee_lista_N_por_orden));
        var cs = await SembrarContratos(db, 3);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        // Orden: 2, 3, 1 -> asignacion debe verlos en ese mismo orden.
        var lista = new List<PacienteContratoDto>
        {
            new(null, cs[1], null, null, null, 1),
            new(null, cs[2], null, null, null, 2),
            new(null, cs[0], null, null, null, 3),
        };
        var saved = await svc.SaveAsync(Req(null, "666", lista), Actor);
        Assert.NotNull(saved);

        var asig = new AsignacionService(db, new FakeTenantContext { TenantId = Tenant });
        var pAsig = await asig.GetPacienteAsync(saved!.Id);

        Assert.NotNull(pAsig);
        Assert.Equal(3, pAsig!.Contratos.Count);
        Assert.Equal(cs[1], pAsig.Contratos[0].ContratoId);
        Assert.Equal(cs[2], pAsig.Contratos[1].ContratoId);
        Assert.Equal(cs[0], pAsig.Contratos[2].ContratoId);
    }

    [Fact]
    public async Task Asignacion_paciente_sin_contratos_devuelve_lista_vacia()
    {
        // Post-PC4 ya no hay slots viejos ni fallback: paciente sin filas en
        // paciente_contratos aparece con Contratos vacio en /asignacion.
        using var db = NewCtx(nameof(Asignacion_paciente_sin_contratos_devuelve_lista_vacia));
        await SembrarContratos(db, 3); // creo contratos pero no los asocio al paciente.
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());
        var saved = await svc.SaveAsync(Req(null, "777", Array.Empty<PacienteContratoDto>()), Actor);

        var asig = new AsignacionService(db, new FakeTenantContext { TenantId = Tenant });
        var pAsig = await asig.GetPacienteAsync(saved!.Id);

        Assert.NotNull(pAsig);
        Assert.Empty(pAsig!.Contratos);
    }
}

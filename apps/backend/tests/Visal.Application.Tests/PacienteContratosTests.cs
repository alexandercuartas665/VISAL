using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del modelo N:M paciente_contratos (PC-1..PC-3). Verifica:
///   - Save con lista Contratos deriva los slots 1/2/3 en el orden correcto
///     (dual-write hasta que PC-4 borre los slots).
///   - Sincronizacion: al reguardar con lista distinta se refresca la tabla.
///   - Contratos != null pero vacio limpia la tabla y los slots.
///   - Contratos == null preserva los slots del request (payload viejo).
///   - GetPacienteAsync de /asignacion lee la tabla N ordenada por Orden y
///     cae en fallback a los slots si la tabla esta vacia.
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

    /// <summary>Siembra una aseguradora + N contratos activos. Devuelve la lista
    /// de contratos creados en el orden pedido.</summary>
    private static async Task<List<Guid>> SembrarContratos(VisalDbContext db, int n, string prefijoCodigo = "C-")
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
                CodigoContrato = $"{prefijoCodigo}{i:D3}",
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
        IReadOnlyList<PacienteContratoDto>? contratos,
        Guid? c1 = null, Guid? c2 = null, Guid? c3 = null)
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
            c1, c2, c3,
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
    public async Task Save_con_lista_N_deriva_slots_en_orden()
    {
        using var db = NewCtx(nameof(Save_con_lista_N_deriva_slots_en_orden));
        var cs = await SembrarContratos(db, 3);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        // Envio los contratos en orden 3, 1, 2 -> los slots deben quedar 3, 1, 2.
        var lista = new List<PacienteContratoDto>
        {
            new(null, cs[2], null, null, null, 1),
            new(null, cs[0], null, null, null, 2),
            new(null, cs[1], null, null, null, 3),
        };
        var saved = await svc.SaveAsync(Req(null, "111", lista), Actor);

        Assert.NotNull(saved);
        Assert.Equal(cs[2], saved!.Contrato1Id);
        Assert.Equal(cs[0], saved.Contrato2Id);
        Assert.Equal(cs[1], saved.Contrato3Id);
        Assert.Equal(3, saved.Contratos.Count);
        Assert.Equal(cs[2], saved.Contratos[0].ContratoAseguradoraId);
    }

    [Fact]
    public async Task Save_lista_de_5_solo_pobla_los_3_slots_y_persiste_los_5_en_tabla()
    {
        using var db = NewCtx(nameof(Save_lista_de_5_solo_pobla_los_3_slots_y_persiste_los_5_en_tabla));
        var cs = await SembrarContratos(db, 5);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var lista = cs.Select((cid, i) => new PacienteContratoDto(null, cid, null, null, null, i + 1)).ToList();
        var saved = await svc.SaveAsync(Req(null, "222", lista), Actor);

        Assert.Equal(cs[0], saved!.Contrato1Id);
        Assert.Equal(cs[1], saved.Contrato2Id);
        Assert.Equal(cs[2], saved.Contrato3Id);
        Assert.Equal(5, saved.Contratos.Count);
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
        Assert.Equal(cs[2], saved2.Contrato1Id);
        Assert.Equal(cs[0], saved2.Contrato2Id);
        Assert.Null(saved2.Contrato3Id);
    }

    [Fact]
    public async Task Save_lista_vacia_limpia_tabla_y_slots()
    {
        using var db = NewCtx(nameof(Save_lista_vacia_limpia_tabla_y_slots));
        var cs = await SembrarContratos(db, 2);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var saved1 = await svc.SaveAsync(Req(null, "444",
            new List<PacienteContratoDto> { new(null, cs[0], null, null, null, 1) }), Actor);
        Assert.NotNull(saved1!.Contrato1Id);

        var saved2 = await svc.SaveAsync(Req(saved1.Id, "444",
            Array.Empty<PacienteContratoDto>()), Actor);
        Assert.Null(saved2!.Contrato1Id);
        Assert.Null(saved2.Contrato2Id);
        Assert.Null(saved2.Contrato3Id);
        Assert.Empty(saved2.Contratos);
    }

    [Fact]
    public async Task Save_con_lista_null_respeta_slots_del_request()
    {
        // Simula caller viejo (import Excel) que aun pasa slots posicionales.
        using var db = NewCtx(nameof(Save_con_lista_null_respeta_slots_del_request));
        var cs = await SembrarContratos(db, 2);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());

        var saved = await svc.SaveAsync(Req(null, "555", null, c1: cs[0], c2: cs[1]), Actor);

        Assert.Equal(cs[0], saved!.Contrato1Id);
        Assert.Equal(cs[1], saved.Contrato2Id);
        // Sin lista N -> paciente_contratos queda vacia (dependera de PC-3
        // fallback a slots para /asignacion).
        Assert.Empty(saved.Contratos);
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
    public async Task Asignacion_fallback_a_slots_si_tabla_N_vacia()
    {
        // Paciente creado con slots pero sin filas en paciente_contratos
        // (payload viejo o dato manual): /asignacion cae al fallback y
        // devuelve los 3 slots en orden 1/2/3.
        using var db = NewCtx(nameof(Asignacion_fallback_a_slots_si_tabla_N_vacia));
        var cs = await SembrarContratos(db, 3);
        var svc = new PacienteService(db, new FakeTenantContext { TenantId = Tenant }, new NoopAudit());
        var saved = await svc.SaveAsync(Req(null, "777", null, c1: cs[2], c2: cs[0]), Actor);

        var asig = new AsignacionService(db, new FakeTenantContext { TenantId = Tenant });
        var pAsig = await asig.GetPacienteAsync(saved!.Id);

        Assert.NotNull(pAsig);
        Assert.Equal(2, pAsig!.Contratos.Count);
        Assert.Equal(cs[2], pAsig.Contratos[0].ContratoId);
        Assert.Equal(cs[0], pAsig.Contratos[1].ContratoId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Visal.Application.Common;
using Visal.Application.Facturacion.Selectors;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

/// <summary>
/// Tests del selector v3 (unidad base = HistoriaClinica.Estado=Cerrada).
/// Contrato del v3:
///   - 1 hecho = 1 HC cerrada cuyo fecha_cierre cae en [FechaInicio, FechaFin].
///   - Filtro EPS: el paciente tiene algun contrato de esa EPS en la tabla
///     paciente_contratos (post-PC1) o en los slots 1/2/3 (fallback).
///   - Filtro Sucursal: SedeAtencionId del paciente debe estar en la lista.
///   - Gate revision: si Sucursal.ExigirHcRevisadaParaFacturar, la MISMA HC
///     debe tener RevisionClinica en estado Aprobada/ArchivadaOk.
/// </summary>
public sealed class RelacionFacturasSelectorV3Tests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private static VisalDbContext NewCtx()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = TenantId, UserId = TenantId });
    }

    /// <summary>Setup minimo: 1 aseguradora + 1 contrato + 1 sucursal + 1 profesional.
    /// Los tests siembran encima los pacientes/HCs que necesitan.</summary>
    private sealed record Setup(VisalDbContext Ctx, Guid AseguradoraId, Guid ContratoId, Guid SucursalId, Guid ProfesionalId);

    private static async Task<Setup> SembrarBaseAsync(bool exigirRevision = false, Guid? ctxOverride = null)
    {
        var ctx = NewCtx();
        var ase = new Aseguradora { Id = Guid.NewGuid(), TenantId = TenantId, Codigo = "EPS-1", Nombre = "EPS TEST", Nit = "900000001" };
        var contrato = new ContratoAseguradora { Id = Guid.NewGuid(), TenantId = TenantId, AseguradoraId = ase.Id, CodigoContrato = "CON-001", Estado = "Activo" };
        var suc = new Sucursal { Id = Guid.NewGuid(), TenantId = TenantId, Codigo = "S1", Nombre = "SEDE 1", Activo = true, ExigirHcRevisadaParaFacturar = exigirRevision };
        var prof = new Profesional { Id = Guid.NewGuid(), TenantId = TenantId, NumeroDocumento = "1", NombreCompleto = "DR TEST" };
        ctx.Aseguradoras.Add(ase);
        ctx.ContratosAseguradora.Add(contrato);
        ctx.Sucursales.Add(suc);
        ctx.Profesionales.Add(prof);
        await ctx.SaveChangesAsync();
        return new Setup(ctx, ase.Id, contrato.Id, suc.Id, prof.Id);
    }

    /// <summary>Siembra un paciente con contrato en la EPS y opcional sede.</summary>
    private static async Task<Guid> SembrarPacienteAsync(Setup s, Guid contratoId, Guid? sedeId = null, string doc = "1")
    {
        var p = new Paciente
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            TipoDocumento = "CC", NumeroDocumento = doc,
            NombreCompleto = "PAC " + doc,
            AseguradoraId = s.AseguradoraId,
            SedeAtencionId = sedeId
        };
        s.Ctx.Pacientes.Add(p);
        // Post-PC4: el contrato del paciente se persiste en paciente_contratos,
        // no en slots fijos. Sembramos una fila orden=1 apuntando al contrato.
        s.Ctx.PacienteContratos.Add(new PacienteContrato
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            PacienteId = p.Id,
            ContratoAseguradoraId = contratoId,
            Orden = 1
        });
        await s.Ctx.SaveChangesAsync();
        return p.Id;
    }

    /// <summary>Crea una HC cerrada en la fecha dada.</summary>
    private static async Task<Guid> SembrarHcCerradaAsync(Setup s, Guid pacienteId, DateTimeOffset fechaCierre)
    {
        var hc = new HistoriaClinica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PacienteId = pacienteId,
            ProfesionalId = s.ProfesionalId,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaCierre = fechaCierre
        };
        s.Ctx.HistoriasClinicas.Add(hc);
        await s.Ctx.SaveChangesAsync();
        return hc.Id;
    }

    private static RelacionFacturasFiltros Filtros(Setup s, DateOnly ini, DateOnly fin, IReadOnlyList<Guid>? sedes = null)
        => new(s.AseguradoraId, sedes, ini, fin);

    [Fact]
    public async Task Devuelve_un_hecho_por_HC_cerrada_en_rango()
    {
        var s = await SembrarBaseAsync();
        var pid = await SembrarPacienteAsync(s, s.ContratoId, s.SucursalId);
        await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));

        var sel = new RelacionFacturasSelector(s.Ctx);
        var hechos = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        Assert.Single(hechos);
        Assert.Equal(pid, hechos[0].Paciente.Id);
        Assert.Equal(s.AseguradoraId, hechos[0].Aseguradora.Id);
        Assert.Equal(s.ContratoId, hechos[0].Contrato.Id);
    }

    [Fact]
    public async Task Excluye_HCs_fuera_del_rango_de_fecha_cierre()
    {
        var s = await SembrarBaseAsync();
        var pid = await SembrarPacienteAsync(s, s.ContratoId, s.SucursalId);
        // 3 HCs: una antes, una dentro, una despues del rango 2026-07.
        await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 6, 30, 23, 0, 0, TimeSpan.Zero));
        await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 8, 1, 0, 30, 0, TimeSpan.Zero));

        var sel = new RelacionFacturasSelector(s.Ctx);
        var hechos = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        Assert.Single(hechos);
    }

    [Fact]
    public async Task Excluye_HC_de_paciente_sin_contrato_de_esa_EPS()
    {
        var s = await SembrarBaseAsync();
        // Segunda EPS distinta con su propio contrato — el paciente tiene solo el
        // contrato de esta EPS "B", no de la que estamos filtrando.
        var epsB = new Aseguradora { Id = Guid.NewGuid(), TenantId = TenantId, Codigo = "EPS-B", Nombre = "OTRA" };
        var contratoB = new ContratoAseguradora { Id = Guid.NewGuid(), TenantId = TenantId, AseguradoraId = epsB.Id, CodigoContrato = "B-1", Estado = "Activo" };
        s.Ctx.Aseguradoras.Add(epsB);
        s.Ctx.ContratosAseguradora.Add(contratoB);
        await s.Ctx.SaveChangesAsync();

        // Paciente solo con contrato de EPS "B".
        var pid = await SembrarPacienteAsync(s, contratoB.Id, s.SucursalId, doc: "99");
        await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));

        var sel = new RelacionFacturasSelector(s.Ctx);
        var hechos = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        Assert.Empty(hechos);
    }

    [Fact]
    public async Task Filtro_sucursal_excluye_pacientes_de_otra_sede()
    {
        var s = await SembrarBaseAsync();
        // Segunda sucursal.
        var suc2 = new Sucursal { Id = Guid.NewGuid(), TenantId = TenantId, Codigo = "S2", Nombre = "OTRA SEDE", Activo = true };
        s.Ctx.Sucursales.Add(suc2);
        await s.Ctx.SaveChangesAsync();

        var pidS1 = await SembrarPacienteAsync(s, s.ContratoId, s.SucursalId, doc: "S1");
        var pidS2 = await SembrarPacienteAsync(s, s.ContratoId, suc2.Id, doc: "S2");
        await SembrarHcCerradaAsync(s, pidS1, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        await SembrarHcCerradaAsync(s, pidS2, new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero));

        var sel = new RelacionFacturasSelector(s.Ctx);
        // Solo sede 1 -> devuelve 1 hecho (el de S1).
        var hechos = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), new[] { s.SucursalId }));

        Assert.Single(hechos);
        Assert.Equal(pidS1, hechos[0].Paciente.Id);
    }

    [Fact]
    public async Task Gate_revision_excluye_HC_no_aprobada_si_sede_lo_exige()
    {
        var s = await SembrarBaseAsync(exigirRevision: true);
        var pid = await SembrarPacienteAsync(s, s.ContratoId, s.SucursalId);
        var hcId = await SembrarHcCerradaAsync(s, pid, new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        // NO creo RevisionClinica -> la HC no esta aprobada.

        var sel = new RelacionFacturasSelector(s.Ctx);
        var hechos = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        // Sede exige revision y no hay -> hecho excluido.
        Assert.Empty(hechos);

        // Ahora agrego la revision aprobada y espero que aparezca.
        s.Ctx.RevisionesClinica.Add(new RevisionClinica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            HistoriaClinicaId = hcId,
            EstadoAgregado = RevisionEstadoAgregado.Aprobada,
            SolicitadaEn = DateTimeOffset.UtcNow,
            UltimaAccionEn = DateTimeOffset.UtcNow
        });
        await s.Ctx.SaveChangesAsync();

        var hechos2 = await sel.SelectAsync(Filtros(s, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));
        Assert.Single(hechos2);
    }
}

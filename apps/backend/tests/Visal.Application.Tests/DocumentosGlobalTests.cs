using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Application.Tenancy.Menu;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Modulo global "Documentos" (consulta de anexos de todos los pacientes) + enlace de un
/// archivo suelto a una historia clinica + ubicacion del item nuevo en un menu ya guardado.
/// </summary>
public sealed class DocumentosGlobalTests
{
    private static readonly Guid Tenant = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public int Escrituras;
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) => Escrituras++;
        public void Write(IApplicationDbContext db, Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) => Escrituras++;
    }

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static NotaMedicaService Svc(VisalDbContext ctx, NoopAudit? audit = null) =>
        new(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }),
            new FakeTenantContext { TenantId = Tenant }, null!, audit ?? new NoopAudit());

    private sealed record Semilla(Guid Pac1, Guid Pac2, Guid Hc1, Guid Hc2, Guid DocFirmasSuelto, Guid DocConHc, Guid DocConsent, Guid DocSinCat);

    private static async Task<Semilla> SembrarAsync(VisalDbContext ctx)
    {
        var p1 = new Paciente { TenantId = Tenant, NumeroDocumento = "1111", NombreCompleto = "ANA PEREZ" };
        var p2 = new Paciente { TenantId = Tenant, NumeroDocumento = "2222", NombreCompleto = "LUIS GOMEZ" };
        ctx.Pacientes.AddRange(p1, p2);
        // La HC exige formato (navegacion requerida -> INNER JOIN en las proyecciones).
        var fd = new FormDefinition { TenantId = Tenant, Codigo = "HC-FO-08", Nombre = "HC GENERAL" };
        ctx.FormDefinitions.Add(fd);
        var hc1 = new HistoriaClinica { TenantId = Tenant, PacienteId = p1.Id, FormDefinitionId = fd.Id, Consecutivo = 101, Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero), FechaAtencion = new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero) };
        var hc2 = new HistoriaClinica { TenantId = Tenant, PacienteId = p2.Id, FormDefinitionId = fd.Id, Consecutivo = 202, Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero) };
        ctx.HistoriasClinicas.AddRange(hc1, hc2);

        var dFirmas = new NotaMedicaDocumento { TenantId = Tenant, PacienteId = p1.Id, NombreOriginal = "firmas nov.pdf", RutaArchivo = "/u/1.pdf",
            Categoria = "Lista de firmas", CreatedAt = new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero) };
        var dConHc = new NotaMedicaDocumento { TenantId = Tenant, PacienteId = p1.Id, HistoriaClinicaId = hc1.Id, NombreOriginal = "otra lista.pdf",
            RutaArchivo = "/u/2.pdf", Categoria = "LISTA DE FIRMAS", CreatedAt = new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero) };
        var dConsent = new NotaMedicaDocumento { TenantId = Tenant, PacienteId = p2.Id, NombreOriginal = "consentimiento.pdf", RutaArchivo = "/u/3.pdf",
            Categoria = "Consentimiento", Subcategoria = "Terapias", Anotaciones = "firmado por acudiente",
            CreatedAt = new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero) };
        var dSinCat = new NotaMedicaDocumento { TenantId = Tenant, PacienteId = p2.Id, NombreOriginal = "scan.jpg", RutaArchivo = "/u/4.jpg",
            CreatedAt = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero) };
        ctx.NotaMedicaDocumentos.AddRange(dFirmas, dConHc, dConsent, dSinCat);
        await ctx.SaveChangesAsync();
        return new Semilla(p1.Id, p2.Id, hc1.Id, hc2.Id, dFirmas.Id, dConHc.Id, dConsent.Id, dSinCat.Id);
    }

    [Fact]
    public async Task Global_sin_filtros_trae_todos_con_paciente_y_hc_de_origen()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        var r = await Svc(ctx).ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro());
        Assert.Equal(4, r.Total);
        Assert.Equal(4, r.Items.Count);
        var conHc = r.Items.Single(d => d.Id == s.DocConHc);
        Assert.Equal("HC-000101", conHc.CodigoHc);
        Assert.Equal("ANA PEREZ", conHc.PacienteNombre);
        Assert.Equal(s.Pac1, conHc.PacienteId);
        Assert.Null(r.Items.Single(d => d.Id == s.DocFirmasSuelto).CodigoHc);
    }

    [Fact]
    public async Task Global_filtra_categoria_sin_distinguir_mayusculas_subcategoria_y_sin_categoria()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        var svc = Svc(ctx);
        var firmas = await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Categoria: "Lista de firmas"));
        Assert.Equal(new[] { s.DocFirmasSuelto, s.DocConHc }.OrderBy(x => x), firmas.Items.Select(d => d.Id).OrderBy(x => x));
        var sub = await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Categoria: "Consentimiento", Subcategoria: "terapias"));
        Assert.Equal(s.DocConsent, Assert.Single(sub.Items).Id);
        var sinCat = await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Categoria: DocumentosGlobalFiltro.SinCategoria));
        Assert.Equal(s.DocSinCat, Assert.Single(sinCat.Items).Id);
    }

    [Fact]
    public async Task Global_filtra_historia_paciente_texto_y_fechas()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        var svc = Svc(ctx);
        Assert.Equal(s.DocConHc, Assert.Single((await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Historia: "con"))).Items).Id);
        Assert.Equal(3, (await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Historia: "sin"))).Total);
        Assert.Equal(2, (await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Paciente: "2222"))).Total);
        Assert.Equal(2, (await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Paciente: "ana"))).Total);
        Assert.Equal(s.DocConsent, Assert.Single((await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Texto: "ACUDIENTE"))).Items).Id);
        var nov = await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Desde: new DateOnly(2026, 11, 1), Hasta: new DateOnly(2026, 11, 30)));
        Assert.Equal(3, nov.Total);
    }

    [Fact]
    public async Task Global_pagina_y_mantiene_el_total()
    {
        await using var ctx = NewCtx();
        await SembrarAsync(ctx);
        for (var i = 0; i < 12; i++)
        {
            ctx.NotaMedicaDocumentos.Add(new NotaMedicaDocumento { TenantId = Tenant, PacienteId = Guid.NewGuid(), NombreOriginal = $"x{i}.pdf", RutaArchivo = "/u/x.pdf" });
        }
        await ctx.SaveChangesAsync();
        var p2 = await Svc(ctx).ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Pagina: 2, TamanoPagina: 10));
        Assert.Equal(16, p2.Total);
        Assert.Equal(6, p2.Items.Count);
    }

    [Fact]
    public async Task Enlazar_suelto_a_hc_del_mismo_paciente_queda_auditado_y_no_se_repite()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        var audit = new NoopAudit();
        var svc = Svc(ctx, audit);
        await svc.EnlazarDocumentoAHistoriaAsync(s.DocFirmasSuelto, s.Hc1, usarFechaAtencion: true, Guid.NewGuid());
        var r = await svc.ListarDocumentosGlobalAsync(new DocumentosGlobalFiltro(Historia: "con"));
        var d = r.Items.Single(x => x.Id == s.DocFirmasSuelto);
        Assert.Equal("HC-000101", d.CodigoHc);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero), d.FechaDocumento);
        Assert.Equal(1, audit.Escrituras);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EnlazarDocumentoAHistoriaAsync(s.DocFirmasSuelto, s.Hc1, false, Guid.NewGuid()));
    }

    [Fact]
    public async Task Enlazar_a_hc_de_otro_paciente_falla()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Svc(ctx).EnlazarDocumentoAHistoriaAsync(s.DocFirmasSuelto, s.Hc2, false, Guid.NewGuid()));
    }

    [Fact]
    public async Task Hcs_enlazables_cuentan_listas_de_firmas()
    {
        await using var ctx = NewCtx();
        var s = await SembrarAsync(ctx);
        var hcs = await Svc(ctx).ListarHcEnlazablesAsync(s.Pac1);
        Assert.Equal(1, Assert.Single(hcs).ListasFirmas);
    }

    [Fact]
    public async Task Menu_guardado_ubica_Documentos_justo_debajo_de_Admision()
    {
        await using var ctx = NewCtx();
        var svc = new NavMenuConfigService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }));
        // Menu personalizado guardado ANTES de existir el modulo "documentos".
        await svc.SaveAsync(new NavMenuTreeDto
        {
            Groups = new()
            {
                new NavMenuGroupDto
                {
                    Id = "operacion-clinica", Label = "Operacion Clinica", IconKey = "carpeta",
                    Items = new()
                    {
                        new NavMenuItemDto { Key = "admision", Label = "Admision", IconKey = "admision" },
                        new NavMenuItemDto { Key = "asignacion", Label = "Asignacion", IconKey = "portapapeles" },
                    },
                },
            },
        });
        var tree = await svc.GetForEditAsync();
        var keys = tree.Groups.Single(g => g.Label == "Operacion Clinica").Items.Select(i => i.Key).ToList();
        Assert.Equal(new[] { "admision", "documentos", "asignacion" }, keys.Take(3));
    }
}

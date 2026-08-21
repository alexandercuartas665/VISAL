using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica el volcado del resumen de una ESCALA al campo analisis de la HC al
/// CERRARLA (EscalaService.CerrarAsync + origen de prefill "escalas.resumen").
/// Reglas cubiertas:
///  - Con la ruta escalas.resumen -> [campo] configurada en el formato de la HC,
///    al cerrar la escala se ACUMULA en ese campo un bloque:
///      [NOMBRE ESCALA - dd/MM/yyyy]
///      TOTAL: 90
///      Grado de dependencia: DEPENDENCIA LEVE
///    tomando solo los campos de la seccion RESULTADO.
///  - Varias escalas ACUMULAN (append) sin pisar el bloque previo ni el texto
///    que el medico ya tenia en el campo.
///  - Sin ruta configurada, cerrar la escala NO toca la HC.
/// </summary>
public sealed class EscalaResumenTests
{
    private static readonly Guid Tenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

    private const string EscalaSchema = """
    {"children":[
      {"type":"section","label":"INDICE BARTHEL","children":[
        {"type":"field","name":"comer","label":"Comer","fieldType":"select"}
      ]},
      {"type":"section","label":"RESULTADO","children":[
        {"type":"field","name":"barthel_total","label":"TOTAL (suma 0 - 100)","fieldType":"calculated"},
        {"type":"field","name":"barthel_clasificacion","label":"Grado de dependencia (automatico)","fieldType":"calculated"}
      ]},
      {"type":"section","label":"OBSERVACIONES Y FIRMA","children":[
        {"type":"field","name":"firma","label":"Firma","fieldType":"text"}
      ]}
    ]}
    """;

    private const string RutaEscalas =
        """{"routes":[{"id":"escres01","name":"Escalas","sourceModule":"escalas","mappings":[{"source":"resumen","target":"analisis"}]}]}""";

    private static async Task<(Guid hcId, Guid escalaFormId)> SembrarAsync(
        VisalDbContext ctx, string? rutaHc, string? analisisInicial = null)
    {
        var hcForm = new FormDefinition
        {
            TenantId = Tenant, Codigo = "HC-FO-08", Nombre = "HC GENERAL", Tipo = "HC",
            SchemaJson = "{\"children\":[]}", PrefillRoutesJson = rutaHc
        };
        var escForm = new FormDefinition
        {
            TenantId = Tenant, Codigo = "PP-FO-46", Nombre = "ESCALA BARTHEL", Tipo = "ESCALAS",
            SchemaJson = EscalaSchema
        };
        ctx.FormDefinitions.AddRange(hcForm, escForm);

        var valoresHc = analisisInicial is null
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, string?> { ["analisis"] = analisisInicial });
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = Guid.NewGuid(), FormDefinitionId = hcForm.Id,
            Estado = HistoriaClinicaEstado.Abierta, ValoresJson = valoresHc,
            FechaApertura = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)
        };
        ctx.HistoriasClinicas.Add(hc);
        await ctx.SaveChangesAsync();
        return (hc.Id, escForm.Id);
    }

    private static async Task<Guid> SembrarEscalaAbiertaAsync(
        VisalDbContext ctx, Guid hcId, Guid escFormId, string total, string grado)
    {
        var valores = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["comer"] = "10 - Independiente",
            ["barthel_total"] = total,
            ["barthel_clasificacion"] = grado,
            ["firma"] = "NO APLICA"
        });
        var e = new HistoriaClinicaEscala
        {
            TenantId = Tenant, HistoriaClinicaId = hcId, FormDefinitionId = escFormId,
            ValoresJson = valores, Estado = HistoriaClinicaEstado.Abierta,
            FechaApertura = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)
        };
        ctx.HistoriaClinicaEscalas.Add(e);
        await ctx.SaveChangesAsync();
        return e.Id;
    }

    private static string? LeerAnalisis(VisalDbContext ctx, Guid hcId)
    {
        var json = ctx.HistoriasClinicas.AsNoTracking().First(h => h.Id == hcId).ValoresJson;
        var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new();
        return dict.TryGetValue("analisis", out var v) ? v : null;
    }

    [Fact]
    public async Task Cerrar_con_ruta_vuelca_resumen_de_seccion_RESULTADO_al_analisis()
    {
        await using var ctx = NewCtx();
        var (hcId, escFormId) = await SembrarAsync(ctx, RutaEscalas);
        var escalaId = await SembrarEscalaAbiertaAsync(ctx, hcId, escFormId, "90", "DEPENDENCIA LEVE");

        var svc = new EscalaService(ctx, new FakeTenantContext { TenantId = Tenant });
        var ok = await svc.CerrarAsync(escalaId, "", Guid.NewGuid());

        Assert.True(ok);
        var analisis = LeerAnalisis(ctx, hcId);
        Assert.NotNull(analisis);
        Assert.Contains("ESCALA BARTHEL", analisis);                                   // encabezado con nombre
        Assert.Contains("TOTAL (suma 0 - 100): 90", analisis);                         // campo RESULTADO
        Assert.Contains("Grado de dependencia (automatico): DEPENDENCIA LEVE", analisis);
        Assert.DoesNotContain("Comer", analisis);                                      // NO campos de otras secciones
        Assert.DoesNotContain("Firma", analisis);                                      // NO seccion firma
    }

    [Fact]
    public async Task Cerrar_varias_escalas_acumula_sin_pisar_texto_previo()
    {
        await using var ctx = NewCtx();
        var (hcId, escFormId) = await SembrarAsync(ctx, RutaEscalas, analisisInicial: "Paciente estable.");

        var e1 = await SembrarEscalaAbiertaAsync(ctx, hcId, escFormId, "90", "DEPENDENCIA LEVE");
        var e2 = await SembrarEscalaAbiertaAsync(ctx, hcId, escFormId, "40", "DEPENDENCIA MODERADA");

        var svc = new EscalaService(ctx, new FakeTenantContext { TenantId = Tenant });
        await svc.CerrarAsync(e1, "", Guid.NewGuid());
        await svc.CerrarAsync(e2, "", Guid.NewGuid());

        var analisis = LeerAnalisis(ctx, hcId);
        Assert.NotNull(analisis);
        Assert.StartsWith("Paciente estable.", analisis);   // respeta el texto del medico
        Assert.Contains("TOTAL (suma 0 - 100): 90", analisis);
        Assert.Contains("TOTAL (suma 0 - 100): 40", analisis);   // ambos bloques presentes
    }

    [Fact]
    public async Task Cerrar_sin_ruta_configurada_no_toca_la_HC()
    {
        await using var ctx = NewCtx();
        var (hcId, escFormId) = await SembrarAsync(ctx, rutaHc: null);
        var escalaId = await SembrarEscalaAbiertaAsync(ctx, hcId, escFormId, "90", "DEPENDENCIA LEVE");

        var svc = new EscalaService(ctx, new FakeTenantContext { TenantId = Tenant });
        await svc.CerrarAsync(escalaId, "", Guid.NewGuid());

        Assert.Null(LeerAnalisis(ctx, hcId));   // analisis sigue vacio
    }
}

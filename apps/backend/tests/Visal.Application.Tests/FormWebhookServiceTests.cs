using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del webhook publico de formularios web (WordPress -> tarjeta PQRS). Cubre: creacion de
/// tarjeta, idempotencia por ventana, token invalido/deshabilitado (401), campo obligatorio
/// faltante (400), parseo form-urlencoded + form_fields[..] + JSON, y ruteo a la etapa PQRS aunque
/// exista otra etapa como primera.
/// </summary>
public sealed class FormWebhookServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    // Frontera de confianza: el webhook opera sin tenant en el contexto (TenantId = null).
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : throw new FormatException();
    }

    private sealed class FakeAudit : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human)
        { }
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static VisalDbContext NewCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = null });
    }

    private static (FormWebhookService svc, VisalDbContext ctx, FakeTime time) Build(string dbName)
    {
        var ctx = NewCtx(dbName);
        var time = new FakeTime();
        var secret = new FakeSecretProtector();
        var audit = new FakeAudit();
        var api = new TenantApiService(ctx, secret, time, audit);
        var svc = new FormWebhookService(ctx, api, secret, time, audit);
        return (svc, ctx, time);
    }

    private static async Task<string> NewTokenAsync(FormWebhookService svc)
    {
        var cfg = await svc.RegenerateAsync(Tenant, Guid.NewGuid());
        return cfg.Token!;
    }

    [Fact]
    public async Task Process_FormUrlEncoded_CreatesCard_InPqrsStage_WithFields()
    {
        var (svc, ctx, _) = Build(nameof(Process_FormUrlEncoded_CreatesCard_InPqrsStage_WithFields));
        var token = await NewTokenAsync(svc);

        var body = "nombre=Juan+Perez&telefono=573001234567&email=juan%40correo.com&asunto=Queja&mensaje=No+me+atendieron&tipo=pqrs&pagina=https%3A%2F%2Fipsvisalrt.com%2Fpqrs";
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, res.StatusCode);
        Assert.True(res.Ok);
        Assert.NotNull(res.CardId);

        var lead = await ctx.Leads.IgnoreQueryFilters().Include(l => l.Stage).FirstAsync(l => l.Id == res.CardId);
        Assert.Equal(Tenant, lead.TenantId);
        Assert.Equal("Juan Perez", lead.ContactName);
        Assert.Equal("573001234567", lead.ContactPhone);
        Assert.Equal(FormWebhookService.PqrsStageName, lead.Stage!.Name);

        Assert.Contains("juan@correo.com", lead.FieldValuesJson);
        Assert.Contains("No me atendieron", lead.FieldValuesJson);
        Assert.Contains("pagina_origen", lead.FieldValuesJson);

        Assert.Contains(await ctx.LeadActivities.IgnoreQueryFilters().Where(a => a.LeadId == lead.Id).ToListAsync(),
            a => a.ActivityType == "web:pqrs");
    }

    [Fact]
    public async Task Process_SamePayloadTwice_WithinWindow_IsDeduped()
    {
        var (svc, ctx, _) = Build(nameof(Process_SamePayloadTwice_WithinWindow_IsDeduped));
        var token = await NewTokenAsync(svc);
        var body = "nombre=Ana&mensaje=hola&tipo=contacto";

        var first = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);
        var second = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, second.StatusCode);
        Assert.True(second.Duplicate);
        Assert.Equal(first.CardId, second.CardId);
        Assert.Equal(1, await ctx.Leads.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Process_SamePayload_AfterWindow_CreatesNewCard()
    {
        var (svc, ctx, time) = Build(nameof(Process_SamePayload_AfterWindow_CreatesNewCard));
        var token = await NewTokenAsync(svc);
        var body = "nombre=Ana&mensaje=hola";

        await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);
        time.Now = time.Now.AddMinutes(20); // fuera de la ventana de 10 min
        var second = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, second.StatusCode);
        Assert.Equal(2, await ctx.Leads.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Process_InvalidToken_Returns401()
    {
        var (svc, _, _) = Build(nameof(Process_InvalidToken_Returns401));
        await NewTokenAsync(svc); // existe un token valido, pero mandamos otro
        var res = await svc.ProcessAsync("vfw_no_existe", "application/x-www-form-urlencoded", "nombre=X");
        Assert.Equal(401, res.StatusCode);
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Process_DisabledTenant_Returns401()
    {
        var (svc, _, _) = Build(nameof(Process_DisabledTenant_Returns401));
        var token = await NewTokenAsync(svc);
        await svc.SetEnabledAsync(Tenant, false, Guid.NewGuid());
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=X");
        Assert.Equal(401, res.StatusCode);
    }

    [Fact]
    public async Task Process_MissingNombre_Returns400()
    {
        var (svc, _, _) = Build(nameof(Process_MissingNombre_Returns400));
        var token = await NewTokenAsync(svc);
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "email=x%40y.com&mensaje=hola");
        Assert.Equal(400, res.StatusCode);
    }

    [Fact]
    public async Task Process_ElementorFormFieldsKeys_AreNormalized()
    {
        var (svc, ctx, _) = Build(nameof(Process_ElementorFormFieldsKeys_AreNormalized));
        var token = await NewTokenAsync(svc);
        var body = "form_fields%5Bnombre%5D=Pedro&form_fields%5Bemail%5D=pedro%40x.com";

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, res.StatusCode);
        var lead = await ctx.Leads.IgnoreQueryFilters().FirstAsync(l => l.Id == res.CardId);
        Assert.Equal("Pedro", lead.ContactName);
        Assert.Contains("pedro@x.com", lead.FieldValuesJson);
    }

    [Fact]
    public async Task Process_JsonBody_CreatesCard_WithTipoContacto()
    {
        var (svc, ctx, _) = Build(nameof(Process_JsonBody_CreatesCard_WithTipoContacto));
        var token = await NewTokenAsync(svc);
        var body = "{\"nombre\":\"Maria\",\"email\":\"m@x.com\",\"tipo\":\"contacto\"}";

        var res = await svc.ProcessAsync(token, "application/json", body);

        Assert.Equal(201, res.StatusCode);
        var lead = await ctx.Leads.IgnoreQueryFilters().FirstAsync(l => l.Id == res.CardId);
        Assert.Equal("Maria", lead.ContactName);
        Assert.Contains(await ctx.LeadActivities.IgnoreQueryFilters().Where(a => a.LeadId == lead.Id).ToListAsync(),
            a => a.ActivityType == "web:contacto");
    }

    [Fact]
    public async Task Process_RoutesToPqrs_EvenWhenAnotherStageIsFirst()
    {
        var (svc, ctx, _) = Build(nameof(Process_RoutesToPqrs_EvenWhenAnotherStageIsFirst));
        // Etapa preexistente con SortOrder 0 (seria la "primera" por defecto).
        var primera = new PipelineStage { TenantId = Tenant, Name = "Nuevo", SortOrder = 0 };
        ctx.PipelineStages.Add(primera);
        await ctx.SaveChangesAsync();

        var token = await NewTokenAsync(svc);
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Luis");

        Assert.Equal(201, res.StatusCode);
        var lead = await ctx.Leads.IgnoreQueryFilters().Include(l => l.Stage).FirstAsync(l => l.Id == res.CardId);
        Assert.Equal(FormWebhookService.PqrsStageName, lead.Stage!.Name);
        Assert.NotEqual(primera.Id, lead.StageId);
    }

    [Fact]
    public async Task Process_RoutesContactoToConfiguredStage()
    {
        var (svc, ctx, _) = Build(nameof(Process_RoutesContactoToConfiguredStage));
        // Config del tenant: los formularios tipo "contacto" van a la etapa "Contacto".
        ctx.TenantConfigurations.Add(new TenantConfiguration
        {
            TenantId = Tenant,
            ConfigKey = ConfiguracionClinicaService.KeyEtapaFormContacto,
            ConfigValue = "Contacto"
        });
        await ctx.SaveChangesAsync();

        var token = await NewTokenAsync(svc);

        // contacto -> etapa "Contacto"
        var c = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Ana&tipo=contacto");
        var leadC = await ctx.Leads.IgnoreQueryFilters().Include(l => l.Stage).FirstAsync(l => l.Id == c.CardId);
        Assert.Equal("Contacto", leadC.Stage!.Name);

        // pqrs sigue cayendo en "PQRS" (sin config para pqrs)
        var p = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Beto&tipo=pqrs");
        var leadP = await ctx.Leads.IgnoreQueryFilters().Include(l => l.Stage).FirstAsync(l => l.Id == p.CardId);
        Assert.Equal(FormWebhookService.PqrsStageName, leadP.Stage!.Name);
    }
}

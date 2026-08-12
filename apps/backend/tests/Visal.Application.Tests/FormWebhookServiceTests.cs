using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests del webhook publico de formularios web (WordPress -> tarjeta en el modulo Tableros). Cubre:
/// creacion de tarjeta en el tablero por tipo (pqrs -> PQR, contacto -> CONTACTOS), auto-creacion del
/// tablero si falta, idempotencia por ventana, token invalido/deshabilitado (401), campo obligatorio
/// faltante (400), parseo form-urlencoded + form_fields[..] + JSON + Elementor Advanced Data ON, y
/// override del nombre del tablero por configuracion del tenant.
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
        // Frontera de confianza: el webhook opera sin tenant en el contexto (TenantId = null).
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = null });
    }

    private static (FormWebhookService svc, VisalDbContext ctx, FakeTime time) Build(string dbName)
    {
        var ctx = NewCtx(dbName);
        var time = new FakeTime();
        var secret = new FakeSecretProtector();
        var audit = new FakeAudit();
        var svc = new FormWebhookService(ctx, secret, time, audit);
        return (svc, ctx, time);
    }

    private static async Task<string> NewTokenAsync(FormWebhookService svc)
    {
        var cfg = await svc.RegenerateAsync(Tenant, Guid.NewGuid());
        return cfg.Token!;
    }

    // Siembra un tablero con las 4 columnas por defecto. Retorna (boardId, primeraColumnaId).
    private static async Task<(Guid BoardId, Guid FirstColumnId)> SeedBoardAsync(VisalDbContext ctx, string name)
    {
        var board = new TaskBoard { TenantId = Tenant, Name = name, OwnerPlatformUserId = Guid.NewGuid() };
        ctx.TaskBoards.Add(board);
        var cols = new (string Name, int Order, bool Done)[]
        {
            ("Por hacer", 0, false), ("En progreso", 1, false), ("En revision", 2, false), ("Completado", 3, true),
        };
        var firstCol = Guid.Empty;
        foreach (var c in cols)
        {
            var col = new TaskBoardColumn { TenantId = Tenant, BoardId = board.Id, Name = c.Name, SortOrder = c.Order, IsDone = c.Done };
            ctx.TaskBoardColumns.Add(col);
            if (c.Order == 0) { firstCol = col.Id; }
        }
        await ctx.SaveChangesAsync();
        return (board.Id, firstCol);
    }

    private static async Task<TaskCard> GetCardAsync(VisalDbContext ctx, Guid? cardId)
        => await ctx.TaskCards.IgnoreQueryFilters().FirstAsync(c => c.Id == cardId);

    [Fact]
    public async Task Process_FormUrlEncoded_CreatesCard_InPqrBoard_WithData()
    {
        var (svc, ctx, _) = Build(nameof(Process_FormUrlEncoded_CreatesCard_InPqrBoard_WithData));
        var (boardId, firstCol) = await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var token = await NewTokenAsync(svc);

        var body = "nombre=Juan+Perez&telefono=573001234567&email=juan%40correo.com&asunto=Queja&mensaje=No+me+atendieron&tipo=pqrs&pagina=https%3A%2F%2Fipsvisalrt.com%2Fpqrs";
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, res.StatusCode);
        Assert.True(res.Ok);
        Assert.NotNull(res.CardId);

        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal(Tenant, card.TenantId);
        Assert.Equal(boardId, card.BoardId);
        Assert.Equal(firstCol, card.ColumnId);
        Assert.Equal("Juan Perez", card.Title);
        Assert.Contains("juan@correo.com", card.Description);
        Assert.Contains("573001234567", card.Description);
        Assert.Contains("No me atendieron", card.Description);
        Assert.Contains("ipsvisalrt.com/pqrs", card.Description);
    }

    [Fact]
    public async Task Process_Contacto_RoutesToContactosBoard()
    {
        var (svc, ctx, _) = Build(nameof(Process_Contacto_RoutesToContactosBoard));
        var pqr = await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var con = await SeedBoardAsync(ctx, FormWebhookService.ContactosBoardName);
        var token = await NewTokenAsync(svc);

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Ana&tipo=contacto");

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal(con.BoardId, card.BoardId);
        Assert.Equal(con.FirstColumnId, card.ColumnId);
        Assert.NotEqual(pqr.BoardId, card.BoardId);
    }

    [Fact]
    public async Task Process_MissingBoard_AutoCreatesPqrBoard_WithColumns()
    {
        var (svc, ctx, _) = Build(nameof(Process_MissingBoard_AutoCreatesPqrBoard_WithColumns));
        var token = await NewTokenAsync(svc); // sin sembrar tableros

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Luis&tipo=pqrs");

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        var board = await ctx.TaskBoards.IgnoreQueryFilters().FirstAsync(b => b.Id == card.BoardId);
        Assert.Equal(FormWebhookService.PqrBoardName, board.Name);

        var cols = await ctx.TaskBoardColumns.IgnoreQueryFilters()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).ToListAsync();
        Assert.Equal(4, cols.Count);
        Assert.Equal("Por hacer", cols[0].Name);
        Assert.Equal(cols[0].Id, card.ColumnId); // cae en la primera columna
    }

    [Fact]
    public async Task Process_SamePayloadTwice_WithinWindow_IsDeduped()
    {
        var (svc, ctx, _) = Build(nameof(Process_SamePayloadTwice_WithinWindow_IsDeduped));
        await SeedBoardAsync(ctx, FormWebhookService.ContactosBoardName);
        var token = await NewTokenAsync(svc);
        var body = "nombre=Ana&mensaje=hola&tipo=contacto";

        var first = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);
        var second = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, second.StatusCode);
        Assert.True(second.Duplicate);
        Assert.Equal(first.CardId, second.CardId);
        Assert.Equal(1, await ctx.TaskCards.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Process_SamePayload_AfterWindow_CreatesNewCard()
    {
        var (svc, ctx, time) = Build(nameof(Process_SamePayload_AfterWindow_CreatesNewCard));
        await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var token = await NewTokenAsync(svc);
        var body = "nombre=Ana&mensaje=hola";

        await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);
        time.Now = time.Now.AddMinutes(20); // fuera de la ventana de 10 min
        var second = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, second.StatusCode);
        Assert.Equal(2, await ctx.TaskCards.IgnoreQueryFilters().CountAsync());
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
        await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var token = await NewTokenAsync(svc);
        var body = "form_fields%5Bnombre%5D=Pedro&form_fields%5Bemail%5D=pedro%40x.com";

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal("Pedro", card.Title);
        Assert.Contains("pedro@x.com", card.Description);
    }

    [Fact]
    public async Task Process_JsonBody_RoutesContactoToContactosBoard()
    {
        var (svc, ctx, _) = Build(nameof(Process_JsonBody_RoutesContactoToContactosBoard));
        var con = await SeedBoardAsync(ctx, FormWebhookService.ContactosBoardName);
        var token = await NewTokenAsync(svc);
        var body = "{\"nombre\":\"Maria\",\"email\":\"m@x.com\",\"tipo\":\"contacto\"}";

        var res = await svc.ProcessAsync(token, "application/json", body);

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal("Maria", card.Title);
        Assert.Equal(con.BoardId, card.BoardId);
    }

    [Fact]
    public async Task Process_ConfigOverridesBoardName()
    {
        var (svc, ctx, _) = Build(nameof(Process_ConfigOverridesBoardName));
        // El tenant reconfigura el tablero de PQRS a "SOPORTE".
        ctx.TenantConfigurations.Add(new TenantConfiguration
        {
            TenantId = Tenant,
            ConfigKey = FormWebhookService.KeyTableroPqrs,
            ConfigValue = "SOPORTE"
        });
        await ctx.SaveChangesAsync();
        var soporte = await SeedBoardAsync(ctx, "SOPORTE");
        var token = await NewTokenAsync(svc);

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", "nombre=Beto&tipo=pqrs");

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal(soporte.BoardId, card.BoardId);
    }

    // ---- Elementor Pro "Advanced Data" = ON (formato oficial real) ----------
    // Los campos vienen anidados: fields[<id>][value] (+ [id][type][title][raw_value][required]) y los
    // metadatos como meta[<clave>][value]. Corchetes literales, application/x-www-form-urlencoded.

    private static string OnField(string id, string type, string title, string value)
        => $"fields[{id}][id]={id}"
         + $"&fields[{id}][type]={type}"
         + $"&fields[{id}][title]={Uri.EscapeDataString(title)}"
         + $"&fields[{id}][value]={Uri.EscapeDataString(value)}"
         + $"&fields[{id}][raw_value]={Uri.EscapeDataString(value)}"
         + $"&fields[{id}][required]=false";

    private static string AdvancedDataOnBody(string nombre, string email, string asunto, string mensaje, string tipo, string? pageUrl)
    {
        var parts = new List<string>
        {
            "form[id]=7ae7988",
            "form[name]=New+Form",
            OnField("nombre", "text", "", nombre),
            OnField("email", "email", "", email),
            OnField("asunto", "select", "Que deseas agendar", asunto),
            OnField("mensaje", "textarea", "", mensaje),
            OnField("tipo", "hidden", "", tipo),
        };
        if (pageUrl is not null)
        {
            parts.Add($"meta[page_url][title]=Page+URL&meta[page_url][value]={Uri.EscapeDataString(pageUrl)}");
        }
        return string.Join("&", parts);
    }

    [Fact]
    public async Task Process_ElementorAdvancedDataOn_CreatesCard_MapsAllFields()
    {
        var (svc, ctx, _) = Build(nameof(Process_ElementorAdvancedDataOn_CreatesCard_MapsAllFields));
        var (boardId, _) = await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var token = await NewTokenAsync(svc);
        var body = AdvancedDataOnBody("Juan Perez", "juan@correo.com", "Medicina general", "Texto del mensaje",
            "pqrs", "https://www.ipsvisalrt.com/contactanos/");

        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);

        Assert.Equal(201, res.StatusCode);
        var card = await GetCardAsync(ctx, res.CardId);
        Assert.Equal(boardId, card.BoardId);                     // tipo=pqrs -> tablero PQR
        Assert.Equal("Juan Perez", card.Title);                  // fields[nombre][value]
        Assert.Contains("juan@correo.com", card.Description);    // fields[email][value]
        Assert.Contains("Medicina general", card.Description);   // fields[asunto][value] (texto de la opcion)
        Assert.Contains("Texto del mensaje", card.Description);  // fields[mensaje][value]
        Assert.Contains("ipsvisalrt.com/contactanos", card.Description); // meta[page_url] -> pagina
    }

    [Fact]
    public async Task Process_ElementorAdvancedDataOn_TipoRoutesCard()
    {
        var (svc, ctx, _) = Build(nameof(Process_ElementorAdvancedDataOn_TipoRoutesCard));
        var pqr = await SeedBoardAsync(ctx, FormWebhookService.PqrBoardName);
        var con = await SeedBoardAsync(ctx, FormWebhookService.ContactosBoardName);
        var token = await NewTokenAsync(svc);

        var contacto = AdvancedDataOnBody("Ana", "a@x.com", "General", "Hola", "contacto", null);
        var c = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", contacto);
        var cardC = await GetCardAsync(ctx, c.CardId);
        Assert.Equal(con.BoardId, cardC.BoardId);

        var pqrs = AdvancedDataOnBody("Beto", "b@x.com", "General", "Queja", "pqrs", null);
        var p = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", pqrs);
        var cardP = await GetCardAsync(ctx, p.CardId);
        Assert.Equal(pqr.BoardId, cardP.BoardId);
    }

    [Fact]
    public async Task Process_ElementorAdvancedDataOn_MissingNombre_Returns400()
    {
        var (svc, _, _) = Build(nameof(Process_ElementorAdvancedDataOn_MissingNombre_Returns400));
        var token = await NewTokenAsync(svc);
        // fields[nombre][value] vacio -> obligatorio faltante.
        var body = AdvancedDataOnBody("", "x@y.com", "General", "hola", "pqrs", null);
        var res = await svc.ProcessAsync(token, "application/x-www-form-urlencoded", body);
        Assert.Equal(400, res.StatusCode);
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Admin;
using Visal.Application.Common;
using Visal.Application.Revision;
using Visal.Application.Revision.Ia;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Tests del orquestador REVISOR CLINICO IA (Capa 08 Ola 5). Cubren:
///   - Happy path: fake AI Provider Gateway devuelve JSON valido, se registra
///     evento PreRevisionAgente + tokens en AiUsageLog.
///   - Agente inexistente: falla humano-legible sin llamar al provider.
///   - Cupo agotado con limite duro: bloquea antes de llamar al provider.
///   - Respuesta LLM invalida (no JSON): reporta error de parseo, aun asi
///     los tokens quedan registrados por auditoria.
///   - Isolacion tenant: agente del tenant A no es visible desde tenant B.
/// </summary>
public sealed class PreRevisionIaServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aa000001-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bb000002-0000-0000-0000-000000000002");
    private static readonly Guid Actor = Guid.Parse("cc000003-0000-0000-0000-000000000003");

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public required VisalDbContext Db { get; init; }
        public required PreRevisionIaService Svc { get; init; }
        public required FakeAiClient AiClient { get; init; }
        public required Guid RevisionId { get; init; }
        public required Guid AgenteId { get; init; }
    }

    private static async Task<Fixture> BuildAsync(
        Guid tenantId,
        AiChatResult aiResponse,
        bool sembrarAgente = true,
        bool agenteActivo = true,
        bool providerHabilitado = true,
        string? dbName = null,
        bool sembrarCupoAgotadoDuro = false)
    {
        var name = dbName ?? Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var tenant = new FakeTenantContext { TenantId = tenantId, UserId = Actor };
        var db = new VisalDbContext(opts, tenant);
        var clock = new FakeTimeProvider(FixedNow);

        var paciente = new Paciente { TenantId = tenantId, NombreCompleto = "PACIENTE X", TipoDocumento = "CC", NumeroDocumento = "1000" };
        var form = new FormDefinition { TenantId = tenantId, Nombre = "HC-FO-01", Codigo = "HC-FO-01" };
        var hc = new HistoriaClinica
        {
            TenantId = tenantId,
            PacienteId = paciente.Id,
            FormDefinitionId = form.Id,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = FixedNow.AddDays(-1),
            FechaCierre = FixedNow,
        };
        db.Pacientes.Add(paciente);
        db.FormDefinitions.Add(form);
        db.HistoriasClinicas.Add(hc);

        if (providerHabilitado)
        {
            db.AiProviderConfigs.Add(new AiProviderConfig
            {
                Provider = AiProvider.Claude, IsEnabled = true, ApiKeyEncrypted = "cipher-key-1", Model = "claude-test-model",
            });
        }

        Guid agenteId = Guid.Empty;
        if (sembrarAgente)
        {
            var agente = new AiAgent
            {
                TenantId = tenantId,
                Name = IPreRevisionIaService.AgenteNombre,
                Provider = AiProvider.Claude,
                IsActive = agenteActivo,
                SystemPrompt = "Eres el revisor.",
            };
            db.AiAgents.Add(agente);
            agenteId = agente.Id;
        }
        await db.SaveChangesAsync();

        var revisionSvc = new RevisionClinicaService(db, tenant, clock);
        var rev = await revisionSvc.SolicitarAsync(new SolicitarRevisionCmd(hc.Id, Actor, null));

        if (sembrarCupoAgotadoDuro)
        {
            // Plan con limite de 100 tokens (Hard) + Subscription vigente + consumo por 200.
            var plan = new SaasPlan { Name = "Plan Test", IsActive = true };
            db.SaasPlans.Add(plan);
            db.SaasPlanLimits.Add(new SaasPlanLimit
            {
                PlanId = plan.Id,
                LimitKey = IAiUsageService.MonthlyTokenLimitKey,
                LimitValue = 100,
                EnforcementMode = LimitEnforcementMode.Hard,
            });
            db.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Active,
                StartsAt = FixedNow.AddMonths(-1),
            });
            db.AiUsageLogs.Add(new AiUsageLog
            {
                TenantId = tenantId,
                Provider = AiProvider.Claude,
                Model = "claude-test-model",
                AgentId = agenteId == Guid.Empty ? null : agenteId,
                InputTokens = 200,
                OutputTokens = 0,
                TotalTokens = 200,
                Source = "revision",
                Success = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var usage = new AiUsageService(db, tenant);
        var protector = new FakeSecretProtector();
        var tools = new RevisionMcpToolsService(db);
        var policy = new RevisionPolicyService(db, tenant);
        var aiClient = new FakeAiClient(aiResponse);
        var svc = new PreRevisionIaService(db, aiClient, usage, protector, tools, revisionSvc, policy);

        return new Fixture { Db = db, Svc = svc, AiClient = aiClient, RevisionId = rev.Id, AgenteId = agenteId };
    }

    private static AiChatResult OkResponse(string body, int inTokens = 120, int outTokens = 40)
        => new(true, body, null, inTokens, outTokens);

    // ---- Happy path ----

    [Fact]
    public async Task Ejecutar_ConRespuestaValida_RegistraVeredictoYTokens()
    {
        var body = "{\"resultado\":\"Aprobado\",\"confianza\":0.87,\"nota\":\"Todo OK\",\"hallazgos\":[\"H1\",\"H2\"]}";
        var fx = await BuildAsync(TenantA, OkResponse(body));

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.True(res.Ok, res.Error);
        Assert.Equal(RevisionResultado.Aprobado, res.Resultado);
        Assert.Equal(0.87m, res.Confianza);
        Assert.Equal(2, res.Hallazgos!.Count);
        Assert.Equal(120, res.InputTokens);
        Assert.Equal(40, res.OutputTokens);

        var eventos = await fx.Db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => e.RevisionClinicaId == fx.RevisionId
                     && e.Tipo == RevisionTipoEvento.PreRevisionAgente)
            .ToListAsync();
        Assert.Single(eventos);
        Assert.Equal(RevisionResultado.Aprobado, eventos[0].Resultado);
        Assert.Equal(IPreRevisionIaService.AgenteNombre, eventos[0].ActorAgenteCodigo);

        var uso = await fx.Db.AiUsageLogs.AsNoTracking().ToListAsync();
        Assert.Single(uso);
        Assert.Equal("revision", uso[0].Source);
        Assert.True(uso[0].Success);
        Assert.Equal(120, uso[0].InputTokens);
        Assert.Equal(40, uso[0].OutputTokens);

        Assert.Equal(1, fx.AiClient.CantidadLlamadas);
    }

    // ---- Fallas duras antes de llamar al provider ----

    [Fact]
    public async Task Ejecutar_SinAgenteConfigurado_DevuelveErrorYNoLlamaProvider()
    {
        var fx = await BuildAsync(TenantA, OkResponse("{}"), sembrarAgente: false);

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
        Assert.Contains(IPreRevisionIaService.AgenteNombre, res.Error);
        Assert.Equal(0, fx.AiClient.CantidadLlamadas);
    }

    [Fact]
    public async Task Ejecutar_ConAgenteApagado_DevuelveErrorYNoLlamaProvider()
    {
        var fx = await BuildAsync(TenantA, OkResponse("{}"), agenteActivo: false);

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.False(res.Ok);
        Assert.Contains("apagado", res.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fx.AiClient.CantidadLlamadas);
    }

    [Fact]
    public async Task Ejecutar_ConCupoAgotadoDuro_DevuelveErrorYNoLlamaProvider()
    {
        // Plan Hard con limite 100, consumo 200 → Exceeded && Hard → bloquea.
        var fx = await BuildAsync(TenantA, OkResponse("{}"), sembrarCupoAgotadoDuro: true);

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.False(res.Ok);
        Assert.Contains("limite", res.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fx.AiClient.CantidadLlamadas);
    }

    // ---- Parseo defensivo ----

    [Fact]
    public async Task Ejecutar_ConRespuestaSinJson_ReportaFalloYRegistraTokens()
    {
        var fx = await BuildAsync(TenantA, OkResponse("no soy json", inTokens: 50, outTokens: 10));

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.False(res.Ok);
        // Los tokens del intento SI se auditan aunque el parseo haya fallado.
        Assert.Equal(50, res.InputTokens);
        Assert.Equal(10, res.OutputTokens);

        // No se persiste evento PreRevisionAgente si no hay veredicto.
        var eventos = await fx.Db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => e.RevisionClinicaId == fx.RevisionId
                     && e.Tipo == RevisionTipoEvento.PreRevisionAgente)
            .ToListAsync();
        Assert.Empty(eventos);
    }

    // ---- Isolacion tenant ----

    [Fact]
    public async Task Ejecutar_ConAgenteEnOtroTenant_NoLoUsa()
    {
        var db = Guid.NewGuid().ToString();
        var body = "{\"resultado\":\"Aprobado\",\"confianza\":0.5,\"nota\":\"\",\"hallazgos\":[]}";
        // TenantA con agente sembrado.
        _ = await BuildAsync(TenantA, OkResponse(body), dbName: db);
        // TenantB pide ejecucion pero NO tiene agente en su tenant (el filtro global bloquea al de A).
        var fxB = await BuildAsync(TenantB, OkResponse(body), sembrarAgente: false, dbName: db);

        var res = await fxB.Svc.EjecutarAsync(fxB.RevisionId);

        Assert.False(res.Ok);
        Assert.Contains(IPreRevisionIaService.AgenteNombre, res.Error);
        Assert.Equal(0, fxB.AiClient.CantidadLlamadas);
    }

    // ---- Fakes ----

    private sealed class FakeAiClient : IAiProviderClient
    {
        private readonly AiChatResult _respuesta;
        public int CantidadLlamadas { get; private set; }
        public string? LastSystemPrompt { get; private set; }

        public FakeAiClient(AiChatResult respuesta) { _respuesta = respuesta; }

        public Task<AiChatResult> CompleteAsync(
            AiProvider provider, string apiKey, string? baseUrl, string model,
            string systemPrompt, IReadOnlyList<AiChatTurn> turns,
            CancellationToken cancellationToken = default)
        {
            CantidadLlamadas++;
            LastSystemPrompt = systemPrompt;
            return Task.FromResult(_respuesta);
        }
    }

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow()
        {
            var v = _now;
            _now = _now.AddSeconds(1);
            return v;
        }
    }
}

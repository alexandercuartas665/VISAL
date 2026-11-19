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
        public Guid HcId { get; init; }
    }

    // Ola 4c: AiUsageService ahora abre contexto corto via IApplicationDbContextFactory.
    // En tests la factory crea un VisalDbContext nuevo sobre el MISMO store InMemory
    // (misma key), asi el using/dispose del servicio no borra los datos sembrados.
    private sealed class SingleStoreDbContextFactory : IApplicationDbContextFactory
    {
        private readonly DbContextOptions<VisalDbContext> _opts;
        private readonly ITenantContext _tenant;
        public SingleStoreDbContextFactory(DbContextOptions<VisalDbContext> opts, ITenantContext tenant)
        {
            _opts = opts;
            _tenant = tenant;
        }
        public IApplicationDbContext Create() => new VisalDbContext(_opts, _tenant);
    }

    private static async Task<Fixture> BuildAsync(
        Guid tenantId,
        AiChatResult aiResponse,
        bool sembrarAgente = true,
        bool agenteActivo = true,
        bool providerHabilitado = true,
        string? dbName = null,
        bool sembrarCupoAgotadoDuro = false,
        bool crearRevision = true,
        HistoriaClinicaEstado estadoHc = HistoriaClinicaEstado.Cerrada,
        RevisionPolicy? politica = null)
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
            Estado = estadoHc,
            FechaApertura = FixedNow.AddDays(-1),
            FechaCierre = estadoHc == HistoriaClinicaEstado.Cerrada ? FixedNow : null,
        };
        if (politica is not null)
        {
            politica.TenantId = tenantId;
            db.RevisionPolicies.Add(politica);
        }
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

        var revisionSvc = new RevisionClinicaService(new SingleStoreDbContextFactory(opts, tenant), tenant, clock);
        var revId = crearRevision
            ? (await revisionSvc.SolicitarAsync(new SolicitarRevisionCmd(hc.Id, Actor, null))).Id
            : Guid.Empty;

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

        var usage = new AiUsageService(new SingleStoreDbContextFactory(opts, tenant), tenant);
        var protector = new FakeSecretProtector();
        var tools = new RevisionMcpToolsService(new SingleStoreDbContextFactory(opts, tenant));
        var policy = new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var aiClient = new FakeAiClient(aiResponse);
        var svc = new PreRevisionIaService(new SingleStoreDbContextFactory(opts, tenant), aiClient, usage, protector, tools, revisionSvc, policy);

        return new Fixture { Db = db, Svc = svc, AiClient = aiClient, RevisionId = revId, AgenteId = agenteId, HcId = hc.Id };
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

    // ---- Auditoria IA al cerrar (Atencion -> "Guardar definitivamente") ----

    private const string BodyRechazoConSugerencia =
        "{\"resultado\":\"Rechazado\",\"confianza\":0.74,\"nota\":\"Copia de otro turno\"," +
        "\"hallazgos\":[\"ENTREGA identica a HC-028780\"]," +
        "\"sugerencias\":[{\"campo\":\"DURANTE NOCHE\",\"texto_sugerido\":\"[COMPLETAR: eventos de este turno]\"}]}";

    [Fact]
    public async Task AuditarHc_con_hc_abierta_no_crea_ciclo_ni_log_solo_consumo()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);

        var a = await fx.Svc.AuditarHcAsync(fx.HcId);

        Assert.True(a.Ok, a.Error);
        Assert.Equal(RevisionResultado.Rechazado, a.Resultado);
        Assert.Single(a.Sugerencias);
        Assert.Equal("DURANTE NOCHE", a.Sugerencias[0].Campo);
        Assert.NotNull(a.Traza);
        Assert.Equal(0, await fx.Db.RevisionesClinica.CountAsync());
        Assert.Equal(0, await fx.Db.RevisionAgenteLogs.CountAsync());
        Assert.Equal(1, await fx.Db.AiUsageLogs.CountAsync());
    }

    [Fact]
    public async Task RegistrarAuditoriaCierre_registra_veredicto_log_y_sugerencias_sin_segunda_llamada()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        var a = await fx.Svc.AuditarHcAsync(fx.HcId);

        var res = await fx.Svc.RegistrarAuditoriaCierreAsync(fx.HcId, a, cerradaPeseARechazo: true, Actor);

        Assert.True(res.Ok, res.Error);
        Assert.Equal(1, fx.AiClient.CantidadLlamadas); // no reprocesa
        var rev = await fx.Db.RevisionesClinica.AsNoTracking().SingleAsync(r => r.HistoriaClinicaId == fx.HcId);
        var ev = await fx.Db.RevisionClinicaEventos.AsNoTracking()
            .Where(e => e.RevisionClinicaId == rev.Id && e.Tipo == RevisionTipoEvento.PreRevisionAgente).ToListAsync();
        Assert.Single(ev);
        Assert.Equal(RevisionResultado.Rechazado, ev[0].Resultado);
        var log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync();
        Assert.Equal(rev.Id, log.RevisionClinicaId);
        Assert.Contains("DURANTE NOCHE", log.SugerenciasJson);
        Assert.Equal(1, await fx.Db.AiUsageLogs.CountAsync());
    }

    [Fact]
    public async Task GuardarLogIntento_y_feedback_quedan_en_el_log_sin_ciclo()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        var a = await fx.Svc.AuditarHcAsync(fx.HcId);

        var logId = await fx.Svc.GuardarLogIntentoCierreAsync(a);
        Assert.NotNull(logId);
        Assert.Equal(0, await fx.Db.RevisionesClinica.CountAsync());

        Assert.True(await fx.Svc.RegistrarFeedbackAsync(logId!.Value, false, "  El hallazgo 1 es falso: es texto de la plantilla  ", Actor));

        var log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync();
        Assert.Null(log.RevisionClinicaId);            // intento: sin ciclo de revision
        Assert.Equal(fx.HcId, log.HistoriaClinicaId);
        Assert.Equal("Rechazado", log.Resultado);
        Assert.False(log.FeedbackUtil);
        Assert.Equal("El hallazgo 1 es falso: es texto de la plantilla", log.FeedbackComentario);
        Assert.NotNull(log.FeedbackEn);

        // Cambia de opinion: pulgar arriba sin comentario.
        Assert.True(await fx.Svc.RegistrarFeedbackAsync(logId.Value, true, null, Actor));
        log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync();
        Assert.True(log.FeedbackUtil);
        Assert.Null(log.FeedbackComentario);
    }

    [Fact]
    public async Task RegistrarAuditoriaCierre_vincula_el_log_del_intento_sin_duplicarlo_y_conserva_feedback()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        var a = await fx.Svc.AuditarHcAsync(fx.HcId);
        var logId = await fx.Svc.GuardarLogIntentoCierreAsync(a);
        await fx.Svc.RegistrarFeedbackAsync(logId!.Value, false, "confundio medicacion previa", Actor);

        var res = await fx.Svc.RegistrarAuditoriaCierreAsync(fx.HcId, a with { LogId = logId }, cerradaPeseARechazo: true, Actor);

        Assert.True(res.Ok, res.Error);
        Assert.Equal(1, fx.AiClient.CantidadLlamadas);
        var rev = await fx.Db.RevisionesClinica.AsNoTracking().SingleAsync(r => r.HistoriaClinicaId == fx.HcId);
        var log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync();   // uno solo
        Assert.Equal(logId, log.Id);
        Assert.Equal(rev.Id, log.RevisionClinicaId);
        Assert.Equal("confundio medicacion previa", log.FeedbackComentario);
    }

    [Fact]
    public async Task GuardarLogIntento_no_guarda_si_la_auditoria_fallo()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false);
        var fallo = AuditoriaIaResultado.Fallo(fx.HcId, "timeout");
        Assert.Null(await fx.Svc.GuardarLogIntentoCierreAsync(fallo));
        Assert.False(await fx.Svc.RegistrarFeedbackAsync(Guid.NewGuid(), true, null, Actor));
        Assert.Equal(0, await fx.Db.RevisionAgenteLogs.CountAsync());
    }

    [Fact]
    public void Prompt_incluye_reglas_de_plantilla_medicacion_previa_y_calificadores()
    {
        var m = typeof(PreRevisionIaService).GetMethod("ArmarSystemPrompt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var prompt = (string)m.Invoke(null, new object[] { "" })!;
        Assert.Contains("TEXTO DE PLANTILLA", prompt);
        Assert.Contains("MEDICACION PREVIA vs NUEVA FORMULACION", prompt);
        Assert.Contains("CALIFICADORES DE TIEMPO", prompt);
        Assert.Contains("TABLAS DEL FORMATO", prompt);
    }

    [Fact]
    public async Task RegistrarAuditoriaCierre_rechaza_resultado_de_otra_hc()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false);
        var a = await fx.Svc.AuditarHcAsync(fx.HcId);

        var res = await fx.Svc.RegistrarAuditoriaCierreAsync(Guid.NewGuid(), a, false, Actor);

        Assert.False(res.Ok);
        Assert.Equal(0, await fx.Db.RevisionesClinica.CountAsync());
    }

    [Theory]
    [InlineData(0.90, true)]
    [InlineData(0.70, false)]
    public async Task RegistrarAuditoriaCierre_aprueba_automaticamente_si_confianza_supera_umbral(double conf, bool esperaAprobada)
    {
        var body = "{\"resultado\":\"Aprobado\",\"confianza\":" + conf.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + ",\"nota\":\"OK\",\"hallazgos\":[]}";
        var fx = await BuildAsync(TenantA, OkResponse(body), crearRevision: false,
            politica: new RevisionPolicy { AdopcionAutomaticaAgente = true, UmbralConfianza = 0.80m });
        var a = await fx.Svc.AuditarHcAsync(fx.HcId);

        await fx.Svc.RegistrarAuditoriaCierreAsync(fx.HcId, a, false, Actor);

        var rev = await fx.Db.RevisionesClinica.AsNoTracking().SingleAsync(r => r.HistoriaClinicaId == fx.HcId);
        Assert.Equal(esperaAprobada, rev.EstadoAgregado == RevisionEstadoAgregado.Aprobada);
    }

    [Fact]
    public async Task ConfigAuditoriaAlCerrar_respeta_interruptor_y_lista_de_formatos()
    {
        var off = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false);
        Assert.Null(await off.Svc.ConfigAuditoriaAlCerrarAsync(off.HcId));

        var todos = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false,
            politica: new RevisionPolicy { AuditoriaIaAlCerrar = true, AuditoriaIaTimeoutSeg = 60 });
        Assert.Equal(60, (await todos.Svc.ConfigAuditoriaAlCerrarAsync(todos.HcId))?.TimeoutSeg);

        var otro = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false,
            politica: new RevisionPolicy { AuditoriaIaAlCerrar = true, AuditoriaIaFormatosJson = "[\"PP-FO-84\"]" });
        Assert.Null(await otro.Svc.ConfigAuditoriaAlCerrarAsync(otro.HcId));

        var incluido = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false,
            politica: new RevisionPolicy { AuditoriaIaAlCerrar = true, AuditoriaIaFormatosJson = "[\"hc-fo-01\"]" });
        Assert.NotNull(await incluido.Svc.ConfigAuditoriaAlCerrarAsync(incluido.HcId));
    }

    [Fact]
    public void ParsearSugerencias_tolera_ausencia_y_nombres_alternos()
    {
        Assert.Empty(PreRevisionIaService.ParsearSugerencias("{\"resultado\":\"Aprobado\",\"confianza\":0.9}"));
        var s = PreRevisionIaService.ParsearSugerencias(
            "{\"resultado\":\"Rechazado\",\"sugerencias\":[{\"field\":\"PLAN\",\"sugerencia\":\"texto\"},{\"campo\":\"\"}]}");
        var sg = Assert.Single(s);
        Assert.Equal("PLAN", sg.Campo);
        Assert.Equal("texto", sg.TextoSugerido);
    }

    [Fact]
    public async Task EjecutarAsync_tras_refactor_guarda_sugerencias_en_el_log()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia));

        var res = await fx.Svc.EjecutarAsync(fx.RevisionId);

        Assert.True(res.Ok, res.Error);
        Assert.Single(res.Sugerencias!);
        var log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync();
        Assert.Contains("[COMPLETAR", log.SugerenciasJson);
        Assert.Contains("TEXTO SUGERIDO", fx.AiClient.LastSystemPrompt);
    }

    [Fact]
    public async Task Politica_valida_tiempo_de_auditoria_al_cerrar()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new FakeTenantContext { TenantId = TenantA, UserId = Actor };
        var svc = new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

        var def = await svc.GetAsync();
        Assert.False(def.AuditoriaIaAlCerrar);
        Assert.Equal(180, def.AuditoriaIaTimeoutSeg);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(def with { AuditoriaIaTimeoutSeg = 10 }, Actor));

        await svc.SaveAsync(def with { AuditoriaIaAlCerrar = true, AuditoriaIaFormatos = new[] { "PP-FO-84", "pp-fo-84", " " } }, Actor);
        var leida = await new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())).GetAsync();
        Assert.True(leida.AuditoriaIaAlCerrar);
        Assert.Equal(new[] { "PP-FO-84" }, leida.AuditoriaIaFormatos);
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

        public Task<AiModelsResult> ListModelsAsync(
            AiProvider provider, string apiKey, string? baseUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AiModelsResult(true, Array.Empty<string>(), null));
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

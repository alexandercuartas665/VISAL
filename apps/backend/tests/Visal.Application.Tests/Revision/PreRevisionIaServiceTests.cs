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
        public DbContextOptions<VisalDbContext> Opts { get; init; } = null!;
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

        return new Fixture { Db = db, Svc = svc, AiClient = aiClient, RevisionId = revId, AgenteId = agenteId, HcId = hc.Id, Opts = opts };
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

    /// <summary>IMP-50: filtro por tipo de servicio de la asignacion que origino la HC.</summary>
    [Fact]
    public async Task ConfigAuditoriaAlCerrar_filtra_por_tipo_de_servicio_de_la_asignacion()
    {
        static RevisionPolicy Pol(string tiposJson, string? formatosJson = null) => new()
            { AuditoriaIaAlCerrar = true, AuditoriaIaTiposServicioJson = tiposJson, AuditoriaIaFormatosJson = formatosJson };

        // HC de ENFERMERIA con filtro ENFERMERIA -> se audita.
        var enf = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, politica: Pol("[\"ENFERMERIA\"]"));
        await VincularAsignacionAsync(enf, "ENFERMERIA");
        Assert.NotNull(await enf.Svc.ConfigAuditoriaAlCerrarAsync(enf.HcId));

        // HC de TERAPIAS (legacy plural) con filtro ENFERMERIA -> no; con filtro TERAPIA -> si.
        var ter = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, politica: Pol("[\"ENFERMERIA\"]"));
        await VincularAsignacionAsync(ter, "TERAPIAS");
        Assert.Null(await ter.Svc.ConfigAuditoriaAlCerrarAsync(ter.HcId));
        var ter2 = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, politica: Pol("[\"TERAPIA\"]"));
        await VincularAsignacionAsync(ter2, "TERAPIAS");
        Assert.NotNull(await ter2.Svc.ConfigAuditoriaAlCerrarAsync(ter2.HcId));

        // HC sin asignacion con filtro de tipo -> no se audita.
        var libre = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, politica: Pol("[\"ENFERMERIA\"]"));
        Assert.Null(await libre.Svc.ConfigAuditoriaAlCerrarAsync(libre.HcId));

        // Tipo OK pero formato fuera de la lista -> no (se combinan con Y).
        var ambos = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false,
            politica: Pol("[\"ENFERMERIA\"]", "[\"PP-FO-84\"]"));
        await VincularAsignacionAsync(ambos, "ENFERMERIA");
        Assert.Null(await ambos.Svc.ConfigAuditoriaAlCerrarAsync(ambos.HcId));
    }

    private static async Task VincularAsignacionAsync(Fixture fx, string tipoServicio)
    {
        var hc = await fx.Db.HistoriasClinicas.SingleAsync(h => h.Id == fx.HcId);
        var asig = new Asignacion
        {
            TenantId = hc.TenantId, LoteId = Guid.NewGuid(), PacienteId = hc.PacienteId,
            Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "X", TipoServicio = tipoServicio,
            Cantidad = 1, ContratoCodigo = "C1", MesVigencia = 8, FechaInicio = new DateOnly(2026, 8, 1),
            Estado = AsignacionEstado.Asignado
        };
        var turno = new AsignacionTurno { TenantId = hc.TenantId, AsignacionId = asig.Id, ProfesionalId = Guid.NewGuid(), Cantidad = 1 };
        var ses = new AsignacionTurnoSesion { TenantId = hc.TenantId, AsignacionTurnoId = turno.Id, SessionNo = 1, FechaAtencion = new DateOnly(2026, 8, 1) };
        fx.Db.Asignaciones.Add(asig);
        fx.Db.AsignacionTurnos.Add(turno);
        fx.Db.AsignacionTurnoSesiones.Add(ses);
        fx.Db.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc { TenantId = hc.TenantId, SesionId = ses.Id, HistoriaClinicaId = hc.Id });
        await fx.Db.SaveChangesAsync();
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

        await svc.SaveAsync(def with { AuditoriaIaAlCerrar = true, AuditoriaIaFormatos = new[] { "PP-FO-84", "pp-fo-84", " " }, AuditoriaIaTiposServicio = new[] { " enfermeria", "ENFERMERIA", "" } }, Actor);
        var leida = await new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())).GetAsync();
        Assert.True(leida.AuditoriaIaAlCerrar);
        Assert.Equal(new[] { "PP-FO-84" }, leida.AuditoriaIaFormatos);
        Assert.Equal(new[] { "ENFERMERIA" }, leida.AuditoriaIaTiposServicio);
    }

    // ---- IMP-68: auditoria al cerrar en modo BLOQUEANTE por tipo de servicio ----

    private static RevisionPolicy PolBloqueo(string bloqueoJson, string? tiposJson = null) => new()
    {
        AuditoriaIaAlCerrar = true,
        AuditoriaIaTiposServicioJson = tiposJson,
        AuditoriaIaBloqueoJson = bloqueoJson,
    };

    /// <summary>Hace que <see cref="Actor"/> sea un profesional del tenant (rol operativo), con o sin
    /// el permiso de exencion "historias.sin-control-cierre".</summary>
    private static async Task SembrarActorAsync(Fixture fx, Guid tenantId, bool exento = false)
    {
        var rol = new Rol { TenantId = tenantId, Nombre = "Profesional" };
        fx.Db.Roles.Add(rol);
        if (exento)
        {
            fx.Db.RolPermisos.Add(new RolPermiso
            {
                TenantId = tenantId, RolId = rol.Id, Modulo = PermisosUsuario.SinControlCierre,
                Ver = true, Crear = false, Editar = false, Eliminar = false,
            });
        }
        fx.Db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId, PlatformUserId = Actor, Email = "prof@test",
            TenantRole = TenantRole.Advisor, RolId = rol.Id,
        });
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SembrarLogAsync(Fixture fx, Guid tenantId, string resultado, DateTimeOffset en)
    {
        fx.Db.RevisionAgenteLogs.Add(new RevisionAgenteLog
        {
            TenantId = tenantId, HistoriaClinicaId = fx.HcId, AgenteCodigo = "REVISOR",
            Ok = true, Resultado = resultado, Confianza = 0.8m, Nota = "Falta el plan.", CreatedAt = en,
        });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task EstadoBloqueo_solo_aplica_a_tipos_bloqueantes_y_no_a_exentos()
    {
        // Tipo informativo (ENFERMERIA no esta en el bloqueo) -> null.
        var enf = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: PolBloqueo("{\"TERAPIA\":2}"));
        await VincularAsignacionAsync(enf, "ENFERMERIA");
        await SembrarActorAsync(enf, TenantA);
        Assert.Null(await enf.Svc.EstadoBloqueoIaAsync(enf.HcId, Actor));

        // TERAPIAS (plural legacy) con TERAPIA bloqueante -> bloquea con su maximo.
        var ter = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: PolBloqueo("{\"TERAPIA\":2}"));
        await VincularAsignacionAsync(ter, "TERAPIAS");
        await SembrarActorAsync(ter, TenantA);
        var est = await ter.Svc.EstadoBloqueoIaAsync(ter.HcId, Actor);
        Assert.NotNull(est);
        Assert.Equal(2, est!.MaxIntentos);
        Assert.Equal(0, est.Rechazos);
        Assert.False(est.Agotado);

        // Coordinador con permiso de exencion -> informativo.
        var coord = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: PolBloqueo("{\"TERAPIA\":2}"));
        await VincularAsignacionAsync(coord, "TERAPIA");
        await SembrarActorAsync(coord, TenantA, exento: true);
        Assert.Null(await coord.Svc.EstadoBloqueoIaAsync(coord.HcId, Actor));

        // Auditoria al cerrar apagada -> nunca bloquea.
        var off = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: new RevisionPolicy { AuditoriaIaAlCerrar = false, AuditoriaIaBloqueoJson = "{\"TERAPIA\":2}" });
        await VincularAsignacionAsync(off, "TERAPIA");
        await SembrarActorAsync(off, TenantA);
        Assert.Null(await off.Svc.EstadoBloqueoIaAsync(off.HcId, Actor));
    }

    [Fact]
    public async Task EstadoBloqueo_cuenta_rechazos_y_la_solicitud_reinicia_la_ronda_al_rechazarse()
    {
        var fx = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: PolBloqueo("{\"ENFERMERIA\":2}"));
        await VincularAsignacionAsync(fx, "ENFERMERIA");
        await SembrarActorAsync(fx, TenantA);
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);

        // 1 rechazo + 1 aprobado: aun quedan intentos -> no puede pedir autorizacion.
        await SembrarLogAsync(fx, TenantA, "Rechazado", t0);
        await SembrarLogAsync(fx, TenantA, "Aprobado", t0.AddMinutes(1));
        var est = await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor);
        Assert.Equal(1, est!.Rechazos);
        var (okAntes, _) = await fx.Svc.SolicitarAutorizacionCierreIaAsync(fx.HcId, "porque si", null, "PROF", Actor);
        Assert.False(okAntes);

        // 2o rechazo: agotado -> pide autorizacion (una sola, aunque insista).
        await SembrarLogAsync(fx, TenantA, "Rechazado", t0.AddMinutes(2));
        est = await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor);
        Assert.True(est!.Agotado);
        var (ok, msg) = await fx.Svc.SolicitarAutorizacionCierreIaAsync(fx.HcId, "El hallazgo no aplica", "Otro", "PROF", Actor);
        Assert.True(ok, msg);
        var (ok2, msg2) = await fx.Svc.SolicitarAutorizacionCierreIaAsync(fx.HcId, "otra vez", null, "PROF", Actor);
        Assert.True(ok2);
        Assert.Contains("pendiente", msg2);
        var sol = await fx.Db.AutorizacionSolicitudes.AsNoTracking().SingleAsync();
        Assert.Equal(AutorizacionSolicitudTipo.CierreAuditoriaIa, sol.Tipo);
        Assert.Contains("2 vez", sol.Detalle);
        Assert.True((await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor))!.Pendiente);

        // El coordinador la rechaza -> ronda nueva: 0 rechazos y ve la respuesta.
        var tracked = await fx.Db.AutorizacionSolicitudes.SingleAsync();
        tracked.Estado = AutorizacionSolicitudEstado.Rechazada;
        tracked.Respuesta = "Corrige el plan";
        tracked.ResueltoEn = DateTimeOffset.UtcNow;
        await fx.Db.SaveChangesAsync();
        est = await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor);
        Assert.Equal(0, est!.Rechazos);
        Assert.Equal("Rechazada", est.SolicitudEstado);
        Assert.Equal("Corrige el plan", est.SolicitudRespuesta);
        Assert.False(est.Agotado);

        // Nuevo rechazo despues de la respuesta cuenta en la ronda nueva.
        await SembrarLogAsync(fx, TenantA, "Rechazado", DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(1, (await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor))!.Rechazos);
    }

    [Fact]
    public async Task SolicitudCierreIa_aprobada_autoriza_sin_tocar_la_fecha_ni_crear_token()
    {
        var fx = await BuildAsync(TenantA, OkResponse("{}"), crearRevision: false, estadoHc: HistoriaClinicaEstado.Abierta,
            politica: PolBloqueo("{\"ENFERMERIA\":1}"));
        await VincularAsignacionAsync(fx, "ENFERMERIA");
        await SembrarActorAsync(fx, TenantA);
        await SembrarLogAsync(fx, TenantA, "Rechazado", DateTimeOffset.UtcNow.AddMinutes(-5));
        var (ok, msg) = await fx.Svc.SolicitarAutorizacionCierreIaAsync(fx.HcId, "Necesito cerrar", null, "PROF", Actor);
        Assert.True(ok, msg);
        var solId = (await fx.Db.AutorizacionSolicitudes.AsNoTracking().SingleAsync()).Id;

        // El aprobador es Owner (permiso implicito de autorizaciones).
        var aprobador = Guid.NewGuid();
        fx.Db.TenantUsers.Add(new TenantUser { TenantId = TenantA, PlatformUserId = aprobador, Email = "coord@test", TenantRole = TenantRole.Owner });
        await fx.Db.SaveChangesAsync();
        var tenant = new FakeTenantContext { TenantId = TenantA, UserId = aprobador };
        var autSvc = new AutorizacionTokenService(new SingleStoreDbContextFactory(fx.Opts, tenant), tenant,
            new NoopAuditImp68(), new FakeTimeProvider(FixedNow));
        var r = await autSvc.AprobarSolicitudAsync(solId, null, "COORD", aprobador);
        Assert.True(r.Ok, r.Mensaje);

        Assert.True((await fx.Svc.EstadoBloqueoIaAsync(fx.HcId, Actor))!.Autorizada);
        Assert.Empty(await fx.Db.AutorizacionTokens.AsNoTracking().ToListAsync());
        var hc = await fx.Db.HistoriasClinicas.AsNoTracking().SingleAsync(h => h.Id == fx.HcId);
        Assert.Null(hc.AutorizacionFechaTokenId);
    }

    private sealed class NoopAuditImp68 : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) { }
        public void Write(IApplicationDbContext db, Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) { }
    }

    [Fact]
    public async Task Politica_guarda_bloqueo_normalizado_y_valida_intentos()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new FakeTenantContext { TenantId = TenantA, UserId = Actor };
        var svc = new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var def = await svc.GetAsync();
        Assert.True(def.AuditoriaIaBloqueo is null or { Count: 0 });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(def with { AuditoriaIaBloqueo = new Dictionary<string, int> { ["TERAPIA"] = 0 } }, Actor));

        await svc.SaveAsync(def with
        {
            AuditoriaIaAlCerrar = true,
            AuditoriaIaBloqueo = new Dictionary<string, int> { [" terapia "] = 3, ["ENFERMERIA"] = 1 },
        }, Actor);
        var leida = await new RevisionPolicyService(new SingleStoreDbContextFactory(opts, tenant), tenant,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())).GetAsync();
        Assert.Equal(3, leida.AuditoriaIaBloqueo!["TERAPIA"]);
        Assert.Equal(1, leida.AuditoriaIaBloqueo["enfermeria"]);
        Assert.Equal(2, leida.AuditoriaIaBloqueo.Count);
    }

    // ---- IMP-73: reuso sin cambios, memoria entre intentos, hallazgos vs observaciones ----

    private static async Task SetValoresAsync(Fixture fx, string valoresJson)
    {
        using var db = new VisalDbContext(fx.Opts, new FakeTenantContext { TenantId = TenantA, UserId = Actor });
        var hc = await db.HistoriasClinicas.SingleAsync(h => h.Id == fx.HcId);
        hc.ValoresJson = valoresJson;
        await db.SaveChangesAsync();
    }

    private const string BodyConObservaciones =
        "{\"resultado\":\"Rechazado\",\"confianza\":0.8,\"nota\":\"Signo vital imposible\"," +
        "\"hallazgos\":[\"Temperatura 85 C imposible\",\"Verificado sin hallazgo: los 11 campos estan llenos\"," +
        "\"Completitud: los 8 campos requeridos estan diligenciados\",\"Observacion administrativa: el registro se abrio dos dias despues\"]," +
        "\"observaciones\":[\"Campo Registro igual al documento\"],\"sugerencias\":[]}";

    [Fact]
    public void Parse_separa_observaciones_y_descarta_validaciones_positivas()
    {
        var (h, o) = PreRevisionIaService.ParsearHallazgos(BodyConObservaciones);
        Assert.Equal(new[] { "Temperatura 85 C imposible" }, h);
        Assert.Equal(2, o.Count);
        Assert.Contains("Campo Registro igual al documento", o);
        Assert.Contains(o, x => x.StartsWith("el registro se abrio dos dias despues", StringComparison.Ordinal));

        // Una validacion con "pero"/"falta" SI es hallazgo.
        var (h2, _) = PreRevisionIaService.ParsearHallazgos(
            "{\"resultado\":\"Rechazado\",\"hallazgos\":[\"Completitud: los campos estan llenos pero falta la firma\"]}");
        Assert.Single(h2);
    }

    [Fact]
    public async Task Observaciones_viajan_en_el_log_con_prefijo_y_se_separan_al_leer()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyConObservaciones), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        var a = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        Assert.Single(a.Hallazgos);
        Assert.Equal(2, a.Observaciones.Count);

        var logId = await fx.Svc.GuardarLogIntentoCierreAsync(a);
        var log = await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync(l => l.Id == logId);
        var (hs, os) = HallazgosIa.Separar(log.HallazgosJson);
        Assert.Equal(a.Hallazgos, hs);
        Assert.Equal(a.Observaciones, os);
        Assert.Contains(HallazgosIa.PrefijoObservacion, log.HallazgosJson);
        Assert.Single(HallazgosIa.Separar(log.HallazgosJson).Hallazgos); // lo que cuenta Facturacion
    }

    [Fact]
    public async Task Cierre_sin_cambios_reutiliza_el_resultado_sin_llamar_al_proveedor()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        await SetValoresAsync(fx, "{\"evolucion\":\"texto copiado del turno anterior\"}");

        var a1 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        var log1 = await fx.Svc.GuardarLogIntentoCierreAsync(a1);
        Assert.NotNull(log1);
        Assert.Contains("\"momento\"", fx.AiClient.LastUserMessage);
        Assert.DoesNotContain("revision_anterior", fx.AiClient.LastUserMessage);

        // Vuelve a cerrar sin tocar nada: mismo concepto, sin proveedor ni log nuevo.
        var a2 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        Assert.Equal(1, fx.AiClient.CantidadLlamadas);
        Assert.True(a2.Ok);
        Assert.NotNull(a2.ReutilizadaDe);
        Assert.Equal(log1, a2.LogId);
        Assert.Equal(RevisionResultado.Rechazado, a2.Resultado);
        Assert.Equal(a1.Hallazgos, a2.Hallazgos);
        Assert.Single(a2.Sugerencias);
        Assert.Equal(1, await fx.Db.RevisionAgenteLogs.CountAsync());
        Assert.Equal(1, await fx.Db.AiUsageLogs.CountAsync());

        // Registrar el cierre con el resultado reutilizado vincula el MISMO log al ciclo.
        var res = await fx.Svc.RegistrarAuditoriaCierreAsync(fx.HcId, a2, cerradaPeseARechazo: true, Actor);
        Assert.True(res.Ok, res.Error);
        Assert.Equal(1, await fx.Db.RevisionAgenteLogs.CountAsync());
        Assert.NotNull((await fx.Db.RevisionAgenteLogs.AsNoTracking().SingleAsync()).RevisionClinicaId);
    }

    [Fact]
    public async Task Cierre_con_cambios_vuelve_a_auditar_con_la_revision_anterior()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        await SetValoresAsync(fx, "{\"evolucion\":\"texto copiado del turno anterior\",\"plan\":\"igual\"}");
        var a1 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        await fx.Svc.GuardarLogIntentoCierreAsync(a1);

        await SetValoresAsync(fx, "{\"evolucion\":\"eventos propios de este turno\",\"plan\":\"igual\"}");
        var a2 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);

        Assert.Equal(2, fx.AiClient.CantidadLlamadas);
        Assert.Null(a2.ReutilizadaDe);
        var msg = fx.AiClient.LastUserMessage!;
        Assert.Contains("revision_anterior", msg);
        Assert.Contains("ENTREGA identica a HC-028780", msg);       // su hallazgo previo
        Assert.Contains("\"campos_cambiados_desde_entonces\":[\"evolucion\"]", msg);
        Assert.Contains("\"documento_sin_cambios\":false", msg);
        Assert.Contains("REVISION ANTERIOR", fx.AiClient.LastSystemPrompt);
    }

    [Fact]
    public async Task Sin_opcion_de_reuso_siempre_llama_pero_lleva_memoria()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        await SetValoresAsync(fx, "{\"evolucion\":\"x\"}");
        var a1 = await fx.Svc.AuditarHcAsync(fx.HcId);
        await fx.Svc.GuardarLogIntentoCierreAsync(a1);

        var a2 = await fx.Svc.AuditarHcAsync(fx.HcId);           // Ordenes: re-ejecucion explicita
        Assert.Equal(2, fx.AiClient.CantidadLlamadas);
        Assert.Null(a2.ReutilizadaDe);
        Assert.Contains("\"documento_sin_cambios\":true", fx.AiClient.LastUserMessage);
        Assert.DoesNotContain("\"momento\"", fx.AiClient.LastUserMessage);
    }

    [Fact]
    public async Task Reuso_no_aplica_si_cambio_el_prompt_o_paso_la_ventana()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        await SetValoresAsync(fx, "{\"evolucion\":\"x\"}");
        var a1 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        var logId = await fx.Svc.GuardarLogIntentoCierreAsync(a1);

        // Cambian las instrucciones del agente: se vuelve a auditar.
        using (var db = new VisalDbContext(fx.Opts, new FakeTenantContext { TenantId = TenantA, UserId = Actor }))
        {
            var ag = await db.AiAgents.SingleAsync();
            ag.SystemPrompt = "Eres el revisor (v2).";
            await db.SaveChangesAsync();
        }
        await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        Assert.Equal(2, fx.AiClient.CantidadLlamadas);

        // Corrida de hace mas de 24 h (con el prompt vigente) tampoco se reutiliza.
        var prompt = fx.AiClient.LastSystemPrompt!;
        var previo = new PreRevisionIaService.CorridaPrevia(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-25), "Rechazado",
            0.7m, null, null, null, prompt, "{\"documento_a_revisar\":{\"a\":1}}", null);
        Assert.False(PreRevisionIaService.EsReutilizable(previo, prompt, "{\"documento_a_revisar\":{\"a\":1}}", DateTimeOffset.UtcNow));
        Assert.True(PreRevisionIaService.EsReutilizable(previo with { CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) },
            prompt, "{\"documento_a_revisar\":{\"a\":1},\"revision_anterior\":{}}", DateTimeOffset.UtcNow));
        Assert.False(PreRevisionIaService.EsReutilizable(previo with { CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) },
            prompt, "{\"documento_a_revisar\":{\"a\":2}}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Prompt_separa_hallazgos_de_observaciones_y_trae_reglas_de_memoria_y_cierre()
    {
        var fx = BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false).GetAwaiter().GetResult();
        fx.Svc.AuditarHcAsync(fx.HcId).GetAwaiter().GetResult();
        var p = fx.AiClient.LastSystemPrompt!;
        Assert.Contains("\"observaciones\"", p);
        Assert.Contains("HALLAZGOS vs OBSERVACIONES", p);
        Assert.Contains("REVISION ANTERIOR", p);
        Assert.Contains("MOMENTO DE CIERRE", p);
        Assert.DoesNotContain("puedes listar lo que validaste", p);
    }

    // ---- Reproceso (experimento de instrucciones) ----

    private const string BodyAprobadoConObs =
        "{\"resultado\":\"Aprobado\",\"confianza\":0.9,\"nota\":\"Acredita la atencion\",\"hallazgos\":[]," +
        "\"observaciones\":[\"Podria detallar la tolerancia\"],\"sugerencias\":[]}";

    /// <summary>Dos intentos al cerrar: el segundo log lleva 'revision_anterior' y 'momento'.</summary>
    private static async Task<(Fixture Fx, Guid LogId)> CorridaConMemoriaAsync()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyRechazoConSugerencia), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        await SetValoresAsync(fx, "{\"evolucion\":\"texto copiado del turno anterior\"}");
        var a1 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        await fx.Svc.GuardarLogIntentoCierreAsync(a1);
        await SetValoresAsync(fx, "{\"evolucion\":\"otro texto\"}");
        var a2 = await fx.Svc.AuditarHcAsync(fx.HcId, null, default, OpcionesAuditoriaIa.Cierre);
        var log2 = await fx.Svc.GuardarLogIntentoCierreAsync(a2);
        Assert.Contains("revision_anterior", fx.AiClient.LastUserMessage);
        return (fx, log2!.Value);
    }

    [Fact]
    public async Task Reproceso_reenvia_el_contexto_sin_memoria_y_no_escribe_logs()
    {
        var (fx, logId) = await CorridaConMemoriaAsync();
        var logsAntes = await fx.Db.RevisionAgenteLogs.CountAsync();
        fx.AiClient.Siguiente = OkResponse(BodyAprobadoConObs, 300, 50);

        var r = await fx.Svc.ReprocesarCorridaAsync(logId);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(3, fx.AiClient.CantidadLlamadas);
        Assert.Equal(fx.HcId, r.HistoriaClinicaId);
        Assert.Equal("Rechazado", r.Original!.Resultado);
        Assert.Equal("Aprobado", r.Nuevo!.Resultado);
        Assert.Empty(r.Nuevo.Hallazgos);
        Assert.Single(r.Nuevo.Observaciones);
        Assert.Equal(300, r.InputTokens);
        var msg = fx.AiClient.LastUserMessage!;
        Assert.DoesNotContain("revision_anterior", msg);
        Assert.Contains("\"documento_a_revisar\"", msg);
        Assert.Contains("\"momento\"", msg);
        Assert.Contains("CERRANDO", msg);
        Assert.Equal(logsAntes, await fx.Db.RevisionAgenteLogs.CountAsync());
        Assert.Equal(1, await fx.Db.AiUsageLogs.CountAsync(u => u.Source == PreRevisionIaService.FuenteUsoReproceso));
    }

    [Fact]
    public async Task Reproceso_con_instrucciones_propias_y_memoria()
    {
        var (fx, logId) = await CorridaConMemoriaAsync();
        var r = await fx.Svc.ReprocesarCorridaAsync(logId, new OpcionesReproceso("INSTRUCCIONES DE PRUEBA V9", ConMemoria: true));

        Assert.True(r.Ok, r.Error);
        Assert.Contains("INSTRUCCIONES DE PRUEBA V9", fx.AiClient.LastSystemPrompt);
        Assert.Contains("REVISION ANTERIOR", fx.AiClient.LastSystemPrompt);   // el sufijo de codigo se conserva
        Assert.Contains("revision_anterior", fx.AiClient.LastUserMessage);
    }

    [Fact]
    public async Task Reproceso_de_corrida_antigua_u_otro_tenant_no_llama_al_proveedor()
    {
        var fx = await BuildAsync(TenantA, OkResponse(BodyAprobadoConObs), crearRevision: false,
            estadoHc: HistoriaClinicaEstado.Abierta);
        var antigua = new RevisionAgenteLog
        {
            TenantId = TenantA, HistoriaClinicaId = fx.HcId, AgenteCodigo = "REVISOR", Ok = true,
            Resultado = "Rechazado", ContextoEnviado = "texto plano de una version anterior", CreatedAt = DateTimeOffset.UtcNow,
        };
        var ajena = new RevisionAgenteLog
        {
            TenantId = TenantB, HistoriaClinicaId = fx.HcId, AgenteCodigo = "REVISOR", Ok = true,
            Resultado = "Rechazado", ContextoEnviado = "{\"documento_a_revisar\":{}}", CreatedAt = DateTimeOffset.UtcNow,
        };
        fx.Db.RevisionAgenteLogs.AddRange(antigua, ajena);
        await fx.Db.SaveChangesAsync();

        var r1 = await fx.Svc.ReprocesarCorridaAsync(antigua.Id);
        var r2 = await fx.Svc.ReprocesarCorridaAsync(ajena.Id);

        Assert.False(r1.Ok);
        Assert.Contains("formato actual", r1.Error);
        Assert.False(r2.Ok);
        Assert.Equal("La corrida no existe.", r2.Error);
        Assert.Equal(0, fx.AiClient.CantidadLlamadas);
    }

    [Fact]
    public void PrepararContextoReproceso_agrega_momento_solo_al_cerrar()
    {
        const string ctx = "{\"documento_a_revisar\":{\"a\":1},\"revision_anterior\":{\"resultado\":\"Rechazado\"}}";
        var s1 = PreRevisionIaService.PrepararContextoReproceso(ctx, conMemoria: false, alCerrar: true, out var m1)!;
        Assert.False(m1);
        Assert.Contains("\"momento\"", s1);
        Assert.DoesNotContain("revision_anterior", s1);

        var s2 = PreRevisionIaService.PrepararContextoReproceso(ctx, conMemoria: true, alCerrar: false, out var m2)!;
        Assert.True(m2);
        Assert.DoesNotContain("\"momento\"", s2);
        Assert.Null(PreRevisionIaService.PrepararContextoReproceso("{\"otro\":1}", false, false, out _));
    }

    // ---- Fakes ----

    private sealed class FakeAiClient : IAiProviderClient
    {
        private readonly AiChatResult _respuesta;
        public int CantidadLlamadas { get; private set; }
        public string? LastSystemPrompt { get; private set; }
        public string? LastUserMessage { get; private set; }
        public AiChatResult? Siguiente { get; set; }

        public FakeAiClient(AiChatResult respuesta) { _respuesta = respuesta; }

        public Task<AiChatResult> CompleteAsync(
            AiProvider provider, string apiKey, string? baseUrl, string model,
            string systemPrompt, IReadOnlyList<AiChatTurn> turns,
            CancellationToken cancellationToken = default)
        {
            CantidadLlamadas++;
            LastSystemPrompt = systemPrompt;
            LastUserMessage = turns.LastOrDefault()?.Text;
            return Task.FromResult(Siguiente ?? _respuesta);
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

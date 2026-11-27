using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision.Bitacora;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>IMP-72: bitacora del auditor IA (analisis entre intentos + servicio + llave de la API).</summary>
public sealed class BitacoraIaTests
{
    private static readonly Guid Tenant = Guid.Parse("b1b1b1b1-b1b1-b1b1-b1b1-b1b1b1b1b1b1");
    private static readonly Guid OtroTenant = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 15, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- analisis (puro)

    private static BitacoraIntentoInput In(int min, string[] hallazgos, Dictionary<string, string?> campos,
        SugerenciaTexto[]? sug = null, bool ok = true, string resultado = "Rechazado")
        => new(Guid.NewGuid(), T0.AddMinutes(min), ok, ok ? resultado : null, hallazgos, sug ?? [], campos);

    [Fact]
    public void Primer_intento_marca_hallazgos_como_primero_sin_alertas()
    {
        var r = BitacoraIaAnalisis.Analizar([In(0, ["El plan de manejo no describe actividades"], new() { ["Plan de manejo"] = "x" })]);
        var a = Assert.Single(r);
        Assert.Equal(1, a.Numero);
        Assert.Null(a.AnteriorId);
        Assert.Equal("Primero", Assert.Single(a.Hallazgos).Estado);
        Assert.Equal(["Plan de manejo"], a.Hallazgos[0].CamposMencionados);
        Assert.Equal(0, a.Alertas);
    }

    [Fact]
    public void Repite_hallazgo_aunque_el_campo_cambio()
    {
        var i1 = In(0, ["El Plan de manejo no describe las actividades realizadas en la sesion"],
            new() { ["Plan de manejo"] = "continuar", ["Motivo de consulta"] = "dolor lumbar" });
        var i2 = In(5, ["Plan de manejo no describe actividades realizadas durante la sesion"],
            new() { ["Plan de manejo"] = "ejercicios de fortalecimiento y estiramiento 30 min", ["Motivo de consulta"] = "dolor lumbar" });
        var r = BitacoraIaAnalisis.Analizar([i1, i2]);
        var h = Assert.Single(r[1].Hallazgos);
        Assert.Equal("Repetido", h.Estado);
        Assert.True(h.CampoMencionadoCambio);
        Assert.Equal(BitacoraIaAnalisis.AlertaRepiteTrasCorregir, h.Alerta);
        Assert.Equal("Plan de manejo", Assert.Single(r[1].CamposCambiados).Campo);
        Assert.Equal(1, BitacoraIaAnalisis.Senales(r).RepiteTrasCorregir);
    }

    [Fact]
    public void Hallazgo_nuevo_sobre_campo_que_no_cambio()
    {
        var campos1 = new Dictionary<string, string?> { ["Plan de manejo"] = "continuar", ["Motivo de consulta"] = "dolor" };
        var campos2 = new Dictionary<string, string?> { ["Plan de manejo"] = "ejercicios 30 min", ["Motivo de consulta"] = "dolor" };
        var r = BitacoraIaAnalisis.Analizar([
            In(0, ["El plan de manejo es generico"], campos1),
            In(5, ["El motivo de consulta esta incompleto y no indica tiempo de evolucion"], campos2)]);
        var h = Assert.Single(r[1].Hallazgos);
        Assert.Equal("Nuevo", h.Estado);
        Assert.False(h.CampoMencionadoCambio);
        Assert.Equal(BitacoraIaAnalisis.AlertaNuevoSinCambio, h.Alerta);
        Assert.Equal(["El plan de manejo es generico"], r[1].HallazgosResueltos);
    }

    [Fact]
    public void Contradice_su_propia_sugerencia_copiada()
    {
        const string sugerido = "Paciente tolera la sesion de terapia fisica sin complicaciones, se realizan ejercicios de movilidad";
        var r = BitacoraIaAnalisis.Analizar([
            In(0, ["La evolucion no describe la respuesta del paciente"], new() { ["Evolucion"] = "bien" },
                [new SugerenciaTexto("Evolucion", sugerido)]),
            In(5, ["El campo 'Evolucion' es copiado y no individualiza al paciente"], new() { ["Evolucion"] = sugerido })]);
        Assert.Equal(["Evolucion"], r[1].SugerenciasAdoptadas);
        Assert.Equal(BitacoraIaAnalisis.AlertaContradiceSugerencia, Assert.Single(r[1].Hallazgos).Alerta);
    }

    [Fact]
    public void Sugerencia_redactada_con_el_mismo_texto_no_cuenta_como_adoptada_si_el_campo_no_cambio()
    {
        // El agente reescribe con las MISMAS palabras del campo: sin edicion del profesional no hay adopcion.
        const string texto = "Paciente tolera la sesion de terapia fisica sin complicaciones";
        var r = BitacoraIaAnalisis.Analizar([
            In(0, ["x"], new() { ["Evolucion"] = texto }, [new SugerenciaTexto("Evolucion", texto + ". [COMPLETAR: respuesta]")]),
            In(5, ["El campo 'Evolucion' no individualiza al paciente"], new() { ["Evolucion"] = texto })]);
        Assert.Empty(r[1].SugerenciasAdoptadas);
        Assert.NotEqual(BitacoraIaAnalisis.AlertaContradiceSugerencia, r[1].Hallazgos[0].Alerta);
    }

    [Fact]
    public void Adopcion_sigue_vigente_mientras_el_campo_no_cambie()
    {
        const string sugerido = "Paciente tolera la sesion de terapia fisica sin complicaciones, se realizan ejercicios de movilidad";
        var r = BitacoraIaAnalisis.Analizar([
            In(0, ["a"], new() { ["Evolucion"] = "bien" }, [new SugerenciaTexto("Evolucion", sugerido)]),
            In(5, ["b"], new() { ["Evolucion"] = sugerido, ["Plan"] = "1" }),
            In(9, ["El campo 'Evolucion' es generico"], new() { ["Evolucion"] = sugerido, ["Plan"] = "2" })]);
        Assert.Equal(BitacoraIaAnalisis.AlertaContradiceSugerencia, Assert.Single(r[2].Hallazgos).Alerta);
    }

    [Fact]
    public void Intento_fallido_no_rompe_la_cadena_y_diff_ignora_claves_internas_y_espacios()
    {
        var r = BitacoraIaAnalisis.Analizar([
            In(0, ["Falta firma"], new() { ["Plan"] = "a  b", ["__tmpl:plan"] = "x" }),
            In(3, [], new(), ok: false),
            In(6, [], new() { ["Plan"] = "a b", ["__tmpl:plan"] = "y" }, resultado: "Aprobado")]);
        Assert.Equal(3, r.Count);
        Assert.Equal(r[0].Id, r[2].AnteriorId);
        Assert.Empty(r[2].CamposCambiados);
        Assert.Equal(["Falta firma"], r[2].HallazgosResueltos);
    }

    // ---------------------------------------------------------------- servicio

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class NoopAudit : IAuditWriter
    {
        public int Escritos;
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) => Escritos++;
        public void Write(IApplicationDbContext db, Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human) => Escritos++;
    }

    private sealed class PlainProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext[4..];
    }

    private static VisalDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext { TenantId = Tenant });

    private static (BitacoraIaService Svc, NoopAudit Audit) Svc(VisalDbContext ctx)
    {
        var audit = new NoopAudit();
        return (new BitacoraIaService(new TestDbContextFactory(ctx, new FakeTenantContext { TenantId = Tenant }),
            new PlainProtector(), audit, TimeProvider.System), audit);
    }

    private static async Task<Guid> SembrarHcAsync(VisalDbContext ctx, Guid tenant, long consecutivo, string profesional, string documento)
    {
        var pac = new Paciente { TenantId = tenant, NumeroDocumento = documento, NombreCompleto = "PACIENTE " + consecutivo };
        var form = new FormDefinition { TenantId = tenant, Codigo = "TF-01", Nombre = "Terapia fisica" };
        var prof = new Profesional { TenantId = tenant, NumeroDocumento = "P" + consecutivo, NombreCompleto = profesional };
        ctx.Pacientes.Add(pac);
        ctx.FormDefinitions.Add(form);
        ctx.Profesionales.Add(prof);
        var hc = new HistoriaClinica
        {
            TenantId = tenant, Consecutivo = consecutivo, PacienteId = pac.Id, FormDefinitionId = form.Id,
            ProfesionalId = prof.Id, FechaApertura = T0,
        };
        ctx.HistoriasClinicas.Add(hc);
        await ctx.SaveChangesAsync();
        return hc.Id;
    }

    private static RevisionAgenteLog Log(Guid tenant, Guid hcId, DateTimeOffset en, string resultado, string hallazgosJson, string camposJson,
        bool? feedback = null)
        => new()
        {
            TenantId = tenant, HistoriaClinicaId = hcId, AgenteCodigo = "REVISOR CLINICO IA", Origen = "Cierre",
            Ok = true, Resultado = resultado, Confianza = 0.8m, TokensIn = 1000, TokensOut = 100,
            Nota = "nota", HallazgosJson = hallazgosJson, CamposLegiblesJson = camposJson,
            SystemPrompt = "PROMPT", ContextoEnviado = "CTX", RespuestaCruda = "RAW",
            FeedbackUtil = feedback, CreatedAt = en,
        };

    /// <summary>CreatedAt lo pisa SaveChanges con la hora actual: se fija despues de guardar.</summary>
    private static async Task GuardarLogsAsync(VisalDbContext ctx, params RevisionAgenteLog[] logs)
    {
        var fechas = logs.Select(l => l.CreatedAt).ToArray();
        ctx.RevisionAgenteLogs.AddRange(logs);
        await ctx.SaveChangesAsync();
        for (var i = 0; i < logs.Length; i++) { logs[i].CreatedAt = fechas[i]; }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Lista_por_historia_con_senales_filtra_por_dia_local_y_aisla_tenant()
    {
        using var ctx = NewCtx();
        var hc1 = await SembrarHcAsync(ctx, Tenant, 123, "ANA PEREZ", "1001");
        var hc2 = await SembrarHcAsync(ctx, Tenant, 124, "LUIS GOMEZ", "1002");
        var ajena = await SembrarHcAsync(ctx, OtroTenant, 900, "OTRA", "9999");
        // 27-nov 03:00 UTC = 26-nov 22:00 en Colombia: debe contar como 26.
        var noche = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        await GuardarLogsAsync(ctx,
            Log(Tenant, hc1, noche.AddMinutes(-30), "Rechazado", "[\"El Plan de manejo no describe actividades realizadas\"]",
                "{\"Plan de manejo\":\"seguir\",\"Motivo de consulta\":\"dolor\"}"),
            Log(Tenant, hc1, noche, "Rechazado",
                "[\"Plan de manejo no describe las actividades realizadas\",\"El motivo de consulta no indica tiempo de evolucion\"]",
                "{\"Plan de manejo\":\"ejercicios 30 minutos\",\"Motivo de consulta\":\"dolor\"}", feedback: false),
            Log(Tenant, hc2, T0, "Aprobado", "[]", "{}"),
            Log(Tenant, hc2, new DateTimeOffset(2026, 11, 25, 15, 0, 0, TimeSpan.Zero), "Rechazado", "[\"x\"]", "{}"),
            Log(OtroTenant, ajena, T0, "Rechazado", "[\"y\"]", "{}"));

        var (svc, _) = Svc(ctx);
        var dia = new DateOnly(2026, 11, 26);
        var pag = await svc.ListarHistoriasAsync(Tenant, new BitacoraFiltro(dia, dia), 1, 50);
        Assert.Equal(2, pag.Total);
        Assert.DoesNotContain(pag.Items, x => x.Hc == "HC-000900");
        var f1 = pag.Items.Single(x => x.Hc == "HC-000123");
        Assert.Equal(2, f1.Corridas);
        Assert.Equal(1, f1.Senales.RepiteTrasCorregir);
        Assert.Equal(1, f1.Senales.NuevoSinCambio);
        Assert.Equal(1, f1.Senales.Total); // "insiste tras corregir" no cuenta como incoherencia
        Assert.Equal(1, f1.PulgarAbajo);
        Assert.Equal("ANA PEREZ", f1.Profesional);
        // La HC2 trae tambien su corrida del 25 (historia completa), aunque el filtro sea del 26.
        Assert.Equal(2, pag.Items.Single(x => x.Hc == "HC-000124").Corridas);

        var alertas = await svc.ListarHistoriasAsync(Tenant, new BitacoraFiltro(dia, dia, SoloAlertas: true), 1, 50);
        Assert.Equal("HC-000123", Assert.Single(alertas.Items).Hc);

        var porProf = await svc.ListarHistoriasAsync(Tenant, new BitacoraFiltro(dia, dia, Profesional: "luis"), 1, 50);
        Assert.Equal("HC-000124", Assert.Single(porProf.Items).Hc);

        var resumen = await svc.ResumenAsync(Tenant, new BitacoraFiltro(dia, dia));
        Assert.Equal(3, resumen.Corridas);
        Assert.Equal(2, resumen.Rechazadas);
        Assert.Equal(1, resumen.Aprobadas);
        Assert.Equal(1, resumen.HistoriasConAlertas);
        Assert.Equal(1, resumen.PulgarAbajo);
    }

    [Fact]
    public async Task Detalle_por_codigo_y_corrida_completa_respetan_el_tenant()
    {
        using var ctx = NewCtx();
        var hc = await SembrarHcAsync(ctx, Tenant, 33421, "ANA", "1001");
        var ajena = await SembrarHcAsync(ctx, OtroTenant, 33422, "OTRA", "9999");
        var l1 = Log(Tenant, hc, T0, "Rechazado", "[\"a\"]", "{}");
        var l2 = Log(OtroTenant, ajena, T0, "Rechazado", "[\"b\"]", "{}");
        await GuardarLogsAsync(ctx, l1, l2);

        var (svc, _) = Svc(ctx);
        var det = await svc.DetalleHistoriaAsync(Tenant, "HC-033421");
        Assert.NotNull(det);
        Assert.Single(det!.Corridas);
        Assert.Null(await svc.DetalleHistoriaAsync(Tenant, "HC-033422"));

        var comp = await svc.CorridaAsync(Tenant, l1.Id);
        Assert.Equal("PROMPT", comp!.SystemPrompt);
        Assert.Equal("HC-033421", comp.Hc);
        Assert.Null(await svc.CorridaAsync(Tenant, l2.Id));
    }

    [Fact]
    public async Task Llave_de_la_api_resuelve_tenant_audita_y_se_puede_apagar_o_rotar()
    {
        using var ctx = NewCtx();
        var (svc, audit) = Svc(ctx);
        var cfg = await svc.RegenerarApiAsync(Tenant, Guid.NewGuid());
        Assert.StartsWith("vbi_", cfg.ApiKey);
        Assert.Equal(cfg.ApiKey, (await svc.GetApiAsync(Tenant)).ApiKey);

        var antes = audit.Escritos;
        Assert.Equal(Tenant, await svc.ResolverLlaveAsync(cfg.ApiKey!, "/api/bitacora-ia/resumen", null));
        Assert.Equal(antes + 1, audit.Escritos);
        Assert.Null(await svc.ResolverLlaveAsync("vbi_falsa", "/x", null));

        await svc.SetApiHabilitadaAsync(Tenant, false, Guid.NewGuid());
        Assert.Null(await svc.ResolverLlaveAsync(cfg.ApiKey!, "/x", null));

        await svc.SetApiHabilitadaAsync(Tenant, true, Guid.NewGuid());
        var nueva = await svc.RegenerarApiAsync(Tenant, Guid.NewGuid());
        Assert.Null(await svc.ResolverLlaveAsync(cfg.ApiKey!, "/x", null));
        Assert.Equal(Tenant, await svc.ResolverLlaveAsync(nueva.ApiKey!, "/x", null));
    }
}

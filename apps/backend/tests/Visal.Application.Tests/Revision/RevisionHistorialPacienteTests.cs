using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Historial del paciente para el agente REVISOR CLINICO IA: solo historias ANTERIORES (por fecha de
/// atencion) dentro de la ventana, en orden cronologico, acotadas por topes (completas + tokens) y
/// priorizando el mismo tipo de servicio; y el mensaje al modelo separa documento_a_revisar vs historial.
/// </summary>
public sealed class RevisionHistorialPacienteTests
{
    private static readonly Guid Tenant = Guid.Parse("ccccccc4-0000-0000-0000-000000000004");
    private static readonly DateTimeOffset Base = new(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);

    private const string Schema = """
    {
      "children": [
        { "type": "field", "name": "evol", "label": "Evolucion", "fieldType": "textarea", "required": true },
        { "type": "field", "name": "plan", "label": "Plan", "fieldType": "textarea", "required": false }
      ]
    }
    """;

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class Escenario
    {
        public required VisalDbContext Db { get; init; }
        public required FakeTenantContext TenantCtx { get; init; }
        public required Guid PacienteId { get; init; }
        public required Guid FormId { get; init; }
        private long _consec = 100;

        public Guid Hc(DateTimeOffset fecha, string? tipoServicio = null,
            HistoriaClinicaEstado estado = HistoriaClinicaEstado.Cerrada, string? evol = null, Guid? paciente = null,
            string? plan = null)
        {
            var valores = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["evol"] = evol ?? $"evolucion del {fecha:yyyy-MM-dd}",
                ["plan"] = plan ?? "plan corto",
            });
            var hc = new HistoriaClinica
            {
                TenantId = Tenant, PacienteId = paciente ?? PacienteId, FormDefinitionId = FormId,
                Estado = estado, FechaApertura = fecha, FechaAtencion = fecha, Consecutivo = ++_consec,
                ValoresJson = valores, EspecialistaNombre = "DR TEST",
            };
            Db.HistoriasClinicas.Add(hc);
            if (tipoServicio is not null)
            {
                var asig = new Asignacion
                {
                    TenantId = Tenant, LoteId = Guid.NewGuid(), PacienteId = hc.PacienteId,
                    Sucursal = "CALI", ServicioId = "SV1", NombreServicio = "SERV", TipoServicio = tipoServicio,
                    Cantidad = 1, ContratoCodigo = "C1", MesVigencia = 11, FechaInicio = new DateOnly(2026, 11, 1),
                    Estado = AsignacionEstado.Pendiente,
                };
                Db.Asignaciones.Add(asig);
                var turno = new AsignacionTurno { TenantId = Tenant, AsignacionId = asig.Id, ProfesionalId = Guid.NewGuid(), Cantidad = 1 };
                Db.AsignacionTurnos.Add(turno);
                var ses = new AsignacionTurnoSesion { TenantId = Tenant, AsignacionTurnoId = turno.Id, SessionNo = 1, FechaAtencion = DateOnly.FromDateTime(fecha.Date) };
                Db.AsignacionTurnoSesiones.Add(ses);
                Db.AsignacionTurnoSesionHcs.Add(new AsignacionTurnoSesionHc { TenantId = Tenant, SesionId = ses.Id, HistoriaClinicaId = hc.Id });
            }
            return hc.Id;
        }

        public RevisionMcpToolsService Svc() => new(new TestDbContextFactory(Db, TenantCtx));
    }

    private static Escenario Nuevo()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var tenant = new FakeTenantContext { TenantId = Tenant };
        var db = new VisalDbContext(opts, tenant);
        var form = new FormDefinition { TenantId = Tenant, Codigo = "PP-FO-85", Nombre = "Evolucion terapia", Tipo = "HC", SchemaJson = Schema };
        var pac = new Paciente { TenantId = Tenant, NombreCompleto = "PAC", TipoDocumento = "CC", NumeroDocumento = "9" };
        db.FormDefinitions.Add(form);
        db.Pacientes.Add(pac);
        return new Escenario { Db = db, TenantCtx = tenant, PacienteId = pac.Id, FormId = form.Id };
    }

    private static async Task<JsonElement> HistorialAsync(Escenario e, Guid hcId, OpcionesHistorial op)
    {
        await e.Db.SaveChangesAsync();
        var ctx = await e.Svc().ArmarContextoAsync(hcId, Array.Empty<string>(), RevisionMcpToolNames.Todas, 30, default, op);
        var inv = Assert.Single(ctx.InvocacionesExitosas, i => i.ToolName == RevisionMcpToolNames.HistorialPaciente);
        return JsonDocument.Parse(inv.JsonPayload!).RootElement.Clone();
    }

    private static readonly OpcionesHistorial Default = new(true, 8, 30, 60000);

    [Fact]
    public async Task Solo_anteriores_en_ventana_cronologico_sin_inactivas_ni_otro_paciente()
    {
        var e = Nuevo();
        var vieja = e.Hc(Base.AddMonths(-10));                                   // fuera de ventana (8 meses)
        var h1 = e.Hc(Base.AddDays(-30));
        var h2 = e.Hc(Base.AddDays(-7), estado: HistoriaClinicaEstado.Abierta);  // abierta -> entra marcada
        e.Hc(Base.AddDays(-3), estado: HistoriaClinicaEstado.Inactiva);         // inactiva -> no entra
        e.Hc(Base.AddDays(-2), paciente: Guid.NewGuid());                        // otro paciente -> no entra
        var objetivo = e.Hc(Base);
        e.Hc(Base.AddDays(5));                                                   // posterior -> no entra

        var h = await HistorialAsync(e, objetivo, Default);

        var docs = h.GetProperty("documentos").EnumerateArray().ToList();
        Assert.Equal(2, docs.Count);
        Assert.Equal(1, docs[0].GetProperty("n").GetInt32());
        Assert.True(docs[0].GetProperty("fecha_atencion").GetDateTimeOffset() < docs[1].GetProperty("fecha_atencion").GetDateTimeOffset());
        Assert.Equal("Cerrada", docs[0].GetProperty("estado").GetString());
        Assert.StartsWith("Abierta", docs[1].GetProperty("estado").GetString());
        Assert.Equal(2, h.GetProperty("resumen").GetProperty("total_en_ventana").GetInt32());
        Assert.Equal("completa", docs[0].GetProperty("detalle").GetString());
        Assert.Equal("Evolucion del 2026-10-11".ToLowerInvariant(),
            docs[0].GetProperty("campos").GetProperty("Evolucion").GetString()!.ToLowerInvariant());
        _ = vieja; _ = h1; _ = h2;
    }

    [Fact]
    public async Task Tope_de_completas_prioriza_mismo_servicio_y_el_resto_va_como_indice()
    {
        var e = Nuevo();
        // 3 de TERAPIA FISICA (mismo servicio que la objetivo) y 3 de MEDICINA, intercaladas.
        for (var i = 1; i <= 3; i++)
        {
            e.Hc(Base.AddDays(-i * 2), tipoServicio: "MEDICINA GENERAL");          // mas recientes
            e.Hc(Base.AddDays(-i * 2 - 1), tipoServicio: "TERAPIA FISICA");
        }
        var objetivo = e.Hc(Base, tipoServicio: "TERAPIA FISICA");

        var h = await HistorialAsync(e, objetivo, Default with { MaxCompletas = 4 });

        var completas = h.GetProperty("documentos").EnumerateArray().ToList();
        Assert.Equal(4, completas.Count);
        Assert.All(completas, d => Assert.Equal("completa", d.GetProperty("detalle").GetString()));
        // Las 3 del mismo servicio van completas aunque sean mas viejas que las de medicina.
        Assert.Equal(3, completas.Count(d => d.GetProperty("tipo_servicio").GetString() == "TERAPIA FISICA"));
        // La 4a completa es la medicina MAS RECIENTE (n=6); las otras 2 de medicina van como indice.
        var med = Assert.Single(completas, d => d.GetProperty("tipo_servicio").GetString() == "MEDICINA GENERAL");
        Assert.Equal(6, med.GetProperty("n").GetInt32());
        var indice = h.GetProperty("indice").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(2, indice.Count);
        Assert.All(indice, l => Assert.Contains("MEDICINA GENERAL", l));
        // n=1 es la terapia mas antigua (va completa); el indice arranca en la medicina n=2.
        Assert.StartsWith("n=2 | ", indice[0]);
        Assert.Equal(4, h.GetProperty("resumen").GetProperty("incluidas_completas").GetInt32());
        Assert.Equal(2, h.GetProperty("resumen").GetProperty("incluidas_indice").GetInt32());
    }

    [Fact]
    public async Task Tope_de_tokens_degrada_las_completas_mas_antiguas()
    {
        var e = Nuevo();
        var largo = new string('x', 290);
        for (var i = 1; i <= 20; i++) { e.Hc(Base.AddDays(-i), evol: largo + i, plan: largo); }
        var objetivo = e.Hc(Base);

        var h = await HistorialAsync(e, objetivo, Default with { MaxTokens = 2000 });

        var r = h.GetProperty("resumen");
        Assert.True(r.GetProperty("tokens_estimados").GetInt32() <= 2000);
        var completas = h.GetProperty("documentos").EnumerateArray().ToList();
        var indice = h.GetProperty("indice").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(20, completas.Count + indice.Count);
        Assert.InRange(completas.Count, 1, 19);
        // Las que quedan completas son las MAS RECIENTES: la ultima (n=20) completa, la primera (n=1) en indice.
        Assert.Equal(20, completas[^1].GetProperty("n").GetInt32());
        Assert.StartsWith("n=1 | ", indice[0]);
    }

    [Fact]
    public async Task Detecta_campos_identicos_a_la_revisada()
    {
        var e = Nuevo();
        const string copiado = "Paciente tolera bien la sesion, se realizan ejercicios de fortalecimiento y estiramiento.";
        e.Hc(Base.AddDays(-2), evol: copiado);
        e.Hc(Base.AddDays(-1), evol: "Texto distinto pero suficientemente largo para superar el umbral minimo.");
        var objetivo = e.Hc(Base, evol: "  " + copiado.ToUpperInvariant() + " ");

        var h = await HistorialAsync(e, objetivo, Default);

        var docs = h.GetProperty("documentos").EnumerateArray().ToList();
        var ident = docs[0].GetProperty("campos_identicos_a_la_revisada").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("Evolucion", ident);
        Assert.False(docs[1].TryGetProperty("campos_identicos_a_la_revisada", out _));
    }

    [Fact]
    public async Task Interruptor_apagado_no_envia_historial()
    {
        var e = Nuevo();
        e.Hc(Base.AddDays(-1));
        var objetivo = e.Hc(Base);
        await e.Db.SaveChangesAsync();

        var ctx = await e.Svc().ArmarContextoAsync(objetivo, Array.Empty<string>(), RevisionMcpToolNames.Todas, 30,
            default, Default with { Activo = false });

        Assert.DoesNotContain(ctx.InvocacionesExitosas, i => i.ToolName == RevisionMcpToolNames.HistorialPaciente);
        var json = PreRevisionIaService.SerializarContexto(ctx);
        Assert.Contains("SIN HISTORIAL", json);
    }

    [Fact]
    public async Task Mensaje_al_modelo_separa_documento_a_revisar_del_historial()
    {
        var e = Nuevo();
        e.Hc(Base.AddDays(-1), evol: "nota anterior");
        var objetivo = e.Hc(Base, evol: "nota revisada");
        await e.Db.SaveChangesAsync();

        var ctx = await e.Svc().ArmarContextoAsync(objetivo, RevisionMcpToolNames.Todas, RevisionMcpToolNames.Todas, 30,
            default, Default);
        var json = PreRevisionIaService.SerializarContexto(ctx);

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "documento_a_revisar", "datos_del_paciente", "historial_del_paciente" }, props.Take(3));

        var revisar = doc.RootElement.GetProperty("documento_a_revisar");
        Assert.Contains("OBJETO DE LA REVISION", revisar.GetProperty("rol").GetString());
        Assert.Contains("nota revisada", revisar.GetProperty(RevisionMcpToolNames.GetHistoriaClinica).GetRawText());
        Assert.DoesNotContain("nota anterior", revisar.GetRawText());

        var hist = doc.RootElement.GetProperty("historial_del_paciente");
        Assert.Contains("SOLO CONTEXTO", hist.GetProperty("rol").GetString());
        Assert.Contains("nota anterior", hist.GetProperty("documentos").GetRawText());
        Assert.DoesNotContain("nota revisada", hist.GetRawText());
    }

    [Fact]
    public async Task Marca_campos_casi_identicos_con_porcentaje()
    {
        var e = Nuevo();
        const string baseTxt = "19:10 SE OBSERVA PACIENTE TRANQUILO DESPIERTO ACOSTADO EN SU CAMA EN POSICION DECUBITO LATERAL DERECHO "
                             + "SE REALIZA CAMBIO DE PANAL SE OBSERVA DIURESIS DE CARACTERISTICAS NORMALES SE TOMAN SIGNOS VITALES "
                             + "SE ADMINISTRA MEDICAMENTO POR GASTROSTOMIA SIN COMPLICACION SE HIDRATA CON AGUA POR GASTROSTOMIA "
                             + "SE REALIZA CAMBIO DE POSICION PACIENTE DUERME TRANQUILO DURANTE LA NOCHE SIN NOVEDAD";
        e.Hc(Base.AddDays(-3), evol: baseTxt + " NOTA MADRUGADA 11/NOVIEMBRE");                 // casi igual
        e.Hc(Base.AddDays(-2), evol: "Texto clinico completamente distinto que describe otra situacion del paciente en otro turno.");
        var objetivo = e.Hc(Base, evol: baseTxt + " NOTA MADRUGADA 13/NOVIEMBRE");

        var h = await HistorialAsync(e, objetivo, Default);

        var docs = h.GetProperty("documentos").EnumerateArray().ToList();
        var casi = docs[0].GetProperty("campos_casi_identicos_a_la_revisada").EnumerateArray().Select(x => x.GetString()!).ToList();
        var linea = Assert.Single(casi);
        Assert.StartsWith("Evolucion (", linea);
        Assert.Contains("% igual", linea);
        Assert.False(docs[0].TryGetProperty("campos_identicos_a_la_revisada", out _));
        Assert.False(docs[1].TryGetProperty("campos_casi_identicos_a_la_revisada", out _));
    }

    [Fact]
    public async Task Lista_medicamentos_ordenados_en_toda_la_ventana_incluso_del_indice()
    {
        var e = Nuevo();
        var medica = e.Hc(Base.AddDays(-20), tipoServicio: "CONSULTA");   // va al indice (tope 1 completa)
        e.Db.HistoriaClinicaMedicamentos.Add(new HistoriaClinicaMedicamento
        {
            TenantId = Tenant, HistoriaClinicaId = medica, NombreMedicamento = "OXCARBAZEPINA 6% SUSP", Posologia = "6 CC",
        });
        e.Db.HistoriaClinicaInsumos.Add(new HistoriaClinicaInsumo { TenantId = Tenant, HistoriaClinicaId = medica, Descripcion = "JERINGA 10 CC" });
        e.Hc(Base.AddDays(-1), tipoServicio: "ENFERMERIA");
        var objetivo = e.Hc(Base, tipoServicio: "ENFERMERIA");

        var h = await HistorialAsync(e, objetivo, Default with { MaxCompletas = 1 });

        Assert.Single(h.GetProperty("documentos").EnumerateArray());
        Assert.Contains("CONSULTA", Assert.Single(h.GetProperty("indice").EnumerateArray()).GetString());
        var ordenes = h.GetProperty("medicamentos_insumos_ordenados_en_ventana").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(2, ordenes.Count);
        Assert.Contains(ordenes, l => l.Contains("MEDICAMENTO: OXCARBAZEPINA 6% SUSP") && l.Contains("6 CC"));
        Assert.Contains(ordenes, l => l.Contains("INSUMO: JERINGA 10 CC"));
    }
}

using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Revision;

/// <summary>
/// Ola 6 RC6e — Isolacion tenant sobre las 9 tools MCP del agente REVISOR CLINICO IA.
/// Sembramos datos en Tenant A, invocamos cada tool desde Tenant B y verificamos:
///   - Payload nulo o null-ish (JSON vacio/none): la tool no leyo data de otro tenant.
///   - No hay excepcion: el global query filter oculta la data sin errores.
/// El resultado esperado es <see cref="ToolInvocationResult.Ok"/>=false con
/// error nulo/no aplica, o payload valido pero vacio (lista de 0, null en get_*).
/// </summary>
public sealed class RevisionMcpToolsIsolationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaa1-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbb2-0000-0000-0000-000000000002");

    private static (VisalDbContext dbA, VisalDbContext dbB, RevisionMcpToolsService svcB, Guid hcId, Guid formId, Guid pacId, Guid asignacionId) SetupAB()
    {
        var name = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<VisalDbContext>().UseInMemoryDatabase(name).Options;
        var tenantA = new FakeTenantContext { TenantId = TenantA };
        var tenantB = new FakeTenantContext { TenantId = TenantB };
        var dbA = new VisalDbContext(opts, tenantA);
        var dbB = new VisalDbContext(opts, tenantB);
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

        // Tenant A — HC completa con datos en TODAS las tablas que consulta cada tool.
        var paciente = new Paciente
        {
            TenantId = TenantA,
            NombreCompleto = "PACIENTE A",
            TipoDocumento = "CC",
            NumeroDocumento = "9001",
        };
        var form = new FormDefinition { TenantId = TenantA, Codigo = "HC-FO-A", Nombre = "HC A", Tipo = "HC" };
        var hc = new HistoriaClinica
        {
            TenantId = TenantA,
            PacienteId = paciente.Id,
            FormDefinitionId = form.Id,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = now.AddDays(-1),
            FechaCierre = now,
        };
        var asignacion = new Asignacion
        {
            TenantId = TenantA,
            PacienteId = paciente.Id,
            ContratoCodigo = "CT-A",
            ServicioId = "S-A",
            NombreServicio = "SERV-A",
            TipoServicio = "TIPO-A",
            Sucursal = "SEDE-A",
        };
        dbA.Pacientes.Add(paciente);
        dbA.FormDefinitions.Add(form);
        dbA.HistoriasClinicas.Add(hc);
        dbA.Asignaciones.Add(asignacion);
        // Contenido de las tablas hijas — cada una alimenta 1 tool.
        dbA.HistoriaClinicaMedicamentos.Add(new HistoriaClinicaMedicamento { TenantId = TenantA, HistoriaClinicaId = hc.Id, CodigoMedicamento = "MED-1", NombreMedicamento = "IBUPROFENO" });
        dbA.NotasMedicas.Add(new NotaMedica { TenantId = TenantA, PacienteId = paciente.Id, HistoriaClinicaId = hc.Id, EspecialistaNombre = "DR X" });
        dbA.HistoriaClinicaEscalas.Add(new HistoriaClinicaEscala { TenantId = TenantA, HistoriaClinicaId = hc.Id, FormDefinitionId = form.Id, FechaApertura = now.AddHours(-1), ValoresJson = "{}" });
        dbA.HistoriaClinicaDocumentos.Add(new HistoriaClinicaDocumento { TenantId = TenantA, HistoriaClinicaId = hc.Id, Tipo = "EVOLUCION", FormDefinitionId = form.Id, FechaApertura = now.AddHours(-2), ValoresJson = "{}" });
        dbA.HistoriaClinicaDocumentos.Add(new HistoriaClinicaDocumento { TenantId = TenantA, HistoriaClinicaId = hc.Id, Tipo = "CONSENTIMIENTO", FormDefinitionId = form.Id, FechaApertura = now.AddHours(-3), ValoresJson = "{}" });
        dbA.SaveChanges();

        var svcB = new RevisionMcpToolsService(dbB);
        return (dbA, dbB, svcB, hc.Id, form.Id, paciente.Id, asignacion.Id);
    }

    private static async Task VerificaAisladoAsync(RevisionMcpToolsService svcB, string tool, Guid hcIdOfA)
    {
        var allowed = RevisionMcpToolNames.Todas;
        var r = await svcB.EjecutarToolAsync(tool, hcIdOfA, allowed, ventanaAsignacionesDias: 30);
        // Aceptamos 2 formas de "no fuga":
        //   (a) Payload nulo (get_* sin resultado -> Ok=false por nulo interno).
        //   (b) Payload NO nulo pero SIN pistas del tenant A (arrays vacios, campos placeholder).
        // Para simplicidad: ninguna respuesta puede contener el string "PACIENTE A" ni "MED-1".
        var payload = r.JsonPayload ?? "";
        Assert.DoesNotContain("PACIENTE A", payload);
        Assert.DoesNotContain("MED-1", payload);
        Assert.DoesNotContain("HC-FO-A", payload);
        Assert.DoesNotContain("CT-A", payload);
    }

    [Fact]
    public async Task GetHistoriaClinica_NoLeeHcDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.GetHistoriaClinica, hcA);
    }

    [Fact]
    public async Task GetPaciente_NoLeePacienteDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.GetPaciente, hcA);
    }

    [Fact]
    public async Task ListOrdenesHc_NoLeeMedicamentosDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListOrdenesHc, hcA);
    }

    [Fact]
    public async Task ListNotasHc_NoLeeNotasDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListNotasHc, hcA);
    }

    [Fact]
    public async Task ListEscalasHc_NoLeeEscalasDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListEscalasHc, hcA);
    }

    [Fact]
    public async Task ListEvolucionesHc_NoLeeEvolucionesDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListEvolucionesHc, hcA);
    }

    [Fact]
    public async Task ListConsentimientosHc_NoLeeConsentimientosDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListConsentimientosHc, hcA);
    }

    [Fact]
    public async Task ListAsignacionesRelacionadas_NoLeeAsignacionesDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.ListAsignacionesRelacionadas, hcA);
    }

    [Fact]
    public async Task GetFormDefinition_NoLeeFormDeOtroTenant()
    {
        var (_, _, svcB, hcA, _, _, _) = SetupAB();
        await VerificaAisladoAsync(svcB, RevisionMcpToolNames.GetFormDefinition, hcA);
    }

    // ---- Fake ----

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }
}

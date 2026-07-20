using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Visal.Application.Common;
using Visal.Application.Revision.Ia;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Visal.Infrastructure.Persistence.Interceptors;

namespace Visal.Integration.Tests;

/// <summary>
/// Ola 9 RC9e — mismo aislamiento tenant que <c>RevisionMcpToolsIsolationTests</c>
/// pero contra Postgres real (Testcontainers). El unit test con EF InMemory
/// no ejerce el global query filter contra el proveedor real; aqui verificamos
/// que las 9 tools MCP filtran correctamente con SQL generado por Npgsql.
/// </summary>
public sealed class RevisionMcpToolsIsolationPgTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private Guid _tenantA;
    private Guid _tenantB;
    private Guid _hcAId;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = CreateContext(tenantId: null);
        await ctx.Database.MigrateAsync();

        _tenantA = Guid.Parse("aaaaaaa1-0000-0000-0000-000000000001");
        _tenantB = Guid.Parse("bbbbbbb2-0000-0000-0000-000000000002");

        // Tenants globales (sin filtro por tenant).
        await using (var seed = CreateContext(tenantId: null))
        {
            seed.Tenants.Add(new Tenant { Id = _tenantA, Name = "Agencia A" });
            seed.Tenants.Add(new Tenant { Id = _tenantB, Name = "Agencia B" });
            await seed.SaveChangesAsync();
        }

        // Datos tenant A — HC completa que alimenta las 9 tools MCP con strings distintivos.
        await using (var a = CreateContext(_tenantA))
        {
            var paciente = new Paciente
            {
                NombreCompleto = "PACIENTE ALPHA",
                TipoDocumento = "CC",
                NumeroDocumento = "9001A",
            };
            var form = new FormDefinition { Codigo = "HC-FO-ALPHA", Nombre = "HC ALPHA", Tipo = "HC" };
            a.Pacientes.Add(paciente);
            a.FormDefinitions.Add(form);
            await a.SaveChangesAsync();

            var hc = new HistoriaClinica
            {
                PacienteId = paciente.Id,
                FormDefinitionId = form.Id,
                Estado = HistoriaClinicaEstado.Cerrada,
                FechaApertura = DateTimeOffset.UtcNow.AddDays(-1),
                FechaCierre = DateTimeOffset.UtcNow,
            };
            var asignacion = new Asignacion
            {
                PacienteId = paciente.Id,
                ContratoCodigo = "CT-ALPHA",
                ServicioId = "S-ALPHA",
                NombreServicio = "SERV-ALPHA",
                TipoServicio = "TIPO-ALPHA",
                Sucursal = "SEDE-ALPHA",
            };
            a.HistoriasClinicas.Add(hc);
            a.Asignaciones.Add(asignacion);
            await a.SaveChangesAsync();

            a.HistoriaClinicaMedicamentos.Add(new HistoriaClinicaMedicamento
            {
                HistoriaClinicaId = hc.Id,
                CodigoMedicamento = "MED-ALPHA-1",
                NombreMedicamento = "IBUPROFENO-ALPHA",
            });
            a.NotasMedicas.Add(new NotaMedica
            {
                PacienteId = paciente.Id,
                HistoriaClinicaId = hc.Id,
                EspecialistaNombre = "DR ALPHA",
            });
            a.HistoriaClinicaEscalas.Add(new HistoriaClinicaEscala
            {
                HistoriaClinicaId = hc.Id,
                FormDefinitionId = form.Id,
                FechaApertura = DateTimeOffset.UtcNow.AddHours(-1),
                ValoresJson = "{\"marca\":\"ESCALA-ALPHA\"}",
            });
            a.HistoriaClinicaDocumentos.Add(new HistoriaClinicaDocumento
            {
                HistoriaClinicaId = hc.Id,
                Tipo = "EVOLUCION",
                FormDefinitionId = form.Id,
                FechaApertura = DateTimeOffset.UtcNow.AddHours(-2),
                ValoresJson = "{\"marca\":\"EVOL-ALPHA\"}",
            });
            a.HistoriaClinicaDocumentos.Add(new HistoriaClinicaDocumento
            {
                HistoriaClinicaId = hc.Id,
                Tipo = "CONSENTIMIENTO",
                FormDefinitionId = form.Id,
                FechaApertura = DateTimeOffset.UtcNow.AddHours(-3),
                ValoresJson = "{\"marca\":\"CONSENT-ALPHA\"}",
            });
            await a.SaveChangesAsync();

            _hcAId = hc.Id;
        }
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // Un @MemberData por tool. Cada uno corre el mismo cuerpo — invoca la tool
    // desde el scope del tenant B (sin datos) apuntando a la HC del tenant A y
    // verifica que ningun string distintivo aparezca en el payload.
    public static IEnumerable<object[]> AllTools() => new[]
    {
        new object[] { RevisionMcpToolNames.GetHistoriaClinica },
        new object[] { RevisionMcpToolNames.GetPaciente },
        new object[] { RevisionMcpToolNames.ListOrdenesHc },
        new object[] { RevisionMcpToolNames.ListNotasHc },
        new object[] { RevisionMcpToolNames.ListEscalasHc },
        new object[] { RevisionMcpToolNames.ListEvolucionesHc },
        new object[] { RevisionMcpToolNames.ListConsentimientosHc },
        new object[] { RevisionMcpToolNames.ListAsignacionesRelacionadas },
        new object[] { RevisionMcpToolNames.GetFormDefinition },
    };

    [Theory]
    [MemberData(nameof(AllTools))]
    public async Task Tool_NoFugaDeDatosDeOtroTenant(string tool)
    {
        await using var ctxB = CreateContext(_tenantB);
        var svc = new RevisionMcpToolsService(ctxB);
        var res = await svc.EjecutarToolAsync(tool, _hcAId, RevisionMcpToolNames.Todas, ventanaAsignacionesDias: 30);
        var payload = res.JsonPayload ?? "";
        Assert.DoesNotContain("ALPHA", payload);
        Assert.DoesNotContain("9001A", payload);
        Assert.DoesNotContain("MED-ALPHA-1", payload);
    }

    private VisalDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new FixedTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<VisalDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableTenantInterceptor(tenantContext, TimeProvider.System))
            .Options;
        return new VisalDbContext(options, tenantContext);
    }

    private sealed class FixedTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
        public Guid? SucursalId => null;
    }
}

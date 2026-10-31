using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Application.Tenancy.Forms;
using Visal.Domain.Entities;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Tests de la denormalizacion Barthel: volcar la ultima escala Barthel cerrada al paciente
/// (puntaje, clasificacion, snapshot, fecha), con regla "gana la mas reciente" y el recalculo
/// masivo por tenant. Ignora formatos que no son Barthel y escalas no cerradas.
/// </summary>
public sealed class BarthelDenormalizadorTests
{
    private static readonly Guid Tenant = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private static VisalDbContext NewCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });
    }

    private static Guid SembrarFormatoBarthel(VisalDbContext db)
    {
        var id = Guid.NewGuid();
        db.FormDefinitions.Add(new FormDefinition
        {
            Id = id, TenantId = Tenant, Codigo = "PP-FO-46",
            Nombre = "PP-FO-46 ESCALA BARTHEL", Tipo = "ESCALAS", Version = "1",
            SchemaJson = "{}", Activo = true,
        });
        return id;
    }

    private static Guid SembrarPacienteConHc(VisalDbContext db, out Guid hcId, Guid formatoHc)
    {
        var pac = new Paciente
        {
            Id = Guid.NewGuid(), TenantId = Tenant,
            NumeroDocumento = "123", TipoDocumento = "CC", NombreCompleto = "PACIENTE PRUEBA",
        };
        db.Pacientes.Add(pac);
        hcId = Guid.NewGuid();
        db.HistoriasClinicas.Add(new HistoriaClinica
        {
            Id = hcId, TenantId = Tenant, PacienteId = pac.Id, FormDefinitionId = formatoHc,
        });
        return pac.Id;
    }

    private static Guid SembrarEscalaCerrada(
        VisalDbContext db, Guid hcId, Guid formatoId, string valoresJson, DateTimeOffset cierre)
    {
        var id = Guid.NewGuid();
        db.HistoriaClinicaEscalas.Add(new HistoriaClinicaEscala
        {
            Id = id, TenantId = Tenant, HistoriaClinicaId = hcId, FormDefinitionId = formatoId,
            ValoresJson = valoresJson, Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = cierre.AddHours(-1), FechaCierre = cierre,
        });
        return id;
    }

    [Fact]
    public async Task AplicarDesdeEscala_vuelca_total_clasificacion_snapshot_y_fecha()
    {
        await using var db = NewCtx(nameof(AplicarDesdeEscala_vuelca_total_clasificacion_snapshot_y_fecha));
        var barthel = SembrarFormatoBarthel(db);
        var pacId = SembrarPacienteConHc(db, out var hcId, barthel);
        var cierre = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);
        var escalaId = SembrarEscalaCerrada(db, hcId, barthel,
            "{\"barthel_total\":\"85\",\"barthel_clasificacion\":\"Dependencia leve\",\"barthel_comida\":\"10\"}",
            cierre);
        await db.SaveChangesAsync();

        var svc = new BarthelDenormalizador(db);
        var aplico = await svc.AplicarDesdeEscalaAsync(escalaId);

        Assert.True(aplico);
        var pac = await db.Pacientes.SingleAsync(p => p.Id == pacId);
        Assert.Equal(85, pac.BarthelTotal);
        Assert.Equal("Dependencia leve", pac.BarthelClasificacion);
        Assert.Equal(cierre, pac.BarthelActualizadoEn);
        Assert.Contains("barthel_comida", pac.BarthelDatosJson);
    }

    [Fact]
    public async Task AplicarDesdeEscala_no_pisa_con_una_escala_mas_antigua()
    {
        await using var db = NewCtx(nameof(AplicarDesdeEscala_no_pisa_con_una_escala_mas_antigua));
        var barthel = SembrarFormatoBarthel(db);
        var pacId = SembrarPacienteConHc(db, out var hcId, barthel);
        var vieja = SembrarEscalaCerrada(db, hcId, barthel, "{\"barthel_total\":\"40\"}",
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        var nueva = SembrarEscalaCerrada(db, hcId, barthel, "{\"barthel_total\":\"90\"}",
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        var svc = new BarthelDenormalizador(db);

        Assert.True(await svc.AplicarDesdeEscalaAsync(nueva));
        Assert.False(await svc.AplicarDesdeEscalaAsync(vieja)); // mas antigua: no pisa

        var pac = await db.Pacientes.SingleAsync(p => p.Id == pacId);
        Assert.Equal(90, pac.BarthelTotal);
    }

    [Fact]
    public async Task AplicarDesdeEscala_ignora_formato_que_no_es_barthel()
    {
        await using var db = NewCtx(nameof(AplicarDesdeEscala_ignora_formato_que_no_es_barthel));
        var otro = Guid.NewGuid();
        db.FormDefinitions.Add(new FormDefinition
        {
            Id = otro, TenantId = Tenant, Codigo = "PP-FO-99", Nombre = "ESCALA NORTON",
            Tipo = "ESCALAS", Version = "1", SchemaJson = "{}", Activo = true,
        });
        var pacId = SembrarPacienteConHc(db, out var hcId, otro);
        var escalaId = SembrarEscalaCerrada(db, hcId, otro, "{\"barthel_total\":\"50\"}",
            new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var svc = new BarthelDenormalizador(db);
        Assert.False(await svc.AplicarDesdeEscalaAsync(escalaId));
        var pac = await db.Pacientes.SingleAsync(p => p.Id == pacId);
        Assert.Null(pac.BarthelTotal);
    }

    [Fact]
    public async Task RecalcularTodos_toma_la_ultima_escala_cerrada_por_paciente()
    {
        await using var db = NewCtx(nameof(RecalcularTodos_toma_la_ultima_escala_cerrada_por_paciente));
        var barthel = SembrarFormatoBarthel(db);
        var pacId = SembrarPacienteConHc(db, out var hcId, barthel);
        SembrarEscalaCerrada(db, hcId, barthel, "{\"barthel_total\":\"30\"}",
            new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
        SembrarEscalaCerrada(db, hcId, barthel, "{\"barthel_total\":\"75\",\"barthel_clasificacion\":\"Moderada\"}",
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var svc = new BarthelDenormalizador(db);
        var n = await svc.RecalcularTodosAsync();

        Assert.Equal(1, n);
        var pac = await db.Pacientes.SingleAsync(p => p.Id == pacId);
        Assert.Equal(75, pac.BarthelTotal);
        Assert.Equal("Moderada", pac.BarthelClasificacion);
    }
}

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
/// La tool get_form_definition debe entregar el CATALOGO DE CAMPOS que el formato define, ya
/// cruzado con los valores de la HC (requerido + estado lleno/vacio), y NADA que el formato no
/// defina. Asi el agente REVISOR CLINICO IA audita la completitud SOLO contra los campos del
/// formato (p.ej. no exige signos vitales si el formato no tiene ese campo).
/// </summary>
public sealed class RevisionFormCatalogoTests
{
    private static readonly Guid Tenant = Guid.Parse("ccccccc3-0000-0000-0000-000000000003");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    [Fact]
    public async Task GetFormDefinition_DevuelveCatalogoDeCampos_SinInventar()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });

        // Formato con 2 campos requeridos (motivo, plan), 1 opcional (obs) dentro de una seccion,
        // y un bloque de texto estatico (que NO es campo de captura). SIN campo de signos vitales.
        const string schema = """
        {
          "children": [
            { "type": "field", "name": "motivo", "label": "Motivo de consulta", "fieldType": "textarea", "required": true },
            { "type": "section", "label": "Datos", "children": [
                { "type": "field", "name": "obs", "label": "Observaciones", "fieldType": "text", "required": false },
                { "type": "text", "textStyle": "heading", "content": "Seccion informativa" }
            ] },
            { "type": "field", "name": "plan", "label": "Plan de manejo", "fieldType": "textarea", "required": true }
          ]
        }
        """;
        var form = new FormDefinition { TenantId = Tenant, Codigo = "PP-FO-99", Nombre = "Nota simple", Tipo = "HC", SchemaJson = schema };
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "PAC", TipoDocumento = "CC", NumeroDocumento = "1" };
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = paciente.Id, FormDefinitionId = form.Id,
            Estado = HistoriaClinicaEstado.Cerrada,
            FechaApertura = DateTimeOffset.UtcNow,
            // motivo y plan llenos; obs vacio.
            ValoresJson = "{\"motivo\":\"cefalea\",\"obs\":\"\",\"plan\":\"analgesico\"}",
        };
        db.FormDefinitions.Add(form);
        db.Pacientes.Add(paciente);
        db.HistoriasClinicas.Add(hc);
        await db.SaveChangesAsync();

        var svc = new RevisionMcpToolsService(db);
        var r = await svc.EjecutarToolAsync(RevisionMcpToolNames.GetFormDefinition, hc.Id, RevisionMcpToolNames.Todas, 30);

        Assert.True(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.JsonPayload));
        // El catalogo NO inventa campos que el formato no define.
        Assert.DoesNotContain("signos vitales", r.JsonPayload!, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(r.JsonPayload!);
        var root = doc.RootElement;
        Assert.Equal("PP-FO-99", root.GetProperty("formato").GetProperty("Codigo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("nota").GetString()));

        var resumen = root.GetProperty("resumen");
        Assert.Equal(3, resumen.GetProperty("total").GetInt32());        // motivo, obs, plan (el bloque text NO cuenta)
        Assert.Equal(2, resumen.GetProperty("requeridos").GetInt32());   // motivo, plan
        Assert.Equal(0, resumen.GetProperty("requeridos_vacios").GetInt32());
        Assert.Equal(2, resumen.GetProperty("llenos").GetInt32());       // motivo, plan
        Assert.Equal(1, resumen.GetProperty("vacios").GetInt32());       // obs

        var campos = root.GetProperty("campos").EnumerateArray().ToList();
        Assert.Equal(3, campos.Count);
        var obs = campos.Single(c => c.GetProperty("Campo").GetString() == "Observaciones");
        Assert.False(obs.GetProperty("Requerido").GetBoolean());
        Assert.Equal("vacio", obs.GetProperty("Estado").GetString());
        var motivo = campos.Single(c => c.GetProperty("Campo").GetString() == "Motivo de consulta");
        Assert.True(motivo.GetProperty("Requerido").GetBoolean());
        Assert.Equal("lleno", motivo.GetProperty("Estado").GetString());
    }

    /// <summary>
    /// El catalogo debe separar las FECHAS DE ATENCION: la del HEADER ("Ciudad y Fecha") y la(s)
    /// del CUERPO (campos marcados isFechaAtencion), para que el agente las contraste. Aqui difieren
    /// (header 30/10 vs cuerpo 05/10) -> el agente lo veria como posible incongruencia temporal.
    /// </summary>
    [Fact]
    public async Task GetFormDefinition_SeparaFechasDeAtencion_HeaderVsCuerpo()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });

        const string schema = """
        {
          "header": { "campos": [ { "id": "cf1", "label": "Ciudad y Fecha", "tipo": "fechaHora" } ] },
          "children": [
            { "type": "field", "name": "fatt", "label": "Fecha de atencion", "fieldType": "date", "required": false, "isFechaAtencion": true },
            { "type": "field", "name": "motivo", "label": "Motivo", "fieldType": "text", "required": true }
          ]
        }
        """;
        var form = new FormDefinition { TenantId = Tenant, Codigo = "PP-FO-85_O", Nombre = "Evolucion", Tipo = "HC", SchemaJson = schema };
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "PAC", TipoDocumento = "CC", NumeroDocumento = "2" };
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = paciente.Id, FormDefinitionId = form.Id,
            Estado = HistoriaClinicaEstado.Cerrada, FechaApertura = DateTimeOffset.UtcNow,
            ValoresJson = "{\"hdr:cf1\":\"30/10/2026 08:00\",\"fatt\":\"2026-10-05\",\"motivo\":\"x\"}",
        };
        db.FormDefinitions.Add(form);
        db.Pacientes.Add(paciente);
        db.HistoriasClinicas.Add(hc);
        await db.SaveChangesAsync();

        var svc = new RevisionMcpToolsService(db);
        var r = await svc.EjecutarToolAsync(RevisionMcpToolNames.GetFormDefinition, hc.Id, RevisionMcpToolNames.Todas, 30);
        Assert.True(r.Ok);

        using var doc = JsonDocument.Parse(r.JsonPayload!);
        var fechas = doc.RootElement.GetProperty("fechas_atencion");

        var header = fechas.GetProperty("header").EnumerateArray().ToList();
        Assert.Single(header);
        Assert.Equal("Ciudad y Fecha", header[0].GetProperty("Campo").GetString());
        Assert.Equal("30/10/2026 08:00", header[0].GetProperty("Valor").GetString());

        var cuerpo = fechas.GetProperty("cuerpo").EnumerateArray().ToList();
        Assert.Single(cuerpo);
        Assert.Equal("Fecha de atencion", cuerpo[0].GetProperty("Campo").GetString());
        Assert.Equal("2026-10-05", cuerpo[0].GetProperty("Valor").GetString());
    }

    /// <summary>
    /// get_historia_clinica debe entregar las fechas con nombres auto-explicativos: la del servicio
    /// como 'fecha_atencion_servicio' (dentro de 'fechas') y apertura/cierre marcadas como
    /// administrativas, para que el agente NO use el cierre como fecha del servicio.
    /// </summary>
    [Fact]
    public async Task GetHistoriaClinica_FechasAutoExplicativas()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new VisalDbContext(opts, new FakeTenantContext { TenantId = Tenant });

        var form = new FormDefinition { TenantId = Tenant, Codigo = "PP-FO-85_O", Nombre = "Evol", Tipo = "HC", SchemaJson = "{}" };
        var paciente = new Paciente { TenantId = Tenant, NombreCompleto = "PAC", TipoDocumento = "CC", NumeroDocumento = "3" };
        var hc = new HistoriaClinica
        {
            TenantId = Tenant, PacienteId = paciente.Id, FormDefinitionId = form.Id,
            Estado = HistoriaClinicaEstado.Cerrada, ValoresJson = "{}",
            FechaAtencion = new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero),
            FechaApertura = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            FechaCierre = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
        };
        db.FormDefinitions.Add(form);
        db.Pacientes.Add(paciente);
        db.HistoriasClinicas.Add(hc);
        await db.SaveChangesAsync();

        var svc = new RevisionMcpToolsService(db);
        var r = await svc.EjecutarToolAsync(RevisionMcpToolNames.GetHistoriaClinica, hc.Id, RevisionMcpToolNames.Todas, 30);
        Assert.True(r.Ok);

        using var doc = JsonDocument.Parse(r.JsonPayload!);
        var root = doc.RootElement;
        // No hay fechas hermanas crudas que confundan al modelo.
        Assert.False(root.TryGetProperty("FechaAtencion", out _));
        Assert.False(root.TryGetProperty("FechaCierre", out _));

        var fechas = root.GetProperty("fechas");
        Assert.Contains("2026-09-30", fechas.GetProperty("fecha_atencion_servicio").GetString());
        Assert.Contains("2026-10-20", fechas.GetProperty("cierre_registro_admin").GetString());
        Assert.Contains("2026-10-01", fechas.GetProperty("apertura_registro_admin").GetString());
    }
}

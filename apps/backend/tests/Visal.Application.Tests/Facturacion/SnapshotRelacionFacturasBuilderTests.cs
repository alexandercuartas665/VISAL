using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Visal.Application.Common;
using Visal.Application.Facturacion.Builders;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

/// <summary>
/// Tests del builder concreto de Relacion de Facturas.
///
/// Cada test siembra un mini-mundo (aseguradora, contrato, servicio,
/// paciente, asignacion, turno, HC, nota) en EF InMemory y verifica
/// una regla del spec §2/§7 sobre lo que el builder emite.
/// </summary>
public sealed class SnapshotRelacionFacturasBuilderTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private static (VisalDbContext ctx, FakeTenantContext tenant) Db()
    {
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenant = new FakeTenantContext { TenantId = TenantId, UserId = TenantId };
        return (new VisalDbContext(opts, tenant), tenant);
    }

    /// <summary>
    /// Contexto reutilizable: siembra un mini-mundo con 1 aseguradora, 1 contrato con 1 servicio
    /// suelto + 1 paquete de 2 CUPS, 1 paciente, 1 sucursal y 1 profesional. Devuelve los ids que
    /// los tests usan para armar asignaciones y notas de manera declarativa.
    /// </summary>
    private sealed record Seed(
        VisalDbContext Ctx, Guid Aseguradora, Guid Sucursal, Guid Profesional,
        Guid Paciente, Guid Contrato, Guid ServicioSuelto, Guid Paquete,
        Guid PaqueteServicioA, Guid PaqueteServicioB);

    private static async Task<Seed> SembrarMundoAsync()
    {
        var (ctx, _) = Db();
        var aseg = new Aseguradora
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "ASMET", Tipo = "EPS", Nombre = "ASMET SALUD",
            Nit = "900226715", Regimen = "SUBSIDIADO",
            CorreoFacturacion = "facturacion.salud@asmetsalud.com"
        };
        var suc = new Sucursal
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "CALI", Nombre = "SANTIAGO DE CALI", Activo = true,
            CodigoHabilitacion = "730010353101"
        };
        var pais = new Pais { Id = Guid.NewGuid(), Codigo = "CO", Nombre = "COLOMBIA", Activo = true };
        var dep = new Departamento { Id = Guid.NewGuid(), Nombre = "VALLE DEL CAUCA", PaisId = pais.Id };
        var mun = new Municipio { Id = Guid.NewGuid(), Nombre = "CALI", DepartamentoId = dep.Id };
        var paciente = new Paciente
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            TipoDocumento = "CC", NumeroDocumento = "2245956",
            PrimerNombre = "JOSE", SegundoNombre = "REINEL",
            PrimerApellido = "HENAO", SegundoApellido = "NAVARRO",
            NombreCompleto = "JOSE REINEL HENAO NAVARRO",
            FechaNacimiento = new DateOnly(1985, 4, 12),
            Sexo = "M", Regimen = "SUBSIDIADO",
            Direccion = "CRA 4 # 18N - 46", Telefono = "8274242",
            Cie10Codigo = "Z243",
            DepartamentoId = dep.Id, MunicipioId = mun.Id, PaisResidenciaId = pais.Id
        };
        var prof = new Profesional
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            TipoDocumento = "CC", NumeroDocumento = "1110089289",
            PrimerNombre = "DANIELA", PrimerApellido = "RIOS", SegundoApellido = "RIOS",
            NombreCompleto = "DANIELA RIOS RIOS"
        };
        var contrato = new ContratoAseguradora
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            AseguradoraId = aseg.Id, CodigoContrato = "TOL-004-26-P",
            Estado = "ACTIVO"
        };
        var servicio = new ServicioContrato
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            ContratoId = contrato.Id,
            CodigoServicio = "890114", CodigoInterno = "INT-890114",
            Descripcion = "ATENCION DOMICILIARIA POR PROMOTOR", Tarifa = 145000m,
            ValorTotal = 145000m,
            Finalidad = "17", CausaExterna = "38", ModalidadAtencion = "02",
            ViaIngreso = "03", GrupoServicios = "07", Servicios = "312"
        };
        var paquete = new Paquete
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "PAQ-DOM-STD", Nombre = "PAQUETE DOMICILIARIO ESTANDAR",
            Precio = 500000m, Activo = true
        };
        var pServA = new PaqueteServicio
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PaqueteId = paquete.Id, Codigo = "890201", Cantidad = 1
        };
        var pServB = new PaqueteServicio
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PaqueteId = paquete.Id, Codigo = "993504", Cantidad = 1
        };
        paquete.CupsRepresentativoServicioId = pServA.Id;
        var catA = new CatalogoServicioReferencia
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "890114", Nombre = "ATENCION (VISITA) DOMICILIARIA (seguimiento)",
            Tipo = TipoCatalogoServicio.ServicioGeneral, Activo = true
        };
        var catB = new CatalogoServicioReferencia
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "890201", Nombre = "PAQUETE ATENCION DOMICILIARIA",
            Tipo = TipoCatalogoServicio.ServicioGeneral, Activo = true
        };

        ctx.Aseguradoras.Add(aseg);
        ctx.Sucursales.Add(suc);
        ctx.Paises.Add(pais);
        ctx.Departamentos.Add(dep);
        ctx.Municipios.Add(mun);
        ctx.Pacientes.Add(paciente);
        ctx.Profesionales.Add(prof);
        ctx.ContratosAseguradora.Add(contrato);
        ctx.ServiciosContrato.Add(servicio);
        ctx.Paquetes.Add(paquete);
        ctx.PaqueteServicios.Add(pServA);
        ctx.PaqueteServicios.Add(pServB);
        ctx.CatalogosServicioReferencia.Add(catA);
        ctx.CatalogosServicioReferencia.Add(catB);
        await ctx.SaveChangesAsync();

        return new Seed(ctx, aseg.Id, suc.Id, prof.Id, paciente.Id,
            contrato.Id, servicio.Id, paquete.Id, pServA.Id, pServB.Id);
    }

    /// <summary>Crea una asignacion + turno + HC + nota definitiva del paciente.</summary>
    private static async Task<Guid> AgregarAtencionAsync(
        Seed seed, string codigoContrato, string servicioId, string sucursalCodigo,
        DateOnly fechaTurno, DateOnly fechaNota, TimeOnly? horaNota = null,
        string? tipoPago = null, decimal? valorPago = null,
        string? numeroAutorizacion = "AP-12345",
        Guid? paqueteInstanciaId = null, string? paqueteCodigo = null,
        decimal? paqueteValorPactado = null)
    {
        var asig = new Asignacion
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            LoteId = Guid.NewGuid(),
            PacienteId = seed.Paciente,
            Sucursal = sucursalCodigo,
            ServicioId = servicioId,
            NombreServicio = "n/a",
            TipoServicio = "CONSULTA",
            Cantidad = 1,
            ContratoCodigo = codigoContrato,
            CodigoAutorizacion = numeroAutorizacion,
            FechaInicio = fechaTurno,
            MesVigencia = (short)fechaTurno.Month,
            TipoPago = tipoPago,
            ValorPagoReal = valorPago,
            Estado = AsignacionEstado.Asignado,
            PaqueteInstanciaId = paqueteInstanciaId,
            PaqueteCodigo = paqueteCodigo,
            PaqueteValorPactado = paqueteValorPactado
        };
        var turno = new AsignacionTurno
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            AsignacionId = asig.Id, ProfesionalId = seed.Profesional,
            Cantidad = 1, FechaInicio = fechaTurno,
            PaqueteInstanciaId = asig.PaqueteInstanciaId,
            PaqueteCodigo = asig.PaqueteCodigo,
            PaqueteValorPactado = asig.PaqueteValorPactado
        };
        var hc = new HistoriaClinica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PacienteId = seed.Paciente, ProfesionalId = seed.Profesional,
            FormDefinitionId = Guid.Empty,
            ValoresJson = "{}",
            FechaApertura = DateTimeOffset.UtcNow,
            Estado = HistoriaClinicaEstado.Abierta
        };
        var nota = new NotaMedica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            HistoriaClinicaId = hc.Id, PacienteId = seed.Paciente,
            AsignacionTurnoId = turno.Id, SessionNo = 1,
            FechaNota = fechaNota, HoraNota = horaNota,
            Contenido = "atencion prestada",
            Estado = NotaMedicaEstado.Definitivo,
            EspecialistaNombre = "DANIELA RIOS"
        };
        seed.Ctx.Asignaciones.Add(asig);
        seed.Ctx.AsignacionTurnos.Add(turno);
        seed.Ctx.HistoriasClinicas.Add(hc);
        seed.Ctx.NotasMedicas.Add(nota);
        await seed.Ctx.SaveChangesAsync();
        return asig.Id;
    }

    private static string FiltrosJson(Guid aseguradoraId, DateOnly desde, DateOnly hasta, IEnumerable<Guid>? sucursales = null)
    {
        var payload = new
        {
            aseguradoraId = aseguradoraId.ToString(),
            sucursalIds = (sucursales ?? Array.Empty<Guid>()).Select(x => x.ToString()).ToArray(),
            fechaInicio = desde.ToString("yyyy-MM-dd"),
            fechaFin = hasta.ToString("yyyy-MM-dd")
        };
        return JsonSerializer.Serialize(payload);
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> EjecutarBuilderAsync(
        VisalDbContext ctx, string filtrosJson)
    {
        var builder = new SnapshotRelacionFacturasBuilder(ctx);
        var filas = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var f in builder.ConstruirAsync(filtrosJson))
        {
            filas.Add(f);
        }
        return filas;
    }

    // ---------- Tests ----------

    [Fact]
    public async Task Columnas_Tiene41ExactasConTildesRotas()
    {
        // El template EPS es sagrado — los 41 headers no se re-formatean.
        var (ctx, _) = Db();
        var builder = new SnapshotRelacionFacturasBuilder(ctx);
        Assert.Equal(41, builder.Columnas.Count);
        Assert.Equal("Consecutivo Factura", builder.Columnas[0]);
        Assert.Equal("codigo habilitacion ", builder.Columnas[3]); // espacio final del template
        Assert.Equal("Identificación", builder.Columnas[8]);        // con tilde
        Assert.Equal("Descripción del procedimiento (Factura)", builder.Columnas[20]);
        Assert.Equal("Vr Cuota Moderadora ", builder.Columnas[22]); // espacio final
        Assert.Equal("Diagnóstico", builder.Columnas[25]);
        Assert.Equal("Modalidad Atención", builder.Columnas[31]);
        Assert.Equal("Vía de Ingreso", builder.Columnas[32]);
        Assert.Equal("Dirección", builder.Columnas[38]);
        Assert.Equal("Correo electrónico", builder.Columnas[40]);
    }

    [Fact]
    public async Task Construir_ServicioSuelto_MapaTodosLosCamposDelSpec()
    {
        // Verificacion punto por punto de las columnas §4 con un servicio suelto.
        var seed = await SembrarMundoAsync();
        var asigId = await AgregarAtencionAsync(seed, "TOL-004-26-P",
            seed.ServicioSuelto.ToString(), "CALI",
            fechaTurno: new DateOnly(2026, 6, 15),
            fechaNota: new DateOnly(2026, 6, 15),
            horaNota: new TimeOnly(14, 35, 22),
            tipoPago: "CUOTA", valorPago: 3800m,
            numeroAutorizacion: "AP-2245956");

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        var f = Assert.Single(filas);
        Assert.Null(f["Consecutivo Factura"]);
        Assert.Null(f["Orden"]);
        Assert.Equal("TOL-004-26-P", f["Contrato"]);
        Assert.Equal("730010353101", f["codigo habilitacion "]);
        Assert.Equal("SUBSIDIADO", f["Regimen"]);
        Assert.Null(f["Archivo json"]);
        Assert.Equal("AP-2245956", f["Autorizacion"]);
        Assert.Equal("CC", f["Tipo_Id"]);
        Assert.Equal("2245956", f["Identificación"]);
        Assert.Equal("HENAO", f["Primer Apellido"]);
        Assert.Equal("NAVARRO", f["Segundo Apellido"]);
        Assert.Equal("JOSE", f["Primer Nombre"]);
        Assert.Equal("REINEL", f["Segundo Nombre"]);
        Assert.Equal("1985-04-12", f["Fecha de Nacimiento"]);
        Assert.Equal("M", f["Sexo"]);
        Assert.Equal("2026-06-15", f["Fecha suministro de tecnologia"]);
        Assert.Equal("14:35:22", f["Hora"]);
        Assert.Equal("890114", f["CUPS"]);
        Assert.Equal("INT-890114", f["Codigo Externo (Factura)"]);
        Assert.Equal(1, f["Cantidad"]);
        Assert.Equal("ATENCION (VISITA) DOMICILIARIA (seguimiento)", f["Descripción del procedimiento (Factura)"]);
        Assert.Equal(145000m, f["Valor Unitario"]);
        Assert.Equal(3800m, f["Vr Cuota Moderadora "]);
        Assert.Null(f["Copago o Pago Compartido"]);
        Assert.Equal(145000m, f["Valor Total"]);
        Assert.Equal("Z243", f["Diagnóstico"]);
        Assert.Equal("CC", f["TipoDocProfesional"]);
        Assert.Equal("1110089289", f["DocumentoProf"]);
        Assert.Equal("DANIELA RIOS RIOS", f["NomProf"]);
        Assert.Equal("17", f["Finalidad"]);
        Assert.Equal("38", f["Causa Externa"]);
        Assert.Equal("02", f["Modalidad Atención"]);
        Assert.Equal("03", f["Vía de Ingreso"]);
        Assert.Equal("07", f["Grupo Servicios"]);
        Assert.Equal("312", f["Servicios"]);
        Assert.Equal("COLOMBIA", f["Nacionalidad"]);
        Assert.Equal("VALLE DEL CAUCA", f["Departamento"]);
        Assert.Equal("CALI", f["Municipio"]);
        Assert.Equal("CRA 4 # 18N - 46", f["Dirección"]);
        Assert.Equal("8274242", f["Telefono"]);
        Assert.Equal("facturacion.salud@asmetsalud.com", f["Correo electrónico"]);
        _ = asigId; // usado dentro
    }

    [Fact]
    public async Task Construir_Copago_DejaCuotaEnNullYUsaCopago()
    {
        // Spec §7.3: por contrato es CUOTA o COPAGO, nunca los dos > 0.
        var seed = await SembrarMundoAsync();
        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "CALI",
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10),
            tipoPago: "COPAGO", valorPago: 12000m);

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        var f = Assert.Single(filas);
        Assert.Null(f["Vr Cuota Moderadora "]);
        Assert.Equal(12000m, f["Copago o Pago Compartido"]);
    }

    [Fact]
    public async Task Construir_Paquete_UnaFilaConCupsRepresentativoYPrecioDelPaquete()
    {
        // Spec §5.1/§7.4: un paquete atendido = 1 sola fila, con CUPS del representativo
        // y Valor Unitario = Paquete.Precio. Otros CUPS del paquete NO generan fila.
        var seed = await SembrarMundoAsync();
        var loteId = Guid.NewGuid();
        // 2 asignaciones del mismo paquete, pero SOLO la ancla trae PaqueteValorPactado.
        await AgregarAtencionAsync(seed, "TOL-004-26-P", "890201", "CALI",
            new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 5),
            paqueteInstanciaId: loteId, paqueteCodigo: "PAQ-DOM-STD",
            paqueteValorPactado: 500000m);
        await AgregarAtencionAsync(seed, "TOL-004-26-P", "993504", "CALI",
            new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6),
            paqueteInstanciaId: loteId, paqueteCodigo: "PAQ-DOM-STD",
            paqueteValorPactado: null);

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        var f = Assert.Single(filas);
        // El CUPS es el del PaqueteServicio marcado como representativo (890201 en el seed).
        Assert.Equal("890201", f["CUPS"]);
        Assert.Equal("PAQUETE ATENCION DOMICILIARIA", f["Descripción del procedimiento (Factura)"]);
        Assert.Equal(500000m, f["Valor Unitario"]);
        Assert.Equal(1, f["Cantidad"]);
    }

    [Fact]
    public async Task Construir_FiltroPorRangoDeFechas_ExcluyeNotasFuera()
    {
        var seed = await SembrarMundoAsync();
        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "CALI",
            new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 30)); // fuera del rango junio
        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "CALI",
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 15)); // dentro

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        Assert.Single(filas);
    }

    [Fact]
    public async Task Construir_FiltroPorAseguradora_ExcluyeOtrasEPS()
    {
        // Spec §5.8: 1 snapshot = 1 aseguradora. Notas de otra EPS no aparecen.
        var seed = await SembrarMundoAsync();
        // Aseguradora + contrato + servicio de OTRA EPS.
        var otraAseg = new Aseguradora
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "SANITAS", Tipo = "EPS", Nombre = "SANITAS", Nit = "800251440"
        };
        var otroContrato = new ContratoAseguradora
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            AseguradoraId = otraAseg.Id, CodigoContrato = "SAN-001", Estado = "ACTIVO"
        };
        var otroServ = new ServicioContrato
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            ContratoId = otroContrato.Id, CodigoServicio = "990001", Tarifa = 90000m
        };
        seed.Ctx.Aseguradoras.Add(otraAseg);
        seed.Ctx.ContratosAseguradora.Add(otroContrato);
        seed.Ctx.ServiciosContrato.Add(otroServ);
        await seed.Ctx.SaveChangesAsync();

        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "CALI",
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        await AgregarAtencionAsync(seed, "SAN-001", otroServ.Id.ToString(), "CALI",
            new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 11));

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        Assert.Single(filas);
    }

    [Fact]
    public async Task Construir_FiltroPorSucursal_ExcluyeSedesNoSeleccionadas()
    {
        var seed = await SembrarMundoAsync();
        var otraSuc = new Sucursal
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            Codigo = "PASTO", Nombre = "PASTO", Activo = true,
            CodigoHabilitacion = "520010000001"
        };
        seed.Ctx.Sucursales.Add(otraSuc);
        await seed.Ctx.SaveChangesAsync();

        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "CALI",
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));
        await AgregarAtencionAsync(seed, "TOL-004-26-P", seed.ServicioSuelto.ToString(), "PASTO",
            new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 11));

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
                sucursales: new[] { seed.Sucursal }));

        var f = Assert.Single(filas);
        Assert.Equal("730010353101", f["codigo habilitacion "]);
    }

    [Fact]
    public async Task Construir_NotaParcial_NoSeIncluye()
    {
        // Solo notas Definitivas se facturan — evita facturar sesiones incompletas.
        var seed = await SembrarMundoAsync();
        // Manual: creamos una asignacion + turno + HC + nota Parcial.
        var asig = new Asignacion
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LoteId = Guid.NewGuid(),
            PacienteId = seed.Paciente, Sucursal = "CALI",
            ServicioId = seed.ServicioSuelto.ToString(), NombreServicio = "n/a",
            TipoServicio = "CONSULTA", Cantidad = 1,
            ContratoCodigo = "TOL-004-26-P",
            FechaInicio = new DateOnly(2026, 6, 10),
            MesVigencia = 6, Estado = AsignacionEstado.Asignado
        };
        var turno = new AsignacionTurno
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            AsignacionId = asig.Id, ProfesionalId = seed.Profesional,
            Cantidad = 1, FechaInicio = asig.FechaInicio
        };
        var hc = new HistoriaClinica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            PacienteId = seed.Paciente, ProfesionalId = seed.Profesional,
            FormDefinitionId = Guid.Empty, ValoresJson = "{}",
            FechaApertura = DateTimeOffset.UtcNow, Estado = HistoriaClinicaEstado.Abierta
        };
        var nota = new NotaMedica
        {
            Id = Guid.NewGuid(), TenantId = TenantId,
            HistoriaClinicaId = hc.Id, PacienteId = seed.Paciente,
            AsignacionTurnoId = turno.Id, FechaNota = new DateOnly(2026, 6, 10),
            Contenido = "avance", Estado = NotaMedicaEstado.Parcial
        };
        seed.Ctx.Asignaciones.Add(asig);
        seed.Ctx.AsignacionTurnos.Add(turno);
        seed.Ctx.HistoriasClinicas.Add(hc);
        seed.Ctx.NotasMedicas.Add(nota);
        await seed.Ctx.SaveChangesAsync();

        var filas = await EjecutarBuilderAsync(seed.Ctx,
            FiltrosJson(seed.Aseguradora, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        Assert.Empty(filas);
    }

    // ---- Fake ITenantContext ----

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }
}

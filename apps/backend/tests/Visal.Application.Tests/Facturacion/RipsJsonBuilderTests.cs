using Visal.Application.Facturacion;
using Visal.Application.Facturacion.Rips;
using Visal.Domain.Enums;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

public class RipsJsonBuilderTests
{
    private static FacturacionSnapshotDetalleDto SampleDetalle() =>
        new(
            Metadata: new FacturacionSnapshotDto(
                Id: Guid.NewGuid(),
                Nombre: "test",
                Tipo: TipoSnapshot.RelacionFacturas,
                Estado: EstadoSnapshot.Vigente,
                FechaEjecucionInicio: DateTimeOffset.UtcNow,
                FechaEjecucionFin: null,
                DuracionMs: null,
                TotalFilas: 0,
                CreadoPor: null,
                ArchivadoPor: null,
                MotivoArchivado: null,
                FechaArchivado: null,
                ErrorMensaje: null,
                AseguradoraId: null,
                AseguradoraNombre: null),
            Columnas: Array.Empty<string>(),
            FiltrosJson: "{}");

    [Fact]
    public void Build_ConSnapshotVacio_EmiteEstructuraCompletaConArraysVacios()
    {
        var builder = new RipsJsonBuilder();
        var payload = builder.Build(SampleDetalle(), Array.Empty<IReadOnlyDictionary<string, object?>>(), "900123456");

        Assert.NotNull(payload);
        Assert.Empty(payload.Usuarios);
        Assert.Empty(payload.Servicios.Consultas);
        Assert.Empty(payload.Servicios.Procedimientos);
        Assert.Empty(payload.Servicios.Urgencias);
        Assert.Empty(payload.Servicios.Hospitalizacion);
        Assert.Empty(payload.Servicios.RecienNacidos);
        Assert.Empty(payload.Servicios.Medicamentos);
        Assert.Empty(payload.Servicios.OtrosServicios);
        Assert.Equal(string.Empty, payload.Transaccion.NumFactura);
    }

    [Fact]
    public void Build_ExtraeNumFacturaDePrimeraFila()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Consecutivo Factura"] = "FE-001" }
        };
        var payload = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        Assert.Equal("FE-001", payload.Transaccion.NumFactura);
    }

    [Fact]
    public void Build_DeduplicaUsuariosPorTipoYNumDoc()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["Tipo_Id"] = "CC", ["Identificación"] = "1111",
                ["Regimen"] = "01", ["Sexo"] = "M", ["Fecha de Nacimiento"] = "1990-01-01"
            },
            new Dictionary<string, object?>
            {
                ["Tipo_Id"] = "CC", ["Identificación"] = "1111", // duplicado — se ignora
                ["Regimen"] = "01", ["Sexo"] = "M"
            },
            new Dictionary<string, object?>
            {
                ["Tipo_Id"] = "TI", ["Identificación"] = "2222",
                ["Regimen"] = "02", ["Sexo"] = "F"
            }
        };
        var payload = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        Assert.Equal(2, payload.Usuarios.Count);
        Assert.Equal(1, payload.Usuarios[0].Consecutivo);
        Assert.Equal(2, payload.Usuarios[1].Consecutivo);
    }

    [Fact]
    public void Build_NormalizaNit_QuitaGuionesYDV()
    {
        var payload = new RipsJsonBuilder().Build(SampleDetalle(), Array.Empty<IReadOnlyDictionary<string, object?>>(), "900.123.456-7");
        Assert.Equal("9001234567", payload.Transaccion.NumDocumentoIdObligado);
    }

    [Fact]
    public void Validate_SinNumFactura_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var payload = builder.Build(SampleDetalle(), Array.Empty<IReadOnlyDictionary<string, object?>>(), "900123456");
        var errores = builder.Validate(payload);
        Assert.Contains(errores, e => e.Contains("Consecutivo Factura"));
    }

    [Fact]
    public void Validate_SinNit_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Consecutivo Factura"] = "FE-1" }
        };
        var payload = builder.Build(SampleDetalle(), filas, "");
        var errores = builder.Validate(payload);
        Assert.Contains(errores, e => e.Contains("NIT del obligado"));
    }

    [Fact]
    public void Validate_ConNumFacturaYNit_NoEmiteErrores()
    {
        var builder = new RipsJsonBuilder();
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Consecutivo Factura"] = "FE-1" }
        };
        var payload = builder.Build(SampleDetalle(), filas, "900123456");
        Assert.Empty(builder.Validate(payload));
    }

    // ==== R3: dispatch AC/AP/AM/AT ====

    [Fact]
    public void Build_DispatchArchivoJson_ClasificaEnSubArrayCorrecto()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            FilaBase("AC", "1111"),
            FilaBase("AP", "2222"),
            FilaBase("AM", "3333"),
            FilaBase("AT", "4444")
        };
        var p = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        Assert.Single(p.Servicios.Consultas);
        Assert.Single(p.Servicios.Procedimientos);
        Assert.Single(p.Servicios.Medicamentos);
        Assert.Single(p.Servicios.OtrosServicios);
    }

    [Fact]
    public void Build_ConsecutivoIndependientePorSubArray()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            FilaBase("AC", "1111"),
            FilaBase("AC", "2222"),
            FilaBase("AP", "3333")
        };
        var p = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        Assert.Equal(1, p.Servicios.Consultas[0].Consecutivo);
        Assert.Equal(2, p.Servicios.Consultas[1].Consecutivo);
        Assert.Equal(1, p.Servicios.Procedimientos[0].Consecutivo);
    }

    [Fact]
    public void Build_ConceptoRecaudo_SinCopagoNiCuota_Emite04Y0()
    {
        var fila = FilaBase("AC", "1111");
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { fila }, "900123456");
        Assert.Equal("04", p.Servicios.Consultas[0].ConceptoRecaudo);
        Assert.Equal(0m, p.Servicios.Consultas[0].VrPagoModerador);
    }

    [Fact]
    public void Build_ConceptoRecaudo_SoloCopago_Emite01()
    {
        var fila = FilaBase("AC", "1111", copago: 5000m);
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { fila }, "900123456");
        Assert.Equal("01", p.Servicios.Consultas[0].ConceptoRecaudo);
        Assert.Equal(5000m, p.Servicios.Consultas[0].VrPagoModerador);
    }

    [Fact]
    public void Build_ConceptoRecaudo_CuotaYCopago_Emite03YSuma()
    {
        var fila = FilaBase("AC", "1111", cuota: 3000m, copago: 5000m);
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { fila }, "900123456");
        Assert.Equal("03", p.Servicios.Consultas[0].ConceptoRecaudo);
        Assert.Equal(8000m, p.Servicios.Consultas[0].VrPagoModerador);
    }

    [Fact]
    public void Build_FechaHora_CombinaFechaSuministroYHora_FormatoManual()
    {
        var fila = FilaBase("AC", "1111");
        var m = new Dictionary<string, object?>(fila)
        {
            ["Fecha suministro de tecnologia"] = new DateOnly(2026, 7, 15),
            ["Hora"] = "8:5"
        };
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)m }, "900123456");
        Assert.Equal("2026-07-15 08:05", p.Servicios.Consultas[0].FechaInicioAtencion);
    }

    [Fact]
    public void Build_LimpiaComillasYSaltosEnNombreMedicamento()
    {
        var m = FilaBaseMutable("AM", "1111");
        m["Descripción del procedimiento (Factura)"] = "ACETAMINOFEN \"500mg\"\r\ntabletas";
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)m }, "900123456");
        var nom = p.Servicios.Medicamentos[0].NomTecnologiaSalud;
        Assert.DoesNotContain("\"", nom);
        Assert.DoesNotContain("\n", nom);
        Assert.DoesNotContain("\r", nom);
    }

    private static Dictionary<string, object?> FilaBaseMutable(string archivo, string numDoc, decimal cuota = 0m, decimal copago = 0m) =>
        new()
        {
            ["Consecutivo Factura"] = "FE-1",
            ["Archivo json"] = archivo,
            ["Tipo_Id"] = "CC",
            ["Identificación"] = numDoc,
            ["Regimen"] = "01",
            ["Sexo"] = "M",
            ["codigo habilitacion "] = "760001234501",
            ["CUPS"] = "890201",
            ["Cantidad"] = 1,
            ["Descripción del procedimiento (Factura)"] = "Consulta general",
            ["Valor Total"] = 45000m,
            ["Vr Cuota Moderadora "] = cuota,
            ["Copago o Pago Compartido"] = copago,
            ["Diagnóstico"] = "I10X",
            ["Modalidad Atención"] = "01",
            ["Grupo Servicios"] = "01",
            ["Servicios"] = "890201"
        };

    private static IReadOnlyDictionary<string, object?> FilaBase(string archivo, string numDoc, decimal cuota = 0m, decimal copago = 0m) =>
        FilaBaseMutable(archivo, numDoc, cuota, copago);

    [Fact]
    public void Build_SinNumeroDocumento_OmiteFila()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Tipo_Id"] = "CC", ["Identificación"] = "" },
            new Dictionary<string, object?> { ["Tipo_Id"] = "CC", ["Identificación"] = "1111" }
        };
        var payload = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        Assert.Single(payload.Usuarios);
    }
}

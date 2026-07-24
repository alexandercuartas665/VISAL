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
    public void Validate_ConNumFacturaYNitYServicio_NoEmiteErrores()
    {
        var builder = new RipsJsonBuilder();
        // R4 exige al menos un servicio + diagnostico + codPrestador en cada consulta;
        // FilaBase(AC, ...) los trae por default.
        var fila = FilaBase("AC", "1111");
        var payload = builder.Build(SampleDetalle(), new[] { fila }, "900123456");
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

    // ==== R4: normalizadores + defaults + validaciones extra ====

    [Theory]
    [InlineData("MASCULINO", "M")]
    [InlineData("M", "M")]
    [InlineData("HOMBRE", "M")]
    [InlineData("FEMENINO", "F")]
    [InlineData("MUJER", "F")]
    [InlineData("F", "F")]
    [InlineData("", "I")]
    [InlineData("Otro", "I")]
    public void NormalizarSexo_MapeaVariantesAlCodigoOficial(string entrada, string esperado)
    {
        Assert.Equal(esperado, RipsJsonBuilder.NormalizarSexo(entrada));
    }

    [Theory]
    [InlineData("CC", "CC")]
    [InlineData("cedula", "CC")]
    [InlineData("Cedula de Ciudadania", "CC")]
    [InlineData("TI", "TI")]
    [InlineData("Tarjeta de Identidad", "TI")]
    [InlineData("PASAPORTE", "PA")]
    [InlineData("registro civil", "RC")]
    public void NormalizarTipoDoc_MapeaTextoLibreACodigo(string entrada, string esperado)
    {
        Assert.Equal(esperado, RipsJsonBuilder.NormalizarTipoDoc(entrada));
    }

    [Theory]
    [InlineData("CONTRIBUTIVO", "01")]
    [InlineData("contributivo", "01")]
    [InlineData("Subsidiado", "02")]
    [InlineData("VINCULADO", "03")]
    [InlineData("Particular", "04")]
    [InlineData("01", "01")]
    [InlineData("99", "99")]
    public void NormalizarRegimen_MapeaTextoLibreACodigo(string entrada, string esperado)
    {
        Assert.Equal(esperado, RipsJsonBuilder.NormalizarRegimen(entrada));
    }

    [Fact]
    public void Build_AplicaNormalizacionAlUsuario()
    {
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["Tipo_Id"] = "cedula",
                ["Identificación"] = "1111",
                ["Regimen"] = "CONTRIBUTIVO",
                ["Sexo"] = "MASCULINO",
                ["Fecha de Nacimiento"] = new DateOnly(1990, 1, 1)
            }
        };
        var p = new RipsJsonBuilder().Build(SampleDetalle(), filas, "900123456");
        var u = p.Usuarios[0];
        Assert.Equal("CC", u.TipoDocumentoIdentificacion);
        Assert.Equal("01", u.TipoUsuario);
        Assert.Equal("M", u.CodSexo);
    }

    [Fact]
    public void Build_ConsultaSinFinalidad_AplicaDefault10()
    {
        var fila = FilaBaseMutable("AC", "1111");
        fila.Remove("Finalidad");
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        Assert.Equal("10", p.Servicios.Consultas[0].FinalidadTecnologiaSalud);
    }

    [Fact]
    public void Build_ConsultaSinCausaExterna_AplicaDefault15()
    {
        var fila = FilaBaseMutable("AC", "1111");
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        Assert.Equal("15", p.Servicios.Consultas[0].CausaMotivoAtencion);
    }

    [Fact]
    public void Validate_SinServicios_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var filas = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Consecutivo Factura"] = "FE-1" }
        };
        var p = builder.Build(SampleDetalle(), filas, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("ningun servicio"));
    }

    [Fact]
    public void Validate_ConsultaSinDiagnostico_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var fila = FilaBaseMutable("AC", "1111");
        fila["Diagnóstico"] = "";
        var p = builder.Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("codDiagnosticoPrincipal"));
    }

    [Fact]
    public void Validate_ConsultaSinCodPrestador_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var fila = FilaBaseMutable("AC", "1111");
        fila["codigo habilitacion "] = "";
        var p = builder.Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("codPrestador"));
    }

    // ==== R6: campos ricos por sub-array ====

    [Theory]
    [InlineData("ACETAMINOFEN 500 mg tabletas", "500 mg", "01")]
    [InlineData("Ibuprofeno 400mg caps", "400 mg", "01")]
    [InlineData("Amoxicilina 250 mg/5 ml suspension", "250 mg", "01")]
    [InlineData("Solucion salina 10 ml ampolla", "10 ml", "02")]
    [InlineData("Vitamina D 1000 UI", "1000 ui", "03")]
    [InlineData("Levotiroxina 500 mcg tab", "500 mcg", "05")]
    [InlineData("Clorhexidina 2%", "2 %", "06")]
    [InlineData("Sin numero", null, null)]
    [InlineData("", null, null)]
    public void ExtraerConcentracionYUnidad_ParseaTextoLibre(string nombre, string? concentracion, string? unidad)
    {
        var (c, u) = RipsJsonBuilder.ExtraerConcentracionYUnidad(nombre);
        Assert.Equal(concentracion, c);
        Assert.Equal(unidad, u);
    }

    [Fact]
    public void Build_Medicamento_DerivaConcentracionDesdeNombre()
    {
        var fila = FilaBaseMutable("AM", "1111");
        fila["Descripción del procedimiento (Factura)"] = "ACETAMINOFEN 500 mg tabletas";
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        var m = p.Servicios.Medicamentos[0];
        Assert.Equal("500 mg", m.ConcentracionMedicamento);
        Assert.Equal("01", m.UnidadMedida);
    }

    [Fact]
    public void Build_Medicamento_PrefiereCodigoExternoSobreCups()
    {
        var fila = FilaBaseMutable("AM", "1111");
        fila["CUPS"] = "890201";
        fila["Codigo Externo (Factura)"] = "19942360-01"; // CUM
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        Assert.Equal("19942360-01", p.Servicios.Medicamentos[0].CodTecnologiaSalud);
    }

    [Fact]
    public void Build_Medicamento_SinCodigoExterno_UsaCupsComoFallback()
    {
        var fila = FilaBaseMutable("AM", "1111");
        fila["CUPS"] = "890201";
        fila["Codigo Externo (Factura)"] = "";
        var p = new RipsJsonBuilder().Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        Assert.Equal("890201", p.Servicios.Medicamentos[0].CodTecnologiaSalud);
    }

    // ==== R5: cuadre financiero ====

    [Fact]
    public void Validate_CopagosSuperanServicios_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        // Un servicio con copago > tarifa: viola manual §4.1.
        var fila = FilaBaseMutable("AC", "1111", copago: 100000m);
        fila["Valor Total"] = 50000m;
        var p = builder.Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("supera suma servicios"));
    }

    [Fact]
    public void Validate_CopagosIgualesAServicios_NoEmiteError()
    {
        var builder = new RipsJsonBuilder();
        var fila = FilaBaseMutable("AC", "1111", copago: 45000m);
        fila["Valor Total"] = 45000m;
        var p = builder.Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        Assert.DoesNotContain(builder.Validate(p), e => e.Contains("supera suma servicios"));
    }

    [Fact]
    public void Validate_CopagosPorPacienteSeAcumulan()
    {
        // Paciente 1111 tiene 2 servicios: 20000 c/u = 40000 en servicios,
        // 15000+15000 = 30000 en copagos -> OK (30k <= 40k).
        // Paciente 2222 tiene 1 servicio de 10000 y copago 20000 -> viola (§4.1).
        var builder = new RipsJsonBuilder();
        var f1a = FilaBaseMutable("AC", "1111", copago: 15000m); f1a["Valor Total"] = 20000m;
        var f1b = FilaBaseMutable("AC", "1111", copago: 15000m); f1b["Valor Total"] = 20000m;
        var f2 = FilaBaseMutable("AC", "2222", copago: 20000m); f2["Valor Total"] = 10000m;
        var p = builder.Build(SampleDetalle(), new[]
        {
            (IReadOnlyDictionary<string, object?>)f1a,
            (IReadOnlyDictionary<string, object?>)f1b,
            (IReadOnlyDictionary<string, object?>)f2
        }, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("Paciente 2222"));
        Assert.DoesNotContain(errores, e => e.Contains("Paciente 1111"));
    }

    [Fact]
    public void Validate_ValorNegativo_EmiteError()
    {
        var builder = new RipsJsonBuilder();
        var fila = FilaBaseMutable("AC", "1111");
        fila["Valor Total"] = -100m;
        var p = builder.Build(SampleDetalle(), new[] { (IReadOnlyDictionary<string, object?>)fila }, "900123456");
        var errores = builder.Validate(p);
        Assert.Contains(errores, e => e.Contains("vrServicio negativo"));
    }

    [Fact]
    public void TotalNeto_SumaServiciosYRestaCopagos()
    {
        var builder = new RipsJsonBuilder();
        var f1 = FilaBaseMutable("AC", "1111", copago: 5000m); f1["Valor Total"] = 45000m;
        var f2 = FilaBaseMutable("AP", "2222", cuota: 3000m); f2["Valor Total"] = 80000m;
        var p = builder.Build(SampleDetalle(), new[]
        {
            (IReadOnlyDictionary<string, object?>)f1,
            (IReadOnlyDictionary<string, object?>)f2
        }, "900123456");
        var (serv, mod, neto) = RipsJsonBuilder.TotalNeto(p);
        Assert.Equal(125000m, serv);
        Assert.Equal(8000m, mod);
        Assert.Equal(117000m, neto);
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
            ["Fecha de Nacimiento"] = new DateOnly(1990, 1, 1),
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

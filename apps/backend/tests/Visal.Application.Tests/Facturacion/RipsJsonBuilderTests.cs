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

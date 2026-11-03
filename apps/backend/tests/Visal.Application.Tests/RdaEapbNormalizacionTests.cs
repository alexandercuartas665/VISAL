using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Verifica la eleccion de UN solo codigo EAPB para el Organization pagador del RDA
/// cuando aseguradoras.codigo trae DOS codigos pegados (contributivo + subsidiado).
/// MinSalud rechaza el valor doble con BUNDLE-005. La eleccion usa el regimen del
/// contrato, con fallback al regimen de la aseguradora y al del paciente.
/// </summary>
public class RdaEapbNormalizacionTests
{
    [Theory]
    // Codigo simple: se devuelve tal cual (trim), sin importar el regimen.
    [InlineData("EPS037", null, null, null, "EPS037")]
    [InlineData("  EPS037  ", null, null, null, "EPS037")]
    // Caso real NUEVA EPS (regimen de la aseguradora = SUBSIDIADO) -> EPSS41.
    [InlineData("EPS037 - EPSS41", null, "SUBSIDIADO", null, "EPSS41")]
    // Caso real NUEVA EPS con contrato Subsidiado -> EPSS41.
    [InlineData("EPS037 - EPSS41", "Subsidiado", null, null, "EPSS41")]
    // Caso real COOSALUD (contrato nulo, aseguradora CONTRIBUTIVO) -> EPS042.
    [InlineData("ESS024 - EPS042", null, "CONTRIBUTIVO", null, "EPS042")]
    // El contrato manda sobre el regimen (ruidoso) del paciente.
    [InlineData("EPS037 - EPSS41", "Subsidiado", null, "CONTRIBUTIVO", "EPSS41")]
    // Sin regimen en contrato ni aseguradora: cae al del paciente.
    [InlineData("EPS037 - EPSS41", null, null, "subsidiado", "EPSS41")]
    // Sin ningun regimen: se prefiere el contributivo (no subsidiado).
    [InlineData("EPS037 - EPSS41", null, null, null, "EPS037")]
    [InlineData("ESS024 - EPS042", null, null, null, "EPS042")]
    public void NormalizarEapb_EligeUnSoloCodigo(
        string codigo, string? tipoContrato, string? regimenAseg, string? regimenPac, string esperado)
    {
        TipoContrato? tc = tipoContrato switch
        {
            "Subsidiado" => TipoContrato.Subsidiado,
            "Contributivo" => TipoContrato.Contributivo,
            _ => null
        };
        Assert.Equal(esperado,
            RdaConsultaBuilderService.NormalizarEapb(codigo, tc, regimenAseg, regimenPac));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizarEapb_VacioDevuelveNull(string? codigo)
    {
        Assert.Null(RdaConsultaBuilderService.NormalizarEapb(codigo, null, null, null));
    }
}

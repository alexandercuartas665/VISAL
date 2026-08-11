using Visal.Application.Facturacion;
using Visal.Domain.Enums;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

/// <summary>
/// Tests del formateador de columnas del snapshot (fecha/numero/moneda para Excel y CSV).
/// Cultura de referencia es-CO: miles ".", decimal ",".
/// </summary>
public sealed class SnapshotColumnaFormatterTests
{
    [Theory]
    [InlineData("2026-08-01", "01/08/2026")]     // ISO
    [InlineData("01/08/2026", "01/08/2026")]     // ya local
    public void FormatoCsv_Fecha_UsaDdMmYyyy(string entrada, string esperado)
    {
        Assert.Equal(esperado, SnapshotColumnaFormatter.FormatoCsv(entrada, SnapshotColumnaFormato.Fecha, null));
    }

    [Theory]
    [InlineData("1234.5", "1.234,50")]           // invariante -> es-CO
    [InlineData(1234.5, "1.234,50")]             // ya numero
    public void FormatoCsv_NumeroDecimal_MilesYComaDecimal(object entrada, string esperado)
    {
        Assert.Equal(esperado, SnapshotColumnaFormatter.FormatoCsv(entrada, SnapshotColumnaFormato.NumeroDecimal, null));
    }

    [Fact]
    public void FormatoCsv_Moneda_AnteponeSimbolo()
    {
        Assert.Equal("$ 1.000,00", SnapshotColumnaFormatter.FormatoCsv("1000", SnapshotColumnaFormato.Moneda, null));
    }

    [Fact]
    public void FormatoCsv_General_DejaTalCual()
    {
        Assert.Equal("ABC-123", SnapshotColumnaFormatter.FormatoCsv("ABC-123", SnapshotColumnaFormato.General, null));
    }

    [Fact]
    public void FormatoCsv_NoParseable_ConFormatoNumero_DejaTexto()
    {
        // No es numero: se respeta el texto en vez de romper.
        Assert.Equal("N/A", SnapshotColumnaFormatter.FormatoCsv("N/A", SnapshotColumnaFormato.NumeroDecimal, null));
    }

    [Fact]
    public void ExcelPattern_MapeaTiposComunes()
    {
        Assert.Equal("dd/mm/yyyy", SnapshotColumnaFormatter.ExcelPattern(SnapshotColumnaFormato.Fecha, null));
        Assert.Equal("#,##0.00", SnapshotColumnaFormatter.ExcelPattern(SnapshotColumnaFormato.NumeroDecimal, null));
        Assert.Null(SnapshotColumnaFormatter.ExcelPattern(SnapshotColumnaFormato.General, null));
        Assert.Equal("dd/mm/yyyy", SnapshotColumnaFormatter.ExcelPattern(SnapshotColumnaFormato.Personalizado, "dd/mm/yyyy"));
    }
}

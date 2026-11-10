using System.Collections.Generic;
using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests;

/// <summary>
/// Protege el parseo RIGIDO de la fecha de atencion: Colombia es dd/MM/yyyy y NUNCA
/// MM/dd. Antes, un campo del cuerpo con valor "06/10/2026" (6 de octubre) se leia con
/// InvariantCulture como 6 de JUNIO (MM/dd) y la impresion transponia dia/mes cuando el
/// dia era menor o igual a 12.
/// </summary>
public class FechaAtencionHelperTests
{
    private const double BogotaOffsetHoras = -5;

    private static FormSchema SchemaConCampoCuerpo(string fieldType) => new()
    {
        Children = new()
        {
            new FormNode
            {
                Type = "field",
                FieldType = fieldType,
                Name = "fecha_hora",
                IsFechaAtencion = true,
            },
        },
    };

    [Theory]
    [InlineData("06/10/2026 14:27")] // 6 de octubre (dd/MM) con hora
    [InlineData("06/10/2026")]       // 6 de octubre (dd/MM) sin hora
    [InlineData("6/10/2026 14:27")]  // d/M sin cero inicial
    public void Cuerpo_DdMm_NoTransponeDiaMes(string valor)
    {
        var schema = SchemaConCampoCuerpo("text");
        var valores = new Dictionary<string, string?> { ["fecha_hora"] = valor };

        var res = FechaAtencionHelper.Calcular(schema, valores);

        Assert.NotNull(res);
        var bog = res!.Value.ToOffset(System.TimeSpan.FromHours(BogotaOffsetHoras));
        Assert.Equal(2026, bog.Year);
        Assert.Equal(10, bog.Month); // OCTUBRE, no junio
        Assert.Equal(6, bog.Day);
    }

    [Fact]
    public void Cuerpo_DdMm_ConservaLaHoraDePared()
    {
        var schema = SchemaConCampoCuerpo("datetime");
        var valores = new Dictionary<string, string?> { ["fecha_hora"] = "06/10/2026 14:27" };

        var res = FechaAtencionHelper.Calcular(schema, valores);

        Assert.NotNull(res);
        var bog = res!.Value.ToOffset(System.TimeSpan.FromHours(BogotaOffsetHoras));
        Assert.Equal(14, bog.Hour);
        Assert.Equal(27, bog.Minute);
    }

    [Fact]
    public void Cuerpo_Iso_SeParseaUnambiguo()
    {
        var schema = SchemaConCampoCuerpo("datetime");
        var valores = new Dictionary<string, string?> { ["fecha_hora"] = "2026-10-06T14:27" };

        var res = FechaAtencionHelper.Calcular(schema, valores);

        Assert.NotNull(res);
        var bog = res!.Value.ToOffset(System.TimeSpan.FromHours(BogotaOffsetHoras));
        Assert.Equal(10, bog.Month);
        Assert.Equal(6, bog.Day);
        Assert.Equal(14, bog.Hour);
        Assert.Equal(27, bog.Minute);
    }

    [Fact]
    public void Cuerpo_IsoConZonaZulu_RespetaElInstante()
    {
        // 19:27Z == 14:27 hora Bogota (UTC-5).
        var schema = SchemaConCampoCuerpo("datetime");
        var valores = new Dictionary<string, string?> { ["fecha_hora"] = "2026-10-06T19:27:00Z" };

        var res = FechaAtencionHelper.Calcular(schema, valores);

        Assert.NotNull(res);
        var bog = res!.Value.ToOffset(System.TimeSpan.FromHours(BogotaOffsetHoras));
        Assert.Equal(10, bog.Month);
        Assert.Equal(6, bog.Day);
        Assert.Equal(14, bog.Hour);
        Assert.Equal(27, bog.Minute);
    }

    [Fact]
    public void Cuerpo_DiaMayorQue12_TambienEsDdMm()
    {
        // "25/10/2026": 25 no es un mes valido en MM/dd, antes caia a fallback; ahora
        // se parsea directo como 25 de octubre.
        var schema = SchemaConCampoCuerpo("text");
        var valores = new Dictionary<string, string?> { ["fecha_hora"] = "25/10/2026" };

        var res = FechaAtencionHelper.Calcular(schema, valores);

        Assert.NotNull(res);
        var bog = res!.Value.ToOffset(System.TimeSpan.FromHours(BogotaOffsetHoras));
        Assert.Equal(10, bog.Month);
        Assert.Equal(25, bog.Day);
    }
}

using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests;

public class DefaultValuesHelperHidratarTests
{
    private static FormSchema SchemaTablaConDefaults()
    {
        // Modelo del bug de MARIA DORA: 3 columnas con defaultValue, una sin.
        return new FormSchema
        {
            Children = new()
            {
                new FormNode
                {
                    Id = "sec",
                    Type = "field",
                    FieldType = "table",
                    Columns = new()
                    {
                        new FormColumn { Id = "diag", Name = "diag", FieldType = "autocomplete" },
                        new FormColumn { Id = "ori",  Name = "ori",  FieldType = "select", DefaultValue = "ENFERMEDAD GENERAL" },
                        new FormColumn { Id = "tip",  Name = "tip",  FieldType = "select", DefaultValue = "1 - IMPRESION DIAGNOSTICA" },
                        new FormColumn { Id = "rel",  Name = "rel",  FieldType = "select", DefaultValue = "PRINCIPAL" }
                    }
                }
            }
        };
    }

    [Fact]
    public void HidrataCeldasAusentes_EnFilasCapturadas()
    {
        var v = new Dictionary<string, string?>
        {
            ["tbl:sec:_rows"] = "3",
            ["tbl:sec:0:diag"] = "H544",
            ["tbl:sec:1:diag"] = "H409",
            ["tbl:sec:2:diag"] = "E109"
        };

        var cambio = DefaultValuesHelper.HidratarDefaultsAusentes(v, SchemaTablaConDefaults());

        Assert.True(cambio);
        // Las 3 filas deben tener ori/tip/rel poblados con default
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("ENFERMEDAD GENERAL", v[$"tbl:sec:{i}:ori"]);
            Assert.Equal("1 - IMPRESION DIAGNOSTICA", v[$"tbl:sec:{i}:tip"]);
            Assert.Equal("PRINCIPAL", v[$"tbl:sec:{i}:rel"]);
        }
    }

    [Fact]
    public void RespetaCeldaExistenteAunqueSeaVacio()
    {
        // El doctor vacio deliberadamente la columna ori en la fila 1
        var v = new Dictionary<string, string?>
        {
            ["tbl:sec:_rows"] = "2",
            ["tbl:sec:0:diag"] = "H544",
            ["tbl:sec:1:diag"] = "H409",
            ["tbl:sec:1:ori"] = "" // presente con vacio
        };

        DefaultValuesHelper.HidratarDefaultsAusentes(v, SchemaTablaConDefaults());

        Assert.Equal("ENFERMEDAD GENERAL", v["tbl:sec:0:ori"]); // ausente -> hidratada
        Assert.Equal("", v["tbl:sec:1:ori"]);                    // vacia explicita -> respetada
    }

    [Fact]
    public void Idempotente_NoReescribeEnSegundaCorrida()
    {
        var v = new Dictionary<string, string?>
        {
            ["tbl:sec:_rows"] = "1",
            ["tbl:sec:0:diag"] = "H544"
        };

        var cambio1 = DefaultValuesHelper.HidratarDefaultsAusentes(v, SchemaTablaConDefaults());
        var cambio2 = DefaultValuesHelper.HidratarDefaultsAusentes(v, SchemaTablaConDefaults());

        Assert.True(cambio1);
        Assert.False(cambio2);
    }

    [Fact]
    public void SinFilas_NoHaceNada()
    {
        var v = new Dictionary<string, string?>();
        var cambio = DefaultValuesHelper.HidratarDefaultsAusentes(v, SchemaTablaConDefaults());
        Assert.False(cambio);
        Assert.Empty(v);
    }

    [Fact]
    public void CampoSueltoConDefault_SeHidrataSiAusente()
    {
        var schema = new FormSchema
        {
            Children = new()
            {
                new FormNode { Type = "field", Name = "sexo", FieldType = "select", DefaultValue = "MASCULINO" }
            }
        };
        var v = new Dictionary<string, string?>();

        var cambio = DefaultValuesHelper.HidratarDefaultsAusentes(v, schema);

        Assert.True(cambio);
        Assert.Equal("MASCULINO", v["sexo"]);
    }

    [Fact]
    public void CampoSueltoPresente_NoSeToca()
    {
        var schema = new FormSchema
        {
            Children = new()
            {
                new FormNode { Type = "field", Name = "sexo", FieldType = "select", DefaultValue = "MASCULINO" }
            }
        };
        var v = new Dictionary<string, string?> { ["sexo"] = "FEMENINO" };

        var cambio = DefaultValuesHelper.HidratarDefaultsAusentes(v, schema);

        Assert.False(cambio);
        Assert.Equal("FEMENINO", v["sexo"]);
    }

    [Fact]
    public void RecorreSecciones()
    {
        var schema = new FormSchema
        {
            Children = new()
            {
                new FormNode
                {
                    Type = "section",
                    Children = new()
                    {
                        new FormNode { Type = "field", Name = "campo", FieldType = "select", DefaultValue = "X" }
                    }
                }
            }
        };
        var v = new Dictionary<string, string?>();

        DefaultValuesHelper.HidratarDefaultsAusentes(v, schema);

        Assert.Equal("X", v["campo"]);
    }

    [Fact]
    public void ColumnaCondicional_NoHidrataSiTriggerNoMatch()
    {
        // Tabla con columna cantidad habilitada solo si refiere=SI
        var schema = new FormSchema
        {
            Children = new()
            {
                new FormNode
                {
                    Id = "t", Type = "field", FieldType = "table",
                    Columns = new()
                    {
                        new FormColumn { Id = "ref", Name = "refiere", FieldType = "select" },
                        new FormColumn { Id = "cant", Name = "cantidad", FieldType = "number",
                                         DefaultValue = "0",
                                         EnabledByColumn = "refiere", EnabledByValue = "SI" }
                    }
                }
            }
        };
        var v = new Dictionary<string, string?>
        {
            ["tbl:t:_rows"] = "2",
            ["tbl:t:0:ref"] = "SI",
            ["tbl:t:1:ref"] = "NO"
        };

        DefaultValuesHelper.HidratarDefaultsAusentes(v, schema);

        Assert.Equal("0", v["tbl:t:0:cant"]);           // habilitada
        Assert.False(v.ContainsKey("tbl:t:1:cant"));    // deshabilitada, no hidratada
    }
}

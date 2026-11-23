using Visal.Application.Facturacion;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

/// <summary>Operadores del filtro por columna de la vista "Ver" de un snapshot.</summary>
public sealed class SnapshotFiltroOperadorTests
{
    private const string Expr = "(datos_json->>'Historia con listado firmas')";

    [Fact]
    public void Operador_desconocido_o_null_es_contiene()
    {
        Assert.Equal(SnapshotFiltroOperador.Contiene, SnapshotFiltroOperador.Normalizar(null));
        Assert.Equal(SnapshotFiltroOperador.Contiene, SnapshotFiltroOperador.Normalizar("xx"));
        var vals = new List<object> { Guid.Empty };
        var sql = SnapshotFiltroOperador.ConstruirCondicion(Expr, null, "abc", vals);
        Assert.Equal($"{Expr} ILIKE {{1}}", sql);
        Assert.Equal("%abc%", vals[1]);
    }

    [Fact]
    public void Igual_a_numero_compara_texto_y_numero()
    {
        var vals = new List<object> { Guid.Empty };
        var sql = SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Igual, "0", vals)!;
        Assert.Contains("lower(btrim(", sql);
        Assert.Contains("::numeric END) = {2}", sql);
        Assert.Equal("0", vals[1]);
        Assert.Equal(0m, vals[2]);
        Assert.DoesNotContain("ILIKE", sql);
    }

    [Fact]
    public void Distinto_excluye_vacios()
    {
        var vals = new List<object>();
        var sql = SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Distinto, "0", vals)!;
        Assert.StartsWith($"({Expr} IS NOT NULL AND btrim({Expr}) <> '' AND NOT (", sql);
    }

    [Fact]
    public void Mayor_que_numero_usa_cast_seguro()
    {
        var vals = new List<object> { Guid.Empty };
        var sql = SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Mayor, "0", vals)!;
        Assert.Equal($"(CASE WHEN {Expr} ~ '^\\s*-?[0-9]+([.][0-9]+)?\\s*$' THEN ({Expr})::numeric END) > {{1}}", sql);
        Assert.Equal(0m, vals[1]);
    }

    [Fact]
    public void Menor_o_igual_fecha_dd_mm_aaaa_compara_iso()
    {
        var vals = new List<object> { Guid.Empty };
        var sql = SnapshotFiltroOperador.ConstruirCondicion("(datos_json->>'Fecha')", SnapshotFiltroOperador.MenorIgual, "15/10/2026", vals)!;
        Assert.Contains("left((datos_json->>'Fecha'), 10) <= {1}", sql);
        Assert.Equal("2026-10-15", vals[1]);
    }

    [Fact]
    public void Vacio_y_no_vacio_no_necesitan_valor()
    {
        var vals = new List<object>();
        Assert.Equal($"({Expr} IS NULL OR btrim({Expr}) = '')", SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Vacio, null, vals));
        Assert.Equal($"({Expr} IS NOT NULL AND btrim({Expr}) <> '')", SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.NoVacio, "", vals));
        Assert.Empty(vals);
        Assert.True(SnapshotFiltroOperador.SinValor(SnapshotFiltroOperador.Vacio));
    }

    [Fact]
    public void Contiene_escapa_comodines_y_sin_valor_no_aplica()
    {
        var vals = new List<object>();
        SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Contiene, "50%_x", vals);
        Assert.Equal("%50\\%\\_x%", vals[0]);
        Assert.Null(SnapshotFiltroOperador.ConstruirCondicion(Expr, SnapshotFiltroOperador.Igual, "  ", vals));
    }

    [Fact]
    public void Ninguna_condicion_lleva_llaves_literales()
    {
        foreach (var (op, _) in SnapshotFiltroOperador.Todos)
        {
            var vals = new List<object> { Guid.Empty };
            var sql = SnapshotFiltroOperador.ConstruirCondicion(Expr, op, "15/10/2026", vals) ?? "";
            var sinPlaceholders = System.Text.RegularExpressions.Regex.Replace(sql, @"\{\d+\}", "");
            Assert.DoesNotContain("{", sinPlaceholders);
            Assert.DoesNotContain("}", sinPlaceholders);
        }
    }
}

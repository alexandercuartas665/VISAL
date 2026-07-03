using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests;

public class VisibleWhenEvaluatorTests
{
    private static Dictionary<string, string?> V(params (string k, string? v)[] entries)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in entries) { d[k] = v; }
        return d;
    }

    [Fact]
    public void NullRule_AlwaysVisible()
        => Assert.True(VisibleWhenEvaluator.ShouldShow(null, V()));

    [Fact]
    public void EmptyField_AlwaysVisible()
        => Assert.True(VisibleWhenEvaluator.ShouldShow(new VisibleWhenRule { Field = "", Operator = "equals", Value = "X" }, V()));

    [Fact]
    public void Equals_MatchCaseInsensitive_IsVisible()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "equals", Value = "FEMENINO" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "femenino"))));
    }

    [Fact]
    public void Equals_DifferentValue_IsHidden()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "equals", Value = "FEMENINO" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "MASCULINO"))));
    }

    [Fact]
    public void Equals_MissingValue_IsHidden_ClinicalDefault()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "equals", Value = "FEMENINO" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V()));
    }

    [Fact]
    public void NotEquals_HidesWhenEqual()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "notEquals", Value = "MASCULINO" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "MASCULINO"))));
    }

    [Fact]
    public void NotEquals_MissingValue_IsVisible()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "notEquals", Value = "MASCULINO" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V()));
    }

    [Fact]
    public void In_CommaList_MatchIsVisible()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "in", Value = "FEMENINO,OTRO" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "OTRO"))));
    }

    [Fact]
    public void In_JsonArray_MatchIsVisible()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "in", Value = "[\"FEMENINO\",\"OTRO\"]" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "FEMENINO"))));
    }

    [Fact]
    public void In_NoMatch_IsHidden()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "in", Value = "FEMENINO,OTRO" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "MASCULINO"))));
    }

    [Fact]
    public void NotIn_HidesWhenMatch()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "notIn", Value = "MASCULINO" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "MASCULINO"))));
    }

    [Fact]
    public void IsEmpty_TrueWhenNull()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "isEmpty" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V()));
    }

    [Fact]
    public void IsEmpty_FalseWhenHasValue()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "isEmpty" };
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "FEMENINO"))));
    }

    [Fact]
    public void IsNotEmpty_TrueWhenHasValue()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "isNotEmpty" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "FEMENINO"))));
    }

    [Fact]
    public void GreaterThan_NumericCompare()
    {
        var rule = new VisibleWhenRule { Field = "edad", Operator = "greaterThan", Value = "18" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("edad", "20"))));
        Assert.False(VisibleWhenEvaluator.ShouldShow(rule, V(("edad", "10"))));
    }

    [Fact]
    public void UnknownOperator_IsVisibleByDesign()
    {
        var rule = new VisibleWhenRule { Field = "sexo", Operator = "matchesRegex", Value = ".*" };
        Assert.True(VisibleWhenEvaluator.ShouldShow(rule, V(("sexo", "cualquier"))));
    }
}

using Teikem.Infrastructure.Dsl;
using Xunit;

namespace Teikem.Tests;

public class RuleEvaluatorTests
{
    private static Dictionary<string, object?> Row(params (string, object?)[] kv)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void And_simplified_array_form_matches_all_conditions()
    {
        var f = RuleEvaluator.CompileFilter("""[{"field":"Status","op":"eq","value":"DELIVERED"},{"field":"Pieces","op":"gt","value":2}]""");
        Assert.True(f(Row(("Status", "delivered"), ("Pieces", 3))));
        Assert.False(f(Row(("Status", "delivered"), ("Pieces", 2))));
    }

    [Fact]
    public void Or_and_not_nesting_works()
    {
        var f = RuleEvaluator.CompileFilter("""{"or":[{"field":"A","op":"eq","value":1},{"not":{"field":"B","op":"isNull"}}]}""");
        Assert.True(f(Row(("A", 0), ("B", "x"))));
        Assert.True(f(Row(("A", 1), ("B", null))));
        Assert.False(f(Row(("A", 0), ("B", null))));
    }

    [Fact]
    public void Dates_compare_as_dates_not_as_parsed_floats()
    {
        // El mock comparaba "2026-07-25" con parseFloat → 2026; aquí compara fechas completas.
        var f = RuleEvaluator.CompileFilter("""{"field":"Date","op":"gte","value":"2026-07-20"}""");
        Assert.True(f(Row(("Date", new DateTime(2026, 7, 25)))));
        Assert.False(f(Row(("Date", new DateTime(2026, 1, 25)))));
        Assert.True(f(Row(("Date", "2026-07-25T10:00:00Z"))));
    }

    [Fact]
    public void In_between_contains_operators()
    {
        Assert.True(RuleEvaluator.Matches(Row(("S", "B")), """{"field":"S","op":"in","value":["A","B"]}"""));
        Assert.True(RuleEvaluator.Matches(Row(("N", 5)), """{"field":"N","op":"between","value":[1,10]}"""));
        Assert.False(RuleEvaluator.Matches(Row(("N", 11)), """{"field":"N","op":"between","value":[1,10]}"""));
        Assert.True(RuleEvaluator.Matches(Row(("T", "Farmacia Las Marías")), """{"field":"T","op":"contains","value":"marías"}"""));
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        Assert.Throws<InvalidOperationException>(() => RuleEvaluator.Matches(Row(("A", 1)), """{"field":"A","op":"xyz","value":1}"""));
    }

    [Fact]
    public void Validation_spec_regex_min_max_length()
    {
        var spec = """{"regex":"^CC-\\d{3}$","minLength":6,"maxLength":6}""";
        Assert.Empty(RuleEvaluator.Validate("CC-100", spec));
        Assert.NotEmpty(RuleEvaluator.Validate("CC-1", spec));
        Assert.Empty(RuleEvaluator.Validate(5m, """{"min":1,"max":10}"""));
        Assert.NotEmpty(RuleEvaluator.Validate(11m, """{"min":1,"max":10}"""));
    }

    [Fact]
    public void Referenced_fields_are_collected()
    {
        var fields = RuleEvaluator.ReferencedFields("""{"and":[{"field":"A","op":"eq","value":1},{"or":[{"field":"B","op":"isNull"}]}]}""");
        Assert.Contains("A", fields);
        Assert.Contains("B", fields);
    }
}

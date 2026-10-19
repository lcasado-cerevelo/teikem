using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

// Lote 131: archivos con varias líneas por orden — el mapeo dice qué NO es pieza (descripción o factura) y todo lo demás es pieza; vale para cualquier cliente.
public sealed class ImportLineRulesTests
{
    private static readonly ImportLineRule[] AxisLike =
    [
        new(ImportLineKinds.Invoice, 5, "EQUALS", "Invoice"),
        new(ImportLineKinds.Description, 5, "IN", "Drum | Item | Descripción"),
        new(ImportLineKinds.Description, 6, "CONTAINS", "drone"),
    ];

    [Theory]
    [InlineData("Invoice", "", ImportLineKinds.Invoice)]
    [InlineData("invoice ", "", ImportLineKinds.Invoice)]          // sin mayúsculas ni espacios de más
    [InlineData("Drum", "", ImportLineKinds.Description)]          // lista de valores
    [InlineData("descripcion", "", ImportLineKinds.Description)]   // sin acentos
    [InlineData("Box", "", ImportLineKinds.Piece)]                 // lo que no es descripción ni factura es pieza
    [InlineData("", "", ImportLineKinds.Piece)]
    [InlineData("Box", "Mini DRONE X", ImportLineKinds.Description)] // otra columna, contiene
    public void A_line_is_a_piece_unless_a_rule_says_otherwise(string typeCell, string textCell, string expected)
    {
        var cells = new[] { "O-1", "", "", "", typeCell, textCell };
        Assert.Equal(expected, ImportLineRules.Classify(cells, AxisLike));
    }

    [Fact]
    public void Without_rules_every_line_is_a_piece_and_the_first_matching_rule_wins()
    {
        Assert.Equal(ImportLineKinds.Piece, ImportLineRules.Classify(new[] { "x" }, null));
        Assert.Equal(ImportLineKinds.Piece, ImportLineRules.Classify(new[] { "x" }, Array.Empty<ImportLineRule>()));
        var both = new ImportLineRule[] { new(ImportLineKinds.Invoice, 1, "NOT_EMPTY"), new(ImportLineKinds.Description, 1, "NOT_EMPTY") };
        Assert.Equal(ImportLineKinds.Invoice, ImportLineRules.Classify(new[] { "x" }, both));
        Assert.Equal(ImportLineKinds.Description, ImportLineRules.Classify(new[] { "" }, new ImportLineRule[] { new(ImportLineKinds.Description, 1, "EMPTY") }));
    }

    [Fact]
    public void Rules_are_validated_with_exact_messages()
    {
        Assert.Empty(ImportLineRules.Validate(AxisLike));
        var bad = ImportLineRules.Validate(new ImportLineRule[]
        {
            new("PIECE", 1, "EQUALS", "x"),
            new(ImportLineKinds.Invoice, 0, "EQUALS", "x"),
            new(ImportLineKinds.Invoice, 1, "MAYBE", "x"),
            new(ImportLineKinds.Description, 1, "CONTAINS", " "),
        });
        Assert.Equal(ImportLineRules.UnknownKindMessage("PIECE"), bad["rules[0].kind"]);
        Assert.Contains("entre 1 y", bad["rules[1].position"]);
        Assert.Equal(ImportLineRules.UnknownOperatorMessage("MAYBE"), bad["rules[2].operator"]);
        Assert.Equal(ImportLineRules.ValueRequiredMessage("CONTAINS"), bad["rules[3].value"]);
        Assert.Equal(ImportLineRules.TooManyRulesMessage, ImportLineRules.Validate(Enumerable.Repeat(AxisLike[0], 21).ToList())["rules"]);
    }

    [Fact]
    public void Normalize_keeps_the_canonical_shape()
    {
        var n = ImportLineRules.Normalize(new ImportLineRule("invoice", 3, "in", " a | b "));
        Assert.Equal(("INVOICE", 3, "IN", "a | b"), (n.Kind, n.Position, n.Operator, n.Value));
        Assert.Null(ImportLineRules.Normalize(new ImportLineRule("DESCRIPTION", 3, "not_empty", "x")).Value);
    }

    [Fact]
    public void The_piece_number_is_a_known_import_field()
    {
        Assert.Equal("pieceNumber", ImportFields.Canonical("PIECENUMBER"));
    }
}

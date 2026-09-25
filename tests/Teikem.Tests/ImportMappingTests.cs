using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (P9): mapeo por posición de columna, defaults de la plantilla y validación de la plantilla del importador de órdenes.</summary>
public class ImportMappingTests
{
    private static readonly ImportColumn[] Columns =
    [
        new(1, "consigneeName"), new(2, "line1"), new(3, "city"), new(4, "packageType"), new(5, "pieces"), new(6, "clientInvoiceNumber"),
    ];

    [Fact]
    public void Maps_positions_to_fields_and_trims()
    {
        var values = ImportMapping.Map(new[] { " Farmacia ", "Calle 5", "Ponce", "box", "2", "F-1" }, Columns, null);
        Assert.Equal("Farmacia", values["consigneeName"]);
        Assert.Equal("Calle 5", values["line1"]);
        Assert.Equal("box", values["packageType"]);
        Assert.Equal("2", values["pieces"]);
        Assert.Equal("F-1", values["clientInvoiceNumber"]);
    }

    [Fact]
    public void Empty_cell_falls_back_to_default_or_is_omitted()
    {
        var defaults = new Dictionary<string, string> { ["packageType"] = "BOX", ["serviceType"] = "STANDARD" };
        var values = ImportMapping.Map(new[] { "Farmacia", "Calle 5", "Ponce", "", "" }, Columns, defaults);
        Assert.Equal("BOX", values["packageType"]);      // celda vacía → default
        Assert.Equal("STANDARD", values["serviceType"]); // default sin columna también aplica
        Assert.False(values.ContainsKey("pieces"));      // vacía y sin default → omitida
        Assert.False(values.ContainsKey("clientInvoiceNumber")); // la fila no llega a la posición 6
    }

    [Fact]
    public void Cell_value_wins_over_default()
    {
        var defaults = new Dictionary<string, string> { ["packageType"] = "BOX" };
        var values = ImportMapping.Map(new[] { "Farmacia", "Calle 5", "Ponce", "ENVELOPE" }, Columns, defaults);
        Assert.Equal("ENVELOPE", values["packageType"]);
    }

    [Fact]
    public void Field_names_are_case_insensitive_but_keys_are_canonical()
    {
        var columns = new[] { new ImportColumn(1, "CONSIGNEENAME"), new ImportColumn(2, "Pieces") };
        var values = ImportMapping.Map(new[] { "Farmacia", "3" }, columns, new Dictionary<string, string> { ["PackageType"] = "BOX" });
        Assert.Equal("Farmacia", values["consigneeName"]);
        Assert.Equal("3", values["pieces"]);
        Assert.Equal("BOX", values["packageType"]);
        Assert.Equal("consigneeName", ImportFields.Canonical("ConsigneeName"));
        Assert.Null(ImportFields.Canonical("nope"));
    }

    [Fact]
    public void Unknown_field_in_map_throws()
    {
        Assert.Throws<ArgumentException>(() => ImportMapping.Map(new[] { "x" }, new[] { new ImportColumn(1, "nope") }, null));
    }

    [Fact]
    public void Validate_accepts_a_good_template()
    {
        var errors = ImportMapping.ValidateColumns(Columns, new Dictionary<string, string> { ["serviceType"] = "STANDARD" });
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_unknown_field_and_repeated_position()
    {
        var columns = new[] { new ImportColumn(1, "consigneeName"), new ImportColumn(1, "line1"), new ImportColumn(2, "nope"), new ImportColumn(0, "pieces") };
        var errors = ImportMapping.ValidateColumns(columns, null);
        Assert.Equal("La posición 1 está repetida.", errors["columns[1].position"]);
        Assert.Equal("Campo desconocido: nope.", errors["columns[2].field"]);
        Assert.Equal("La posición debe ser 1 o mayor.", errors["columns[3].position"]);
    }

    [Fact]
    public void Validate_rejects_repeated_field_and_unknown_default()
    {
        var columns = new[] { new ImportColumn(1, "consigneeName"), new ImportColumn(2, "ConsigneeName"), new ImportColumn(3, "pieces") };
        var errors = ImportMapping.ValidateColumns(columns, new Dictionary<string, string> { ["foo"] = "bar" });
        Assert.Equal("El campo consigneeName está repetido.", errors["columns[1].field"]);
        Assert.Equal("Campo desconocido: foo.", errors["defaults.foo"]);
    }

    [Fact]
    public void Validate_requires_consignee_name_or_code()
    {
        var errors = ImportMapping.ValidateColumns(new[] { new ImportColumn(1, "line1"), new ImportColumn(2, "pieces") }, null);
        Assert.Equal(ImportMapping.ConsigneeColumnMessage, errors["columns"]);

        Assert.Empty(ImportMapping.ValidateColumns(new[] { new ImportColumn(1, "consigneeCode"), new ImportColumn(2, "pieces") }, null));
    }

    [Fact]
    public void Validate_requires_package_type_or_pieces_in_columns_or_defaults()
    {
        var onlyConsignee = new[] { new ImportColumn(1, "consigneeName"), new ImportColumn(2, "line1") };
        Assert.Equal(ImportMapping.PackageColumnMessage, ImportMapping.ValidateColumns(onlyConsignee, null)["columns"]);
        Assert.Empty(ImportMapping.ValidateColumns(onlyConsignee, new Dictionary<string, string> { ["packageType"] = "BOX" }));
        Assert.Empty(ImportMapping.ValidateColumns(onlyConsignee, new Dictionary<string, string> { ["pieces"] = "1" }));
    }

    [Fact]
    public void Validate_requires_at_least_one_column()
    {
        Assert.Equal(ImportMapping.ColumnsRequiredMessage, ImportMapping.ValidateColumns(Array.Empty<ImportColumn>(), null)["columns"]);
        Assert.Equal(ImportMapping.ColumnsRequiredMessage, ImportMapping.ValidateColumns(null, null)["columns"]);
    }

    [Fact]
    public void Catalog_contains_every_field_of_the_plan()
    {
        var expected = new[]
        {
            "orderNumber", "clientInvoiceNumber", "serviceType", "consigneeName", "consigneeCode", "line1", "line2", "city", "state",
            "postalCode", "country", "contactPhone", "packageType", "pieces", "weightKg", "volumeM3", "description", "codAmount",
            "requestedDate", "notes", "reference",
        };
        Assert.Equal(expected.OrderBy(x => x), ImportFields.All.OrderBy(x => x));
        Assert.All(expected, f => Assert.True(ImportFields.IsKnown(f)));
    }

    [Fact]
    public void Typed_parsers_are_invariant()
    {
        Assert.True(ImportMapping.TryParseInt(" 3 ", out var i) && i == 3);
        Assert.False(ImportMapping.TryParseInt("3.5", out _));
        Assert.True(ImportMapping.TryParseDecimal("12.50", out var d) && d == 12.5m);
        Assert.False(ImportMapping.TryParseDecimal("abc", out _));
        Assert.True(ImportMapping.TryParseDate("2026-10-05", out var iso) && iso == new DateTime(2026, 10, 5));
        Assert.True(ImportMapping.TryParseDate("10/05/2026", out var us) && us == new DateTime(2026, 10, 5));
        Assert.False(ImportMapping.TryParseDate("no-date", out _));
        Assert.False(ImportMapping.TryParseDate("", out _));
    }
}

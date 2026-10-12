using Teikem.Domain.Clients;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): catálogo de campos, validación de la plantilla de consignatarios (mensajes exactos), mapeo celda → default
/// y parseos de sí/no, hora HH:mm y ventana horaria de ConsigneeImportMapping.
/// </summary>
public class ConsigneeImportMappingTests
{
    private static ImportColumn[] Cols(params string[] fields) => fields.Select((f, i) => new ImportColumn(i + 1, f)).ToArray();

    private static IReadOnlyDictionary<string, string> Validate(ImportColumn[] columns, Dictionary<string, string>? defaults = null)
        => ConsigneeImportMapping.ValidateColumns(columns, defaults);

    [Fact]
    public void The_field_catalog_is_exact_and_case_insensitive()
    {
        Assert.Equal(new[]
        {
            "code", "name", "line1", "line2", "city", "state", "postalCode", "country", "clientCode", "clientName",
            "defaultServiceMinutes", "defaultWindowStart", "defaultWindowEnd", "accessNotes", "deliveryNotes", "allowDupInvoice", "contactPhone",
        }, ConsigneeImportFields.All);
        Assert.Equal("postalCode", ConsigneeImportFields.Canonical(" POSTALCODE "));
        Assert.Equal("allowDupInvoice", ConsigneeImportFields.Canonical("allowdupinvoice"));
        Assert.Null(ConsigneeImportFields.Canonical("locationType")); // el tipo es fijo DELIVERY
        Assert.Null(ConsigneeImportFields.Canonical("consigneeName"));  // campo de órdenes, no de consignatarios
        Assert.Equal(new[] { "city", "line1", "name" }, ConsigneeImportFields.Required.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "clientCode", "clientName", "code", "line1", "name" }, ConsigneeImportFields.NoDefault.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_complete_template_is_valid()
    {
        Assert.Empty(Validate(Cols("code", "name", "line1", "city", "postalCode", "clientCode", "contactPhone", "allowDupInvoice"),
            new Dictionary<string, string> { ["country"] = "PR", ["defaultServiceMinutes"] = "10" }));
    }

    [Fact]
    public void Name_and_line1_columns_are_required()
    {
        Assert.Equal("La plantilla debe incluir las columnas name y line1.", Validate(Cols("name", "city"))["columns"]);
        Assert.Equal("La plantilla debe incluir las columnas name y line1.", Validate(Cols("line1", "city"))["columns"]);
    }

    [Fact]
    public void City_can_come_from_a_column_or_from_defaults()
    {
        Assert.Equal("La plantilla debe indicar la ciudad (city) en una columna o en defaults.", Validate(Cols("name", "line1"))["columns"]);
        Assert.Empty(Validate(Cols("name", "line1"), new Dictionary<string, string> { ["city"] = "San Juan" }));
        Assert.Empty(Validate(Cols("name", "line1", "city")));
    }

    [Fact]
    public void Client_code_and_client_name_together_are_rejected()
    {
        Assert.Equal("Use clientCode o clientName, no ambos.", Validate(Cols("name", "line1", "city", "clientCode", "clientName"))["columns"]);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("line1")]
    [InlineData("clientCode")]
    [InlineData("clientName")]
    public void Identity_fields_do_not_accept_a_default(string field)
    {
        var errors = Validate(Cols("name", "line1", "city"), new Dictionary<string, string> { [field] = "X" });
        Assert.Equal($"El campo {field} no admite valor por defecto.", errors[$"defaults.{field}"]);
    }

    [Fact]
    public void Positions_repeated_fields_and_unknown_fields_use_the_order_template_messages()
    {
        var errors = Validate(new[] { new ImportColumn(1, "name"), new ImportColumn(1, "line1"), new ImportColumn(0, "city"), new ImportColumn(3, "NAME"), new ImportColumn(4, "foo") },
            new Dictionary<string, string> { ["bar"] = "1" });
        Assert.Equal("La posición 1 está repetida.", errors["columns[1].position"]);
        Assert.Equal("La posición debe ser 1 o mayor.", errors["columns[2].position"]);
        Assert.Equal("El campo name está repetido.", errors["columns[3].field"]);
        Assert.Equal("Campo desconocido: foo.", errors["columns[4].field"]);
        Assert.Equal("Campo desconocido: bar.", errors["defaults.bar"]);
        Assert.Equal("Indique al menos una columna.", Validate(Array.Empty<ImportColumn>())["columns"]);
    }

    [Fact]
    public void Map_fills_with_defaults_and_the_cell_wins()
    {
        var columns = Cols("name", "line1", "city", "allowDupInvoice");
        var defaults = new Dictionary<string, string> { ["city"] = "Ponce", ["allowDupInvoice"] = "no", ["defaultServiceMinutes"] = "15" };
        var values = ConsigneeImportMapping.Map(new[] { " Farmacia ", "Calle 5", "", "sí" }, columns, defaults);
        Assert.Equal("Farmacia", values["name"]);
        Assert.Equal("Ponce", values["city"]);              // celda vacía → default
        Assert.Equal("sí", values["allowDupInvoice"]);      // la celda gana
        Assert.Equal("15", values["defaultServiceMinutes"]); // default sin columna también aplica
        Assert.False(values.ContainsKey("code"));
    }

    [Fact]
    public void Normalize_defaults_keeps_the_consignee_fields_with_canonical_keys()
    {
        var normalized = ConsigneeImportMapping.NormalizeDefaults(new Dictionary<string, string>
        {
            ["ALLOWDUPINVOICE"] = " sí ", ["defaultservicemInutes"] = "10", ["deliveryNotes"] = "Timbre", ["foo"] = "x", ["state"] = " ",
        });
        Assert.Equal("sí", normalized["allowDupInvoice"]);
        Assert.Equal("10", normalized["defaultServiceMinutes"]);
        Assert.Equal("Timbre", normalized["deliveryNotes"]);
        Assert.Equal(3, normalized.Count);
    }

    [Theory]
    [InlineData("sí", true)] [InlineData("SÍ", true)] [InlineData("si", true)] [InlineData("S", true)] [InlineData("1", true)]
    [InlineData("true", true)] [InlineData("Yes", true)] [InlineData("y", true)]
    [InlineData("no", false)] [InlineData("N", false)] [InlineData("0", false)] [InlineData("FALSE", false)]
    public void Try_parse_bool_accepts_the_variants(string text, bool expected)
    {
        Assert.True(ConsigneeImportMapping.TryParseBool(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("quizás")] [InlineData("2")] [InlineData("")] [InlineData(null)]
    public void Try_parse_bool_rejects_anything_else(string? text) => Assert.False(ConsigneeImportMapping.TryParseBool(text, out _));

    [Theory]
    [InlineData("08:00", 8, 0)] [InlineData("8:30", 8, 30)] [InlineData("17:45:00", 17, 45)] [InlineData(" 23:59 ", 23, 59)]
    public void Try_parse_time_accepts_24_hour_formats(string text, int hour, int minute)
    {
        Assert.True(ConsigneeImportMapping.TryParseTime(text, out var t));
        Assert.Equal(new TimeOnly(hour, minute), t);
    }

    [Theory]
    [InlineData("8am")] [InlineData("25:00")] [InlineData("08.00")] [InlineData("")] [InlineData("5 PM")]
    public void Try_parse_time_rejects_other_formats(string text) => Assert.False(ConsigneeImportMapping.TryParseTime(text, out _));

    [Theory]
    [InlineData("defaultServiceMinutes", "diez", "Los minutos de servicio deben ser un número entero.")]
    [InlineData("defaultServiceMinutes", "-5", "Los minutos de servicio no pueden ser negativos.")]
    [InlineData("defaultWindowStart", "8am", "La hora debe tener el formato HH:mm (24 horas).")]
    [InlineData("DEFAULTWINDOWEND", "25:00", "La hora debe tener el formato HH:mm (24 horas).")]
    [InlineData("allowDupInvoice", "quizás", "El valor de allowDupInvoice debe ser sí o no (también 1/0 o true/false).")]
    [InlineData("country", "PRI", "El código de país debe ser ISO 3166-1 alfa-2 (2 letras).")]
    public void Typed_defaults_are_checked_when_saving_with_the_row_messages(string field, string value, string message)
    {
        var errors = Validate(Cols("name", "line1", "city"), new Dictionary<string, string> { [field] = value });
        Assert.Equal(message, errors[$"defaults.{field}"]);
    }

    [Fact]
    public void Valid_typed_defaults_and_long_texts_in_defaults()
    {
        Assert.Empty(Validate(Cols("name", "line1", "city"), new Dictionary<string, string>
        {
            ["defaultServiceMinutes"] = "15", ["defaultWindowStart"] = "8:00", ["defaultWindowEnd"] = "17:00",
            ["allowDupInvoice"] = "Sí", ["country"] = "us", ["deliveryNotes"] = "Timbre",
        }));
        var errors = Validate(Cols("name", "line1"), new Dictionary<string, string> { ["city"] = new string('x', 101), ["postalCode"] = new string('9', 21) });
        Assert.Equal("Máximo 100 caracteres.", errors["defaults.city"]);
        Assert.Equal("Máximo 20 caracteres.", errors["defaults.postalCode"]);
    }

    [Fact]
    public void Window_needs_both_hours_and_end_after_start()
    {
        Assert.Null(ConsigneeImportMapping.ValidateWindow(null, null));
        Assert.Null(ConsigneeImportMapping.ValidateWindow(new TimeOnly(8, 0), new TimeOnly(12, 0)));
        Assert.Equal(("defaultWindowEnd", "La ventana horaria requiere hora de inicio y hora de fin."), ConsigneeImportMapping.ValidateWindow(new TimeOnly(8, 0), null));
        Assert.Equal(("defaultWindowStart", "La ventana horaria requiere hora de inicio y hora de fin."), ConsigneeImportMapping.ValidateWindow(null, new TimeOnly(8, 0)));
        Assert.Equal(("defaultWindowEnd", "La hora de fin de la ventana debe ser posterior a la de inicio."), ConsigneeImportMapping.ValidateWindow(new TimeOnly(12, 0), new TimeOnly(8, 0)));
        Assert.Equal(("defaultWindowEnd", "La hora de fin de la ventana debe ser posterior a la de inicio."), ConsigneeImportMapping.ValidateWindow(new TimeOnly(8, 0), new TimeOnly(8, 0)));
    }
}

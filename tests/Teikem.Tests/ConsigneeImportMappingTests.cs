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
            "defaultServiceMinutes", "defaultWindowStart", "defaultWindowEnd", "accessNotes", "deliveryNotes", "allowDupInvoice", "contactPhone", "contactName",
        }, ConsigneeImportFields.All);
        Assert.Null(ConsigneeImportFields.Canonical("ignore")); // «No usar» es una marca, no un campo de la lista
        Assert.True(ConsigneeImportFields.IsIgnore(" IGNORE "));
        Assert.Equal("postalCode", ConsigneeImportFields.Canonical(" POSTALCODE "));
        Assert.Equal("allowDupInvoice", ConsigneeImportFields.Canonical("allowdupinvoice"));
        Assert.Null(ConsigneeImportFields.Canonical("locationType")); // el tipo es fijo DELIVERY
        Assert.Null(ConsigneeImportFields.Canonical("consigneeName"));  // campo de órdenes, no de consignatarios
        Assert.Equal(new[] { "line1", "name" }, ConsigneeImportFields.Required.OrderBy(x => x, StringComparer.Ordinal)); // Lote 46: city sale del ZIP
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
        Assert.Equal("La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.", Validate(Cols("name", "line1"))["columns"]);
        Assert.Empty(Validate(Cols("name", "line1"), new Dictionary<string, string> { ["city"] = "San Juan" }));
        Assert.Empty(Validate(Cols("name", "line1", "city")));
        // Lote 46 (F-B3): el código postal basta (el pueblo sale del catálogo)
        Assert.Empty(Validate(Cols("name", "line1", "postalCode")));
        Assert.Empty(Validate(Cols("name", "line1"), new Dictionary<string, string> { ["postalCode"] = "00901" }));
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
        var errors = Validate(new[] { new ImportColumn(1, "name"), new ImportColumn(1, "NAME"), new ImportColumn(0, "city"), new ImportColumn(3, "country"), new ImportColumn(4, "foo"), new ImportColumn(5, "Country") },
            new Dictionary<string, string> { ["bar"] = "1" });
        Assert.Equal("La posición 1 está repetida.", errors["columns[1].position"]);   // la misma columna dos veces en el mismo campo
        Assert.Equal("La posición debe ser 1 o mayor.", errors["columns[2].position"]);
        Assert.Equal("Campo desconocido: foo.", errors["columns[4].field"]);
        Assert.Equal("El campo country está repetido.", errors["columns[5].field"]); // los campos con formato toman una sola columna
        Assert.Equal("Campo desconocido: bar.", errors["defaults.bar"]);
        Assert.Equal("Indique al menos una columna.", Validate(Array.Empty<ImportColumn>())["columns"]);
    }

    // ---------------------------------------------------------------- Lote 45 (F-B2): «No usar», columnas unidas, persona de contacto, ZIP

    [Fact]
    public void Ignored_columns_are_saved_and_skipped_without_skipping_the_row()
    {
        var columns = new[] { new ImportColumn(1, "ignore"), new ImportColumn(2, "name"), new ImportColumn(3, "Ignore"), new ImportColumn(4, "line1"), new ImportColumn(5, "city") };
        Assert.Empty(Validate(columns));
        var values = ConsigneeImportMapping.Map(new[] { "basura", "Farmacia", "más basura", "Calle 1", "Ponce" }, columns, null);
        Assert.Equal(new[] { "city", "line1", "name" }, values.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("Farmacia", values["name"]);
        // solo columnas «No usar» = sin name ni line1
        Assert.Equal("La plantilla debe incluir las columnas name y line1.", Validate(new[] { new ImportColumn(1, "ignore") })["columns"]);
    }

    [Fact]
    public void An_ignored_column_cannot_also_feed_a_field()
    {
        var errors = Validate(new[] { new ImportColumn(1, "name"), new ImportColumn(2, "line1"), new ImportColumn(3, "city"), new ImportColumn(2, "ignore") });
        Assert.Equal("La columna 2 está marcada «No usar» y también tiene un campo asignado.", errors["columns[3].field"]);
    }

    [Fact]
    public void A_text_field_joins_several_columns_in_part_order_with_its_separator()
    {
        // Nombre = Código + « - » + Nombre; la columna del código también va sola al campo code; dirección partida en dos columnas.
        var columns = new[]
        {
            new ImportColumn(1, "code"), new ImportColumn(2, "name", 2, " - "), new ImportColumn(1, "name", 1),
            new ImportColumn(3, "line1"), new ImportColumn(4, "line1", Separator: ", "), new ImportColumn(5, "city"),
        };
        Assert.Empty(Validate(columns));
        var values = ConsigneeImportMapping.Map(new[] { "C-001", "Farmacia Uno", "Calle 1", "Urb. Sol", "Ponce" }, columns, null);
        Assert.Equal("C-001", values["code"]);
        Assert.Equal("C-001 - Farmacia Uno", values["name"]);
        Assert.Equal("Calle 1, Urb. Sol", values["line1"]);   // sin Part: por posición

        // una parte vacía no deja el separador colgando; todas vacías → el default
        var empty = ConsigneeImportMapping.Map(new[] { "", "Farmacia Dos", "", "", "" }, columns, new Dictionary<string, string> { ["city"] = "Ponce" });
        Assert.Equal("Farmacia Dos", empty["name"]);
        Assert.False(empty.ContainsKey("line1"));
        Assert.Equal("Ponce", empty["city"]);

        // sin separador: un espacio
        var spaced = ConsigneeImportMapping.Map(new[] { "Ana", "Rivera" }, new[] { new ImportColumn(1, "contactName", 1), new ImportColumn(2, "contactName", 2) }, null);
        Assert.Equal("Ana Rivera", spaced["contactName"]);
    }

    [Fact]
    public void Join_limits_parts_and_separator_length()
    {
        var columns = new List<ImportColumn> { new(1, "line1"), new(2, "city") };
        for (var p = 1; p <= 6; p++) columns.Add(new ImportColumn(10 + p, "name", p));
        columns.Add(new ImportColumn(20, "accessNotes", 1));
        columns.Add(new ImportColumn(21, "accessNotes", 2, new string('-', 11)));
        var errors = Validate(columns.ToArray());
        Assert.Equal("El campo name admite a lo sumo 5 columnas unidas.", errors["columns[7].field"]);
        Assert.Equal("El separador admite a lo sumo 10 caracteres.", errors["columns[9].separator"]);
    }

    [Fact]
    public void Normalize_columns_numbers_the_parts_and_drops_what_does_not_apply()
    {
        var normalized = ConsigneeImportMapping.NormalizeColumns(new[]
        {
            new ImportColumn(3, "NAME", 7, " / "), new ImportColumn(1, "name", 2, "x"), new ImportColumn(2, "line1", 4, "y"), new ImportColumn(4, "IGNORE", 1, "z"),
        });
        Assert.Equal(new[]
        {
            new ImportColumn(1, "name", 1, null),    // la primera parte nunca lleva separador
            new ImportColumn(2, "line1"),            // un campo de una sola columna queda sin parte ni separador
            new ImportColumn(3, "name", 2, " / "),
            new ImportColumn(4, "ignore"),
        }, normalized);
    }

    [Theory]
    [InlineData("901", "PR", "00901")] [InlineData("2108", "US", "02108")] [InlineData("00901", "PR", "00901")]
    [InlineData("901", "DO", "901")] [InlineData("90", "PR", "90")] [InlineData("00901-1234", "PR", "00901-1234")] [InlineData("A901", "PR", "A901")]
    public void Zip_codes_that_lost_their_leading_zeros_are_padded_for_pr_and_us(string zip, string country, string expected)
        => Assert.Equal(expected, ConsigneeImportMapping.NormalizePostalCode(zip, country));

    [Theory]
    [InlineData("787-555-0101", "7875550101", false)]
    [InlineData("787 555-0101", "7875550101", false)]
    [InlineData("7875550101", "7875550101", false)]
    [InlineData("(787)555-0101", "7875550101", false)]
    [InlineData("+1 (787) 555.0101", "7875550101", false)]
    [InlineData("787-555-0101 / 787-555-0202", "7875550101", true)]
    [InlineData("7875550101 7875550202", "7875550101", true)]
    [InlineData("787-555-0101, (939)555-0303", "7875550101", true)]
    [InlineData("555-0101", "555-0101", false)]   // sin 10 dígitos: queda igual (lo decide la regla del ContactPoint)
    [InlineData("abc", "abc", false)]
    public void Phones_in_mixed_formats_keep_ten_digits_and_two_numbers_take_the_first(string raw, string expected, bool multiple)
    {
        Assert.Equal((expected, multiple), ConsigneeImportMapping.NormalizePhone(raw));
        Assert.Equal("La celda del teléfono trae más de un número; se tomó el primero (7875550101).", ConsigneeImportMapping.MultiplePhonesWarning("7875550101"));
    }

    [Fact]
    public void Contact_name_accepts_a_default_and_has_a_150_limit()
    {
        Assert.Empty(Validate(Cols("name", "line1", "city"), new Dictionary<string, string> { ["contactName"] = "Recepción" }));
        Assert.Equal("Máximo 150 caracteres.", Validate(Cols("name", "line1", "city"), new Dictionary<string, string> { ["contactName"] = new string('x', 151) })["defaults.contactName"]);
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

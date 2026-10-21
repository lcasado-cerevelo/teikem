using System.Text;
using System.Text.Json.Nodes;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 137 (P11): formato del archivo de una integración — hoja de Excel, y JSON y XML con mapeo por ruta (rutas, líneas, orden y documento, límites, DTD) convertidos al CSV de la plantilla.
public sealed class FileFormatRulesTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static FileFormatConfig Cfg(string kind, string records, string? lines, params (string H, string P)[] cols)
        => new(kind, null, records, lines, cols.Select(c => new FormatColumn(c.H, c.P)).ToList());

    [Theory]
    [InlineData("$", true)]
    [InlineData("$.a", true)]
    [InlineData("$.a.b[0].c", true)]
    [InlineData("$.orders[*]", true)]
    [InlineData("@.lines[*]", true)]
    [InlineData("^.ref", true)]
    [InlineData("$.a:b.@id.#text", true)]
    [InlineData("a.b", false)]
    [InlineData("$a", false)]
    [InlineData("$.", false)]
    [InlineData("$..a", false)]
    [InlineData("$.a[", false)]
    [InlineData("$.a[x]", false)]
    [InlineData("$.a[1234567]", false)]
    [InlineData("", false)]
    public void A_path_is_valid_only_in_the_dollar_at_caret_dot_name_index_wildcard_form(string path, bool valid) => Assert.Equal(valid, FileFormatRules.ParsePath(path) is not null);

    [Fact]
    public void Wildcard_and_index_zero_on_a_single_item_take_the_item_so_one_path_serves_one_or_many()
    {
        var one = JsonNode.Parse("{\"o\":{\"a\":1}}");
        var many = JsonNode.Parse("{\"o\":[{\"a\":1},{\"a\":2}]}");
        Assert.Single(FileFormatRules.Evaluate(one, FileFormatRules.ParsePath("$.o[*]")!));
        Assert.Equal(2, FileFormatRules.Evaluate(many, FileFormatRules.ParsePath("$.o[*]")!).Count);
        Assert.Single(FileFormatRules.Evaluate(one, FileFormatRules.ParsePath("$.o[0]")!));
        Assert.Empty(FileFormatRules.Evaluate(one, FileFormatRules.ParsePath("$.o[1]")!));
        Assert.Empty(FileFormatRules.Evaluate(one, FileFormatRules.ParsePath("$.zzz")!));
    }

    [Theory]
    [InlineData("{\"v\":\"texto\"}", "texto")]
    [InlineData("{\"v\":12.5}", "12.5")]
    [InlineData("{\"v\":true}", "true")]
    [InlineData("{\"v\":null}", "")]
    [InlineData("{\"v\":{\"x\":1}}", "")]
    [InlineData("{\"v\":[1,2]}", "")]
    [InlineData("{}", "")]
    public void A_value_is_read_as_invariant_text_and_objects_and_lists_are_empty(string json, string expected)
        => Assert.Equal(expected, FileFormatRules.ScalarText(FileFormatRules.Evaluate(JsonNode.Parse(json), FileFormatRules.ParsePath("$.v")!).FirstOrDefault()));

    private const string OrdersJson = """
        { "customer": "ACME", "orders": [
            { "ref": "A-1", "to": {"name": "Ana", "zip": "00901"}, "lines": [ {"desc": "Caja", "qty": 2}, {"desc": "Bolsa", "qty": 1} ] },
            { "ref": "A-2", "to": {"name": "Beto, Jr.", "zip": "00902"}, "lines": [] },
            { "ref": "A-3", "to": {"name": "Cora \"C\"", "zip": "00903"} }
        ] }
        """;

    [Fact]
    public void Json_with_lines_gives_one_row_per_line_with_order_and_document_columns_and_a_row_for_orders_without_lines()
    {
        var cfg = Cfg("JSON", "$.orders[*]", "@.lines[*]", ("Ref", "^.ref"), ("Cliente", "$.customer"), ("Nombre", "^.to.name"), ("Zip", "^.to.zip"), ("Descripción", "@.desc"), ("Cantidad", "@.qty"));
        var table = FileFormatRules.ToTable(Utf8(OrdersJson), "JSON", cfg, out var error);
        Assert.Null(error);
        Assert.NotNull(table);
        Assert.Equal(["Ref", "Cliente", "Nombre", "Zip", "Descripción", "Cantidad"], table.Header);
        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(["A-1", "ACME", "Ana", "00901", "Caja", "2"], table.Rows[0]);
        Assert.Equal(["A-1", "ACME", "Ana", "00901", "Bolsa", "1"], table.Rows[1]);
        Assert.Equal(["A-2", "ACME", "Beto, Jr.", "00902", "", ""], table.Rows[2]);   // sin líneas: una fila con lo de la línea vacío
        Assert.Equal(["A-3", "ACME", "Cora \"C\"", "00903", "", ""], table.Rows[3]);
    }

    [Fact]
    public void Json_without_lines_reads_each_order_as_the_current_record()
    {
        var cfg = Cfg("JSON", "$.orders[*]", null, ("Ref", "@.ref"), ("Zip", "@.to.zip"));
        var table = FileFormatRules.ToTable(Utf8(OrdersJson), "JSON", cfg, out _)!;
        Assert.Equal([["A-1", "00901"], ["A-2", "00902"], ["A-3", "00903"]], table.Rows.Select(r => r.ToArray()).ToArray());
    }

    [Fact]
    public void Csv_text_quotes_delimiters_quotes_and_line_breaks_so_the_template_reads_the_same_cells()
    {
        var cfg = Cfg("JSON", "$.orders[*]", null, ("Ref", "@.ref"), ("Nombre", "@.to.name"));
        var table = FileFormatRules.ToTable(Utf8(OrdersJson), "JSON", cfg, out _)!;
        var csv = FileFormatRules.ToCsv(table.Header, table.Rows, ',');
        var parsed = CsvParser.Parse(csv, ',', hasHeader: true).ToList();
        Assert.Equal(3, parsed.Count);
        Assert.Equal(["A-2", "Beto, Jr."], parsed[1].Cells);
        Assert.Equal(["A-3", "Cora \"C\""], parsed[2].Cells);
        Assert.StartsWith("Ref,Nombre\n", csv);
        Assert.DoesNotContain("Ref", FileFormatRules.ToCsv(null, table.Rows, ','));
    }

    private const string OrdersXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Pedidos cliente="ACME">
          <Orden id="X-1">
            <Destino><Nombre>Ana</Nombre></Destino>
            <Linea cod="C1">Caja</Linea>
            <Linea cod="C2">Bolsa</Linea>
          </Orden>
          <Orden id="X-2">
            <Destino><Nombre>Beto</Nombre></Destino>
            <Linea cod="C3">Sobre</Linea>
          </Orden>
        </Pedidos>
        """;

    [Fact]
    public void Xml_attributes_are_at_names_text_of_elements_with_attributes_is_hash_text_and_repeated_elements_are_lists()
    {
        var cfg = Cfg("XML", "$.Pedidos.Orden[*]", "@.Linea[*]", ("Orden", "^.@id"), ("Cliente", "$.Pedidos.@cliente"), ("Nombre", "^.Destino.Nombre"), ("Código", "@.@cod"), ("Descripción", "@.#text"));
        var table = FileFormatRules.ToTable(Utf8(OrdersXml), "XML", cfg, out var error)!;
        Assert.Null(error);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["X-1", "ACME", "Ana", "C1", "Caja"], table.Rows[0]);
        Assert.Equal(["X-1", "ACME", "Ana", "C2", "Bolsa"], table.Rows[1]);
        Assert.Equal(["X-2", "ACME", "Beto", "C3", "Sobre"], table.Rows[2]);   // una sola línea (objeto) se lee igual con [*]
    }

    [Fact]
    public void Xml_with_a_dtd_or_external_entities_is_refused_and_broken_files_say_so()
    {
        var cfg = Cfg("XML", "$.a[*]", null, ("A", "@.x"));
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><a><x>&e;</x></a>";
        Assert.Null(FileFormatRules.ToTable(Utf8(xxe), "XML", cfg, out var dtd));
        Assert.Equal(FileFormatRules.DtdMessage, dtd);
        Assert.Null(FileFormatRules.ToTable(Utf8("<a><x>"), "XML", cfg, out var broken));
        Assert.StartsWith("No se pudo leer el archivo XML: ", broken);
        Assert.Null(FileFormatRules.ToTable(Utf8("{no es json"), "JSON", Cfg("JSON", "$.a[*]", null, ("A", "@.x")), out var badJson));
        Assert.StartsWith("No se pudo leer el archivo JSON: ", badJson);
    }

    [Fact]
    public void A_mapping_that_finds_no_orders_a_missing_mapping_and_too_many_rows_give_their_exact_messages()
    {
        Assert.Null(FileFormatRules.ToTable(Utf8("{\"x\":1}"), "JSON", Cfg("JSON", "$.orders[*]", null, ("A", "@.x")), out var none));
        Assert.Equal("La ruta de los envíos «$.orders[*]» no encontró ningún registro en el archivo.", none);
        Assert.Null(FileFormatRules.ToTable(Utf8("{\"x\":1}"), "JSON", new FileFormatConfig(), out var unmapped));
        Assert.Equal("El archivo es JSON, pero la integración no tiene el mapeo de rutas: configure «Formato del archivo».", unmapped);
        var many = "[" + string.Join(",", Enumerable.Range(0, 6)) + "]";
        Assert.Null(FileFormatRules.ToTable(Utf8(many), "JSON", Cfg("JSON", "$[*]", null, ("N", "@")), out var limit, maxRows: 5));
        Assert.Equal(CsvParser.LimitMessage, limit);
        Assert.Null(FileFormatRules.ToTable(new byte[CsvParser.MaxBytes + 1], "JSON", Cfg("JSON", "$[*]", null, ("N", "@")), out var big));
        Assert.Equal(CsvParser.LimitMessage, big);
    }

    [Fact]
    public void The_format_is_validated_with_exact_messages()
    {
        string? First(FileFormatConfig c) => FileFormatRules.Validate(c).Values.FirstOrDefault();
        Assert.Empty(FileFormatRules.Validate(null));
        Assert.Empty(FileFormatRules.Validate(new FileFormatConfig("CSV")));
        Assert.Empty(FileFormatRules.Validate(new FileFormatConfig("EXCEL", "Hoja1")));
        Assert.Empty(FileFormatRules.Validate(new FileFormatConfig()));
        Assert.Equal("Formato desconocido: PDF. Use AUTO, CSV, EXCEL, JSON, XML.", First(new FileFormatConfig("PDF")));
        Assert.Equal("El nombre de la hoja no puede pasar de 31 caracteres.", First(new FileFormatConfig("EXCEL", new string('h', 32))));
        Assert.Equal(FileFormatRules.RecordsRequiredMessage, First(new FileFormatConfig("JSON")));
        Assert.Equal(FileFormatRules.RecordsRootMessage, First(Cfg("JSON", "@.orders[*]", null, ("A", "$.a"))));
        Assert.Equal(FileFormatRules.PathInvalidMessage("orders"), First(Cfg("JSON", "orders", null, ("A", "$.a"))));
        Assert.Equal(FileFormatRules.LinesRootMessage, First(Cfg("JSON", "$.o[*]", "$.lines", ("A", "$.a"))));
        Assert.Equal(FileFormatRules.ColumnsRequiredMessage, First(Cfg("JSON", "$.o[*]", null)));
        Assert.Equal(FileFormatRules.HeaderRequiredMessage, First(Cfg("JSON", "$.o[*]", null, (" ", "$.a"))));
        Assert.Equal("Hay dos columnas llamadas «ref».", First(Cfg("JSON", "$.o[*]", null, ("Ref", "$.a"), ("ref", "$.b"))));
        Assert.Equal(FileFormatRules.PathInvalidMessage("a.b"), First(Cfg("JSON", "$.o[*]", null, ("A", "a.b"))));
        Assert.Equal(FileFormatRules.OrderRootMessage("^.x"), First(Cfg("JSON", "$.o[*]", null, ("A", "^.x"))));
        Assert.Equal(FileFormatRules.ColumnsMaxMessage, First(new FileFormatConfig("JSON", null, "$.o[*]", null, Enumerable.Range(0, 61).Select(i => new FormatColumn("c" + i, "$.a")).ToList())));
        Assert.Empty(FileFormatRules.Validate(Cfg("XML", "$.p.o[*]", "@.l[*]", ("A", "^.@id"), ("B", "@.#text"), ("C", "$.p.@c"))));
        // AUTO solo valida el mapeo si lo trae
        Assert.Equal(FileFormatRules.RecordsRequiredMessage, First(new FileFormatConfig("AUTO", null, null, null, [new FormatColumn("A", "$.a")])));
    }

    [Theory]
    [InlineData("AUTO", "pedidos.xlsx", "EXCEL")]
    [InlineData("AUTO", "pedidos.JSON", "JSON")]
    [InlineData("AUTO", "pedidos.xml", "XML")]
    [InlineData("AUTO", "pedidos.csv", "CSV")]
    [InlineData("AUTO", "pedidos.txt", "CSV")]
    [InlineData("AUTO", "sin-extension", "CSV")]
    [InlineData("JSON", "pedidos.txt", "JSON")]
    [InlineData("csv", "pedidos.json", "CSV")]
    public void The_format_comes_from_the_configuration_or_from_the_extension(string kind, string name, string expected)
        => Assert.Equal(expected, FileFormatRules.Resolve(new FileFormatConfig(kind), name, []));

    [Fact]
    public void A_zip_signature_means_excel_in_auto_and_the_folder_extensions_follow_the_format()
    {
        Assert.Equal("EXCEL", FileFormatRules.Resolve(null, "raro.bin", [0x50, 0x4B, 0x03, 0x04]));
        Assert.Equal([".csv", ".txt"], FileFormatRules.ExtensionsOf(new FileFormatConfig("CSV")));
        Assert.Equal([".xlsx"], FileFormatRules.ExtensionsOf(new FileFormatConfig("EXCEL")));
        Assert.Equal([".json"], FileFormatRules.ExtensionsOf(new FileFormatConfig("JSON")));
        Assert.Equal([".xml"], FileFormatRules.ExtensionsOf(new FileFormatConfig("XML")));
        Assert.Equal(5, FileFormatRules.ExtensionsOf(null).Count);
    }

    private static ImportTemplate Template(bool header = true, string delimiter = ",") => new() { HasHeader = header, Delimiter = delimiter };

    [Fact]
    public void An_excel_sheet_is_converted_to_the_csv_of_the_template_choosing_the_sheet_by_name()
    {
        var book = Xlsx.Build(("Resumen", [["x"]]), ("Pedidos", [["Ref", "Nombre"], ["A-1", "Ana, M."], ["A-2", "Beto"]]));
        var converted = FileFormatConverter.Convert(book, "pedidos.xlsx", new FileFormatConfig("EXCEL", "pedidos"), Template())!;
        Assert.Equal("EXCEL", converted.Kind);
        Assert.Equal(2, converted.Rows);
        var parsed = CsvParser.Parse(converted.Content, ',', hasHeader: true).ToList();
        Assert.Equal(["A-1", "Ana, M."], parsed[0].Cells);
        // sin nombre de hoja, la primera
        Assert.Equal(["x"], CsvParser.Parse(FileFormatConverter.Convert(book, "pedidos.xlsx", null, Template(header: false))!.Content, ',', hasHeader: false).First().Cells);
        // hoja inexistente y archivo que no es un libro
        var missing = Assert.Throws<ValidationException>(() => FileFormatConverter.Convert(book, "pedidos.xlsx", new FileFormatConfig("EXCEL", "Nada"), Template()));
        Assert.Equal(ImportFileReader.SheetNotFoundMessage("Nada"), missing.Errors!.Values.Single()[0]);
        Assert.Equal(FileFormatConverter.NotExcelMessage, Assert.Throws<ValidationException>(() => FileFormatConverter.Convert(Utf8("a,b"), "pedidos.txt", new FileFormatConfig("EXCEL"), Template())).Errors!["file"][0]);
    }

    [Fact]
    public void Json_and_xml_go_through_the_template_delimiter_and_a_csv_is_left_alone()
    {
        var cfg = Cfg("JSON", "$.orders[*]", null, ("Ref", "@.ref"), ("Nombre", "@.to.name"));
        var semicolon = FileFormatConverter.Convert(Utf8(OrdersJson), "p.json", cfg, Template(delimiter: ";"))!;
        Assert.StartsWith("Ref;Nombre\n", semicolon.Content);
        Assert.Contains("A-2;Beto, Jr.\n", semicolon.Content);   // con «;» la coma del texto no necesita comillas
        Assert.Null(FileFormatConverter.Convert(Utf8("a,b\n1,2"), "p.csv", null, Template()));
        Assert.Equal(FileFormatRules.StructuredWithoutMappingMessage("JSON"), Assert.Throws<ValidationException>(() => FileFormatConverter.Convert(Utf8("{}"), "p.json", null, Template())).Errors!["file"][0]);
    }
}

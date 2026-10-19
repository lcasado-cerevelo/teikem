using System.Text.Json;
using System.Xml.Linq;
using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

// Lote 137b (P11): reglas puras de la salida de órdenes — validación con mensajes exactos, nombre del archivo, armado en CSV, JSON y XML, la API saliente (URL, método, autenticación, encabezados) y los alcances.
public sealed class OutputRulesTests
{
    private static OutputConfig Cfg(string format = "CSV", string trigger = "EVENT", string? delimiter = ",", string? fileName = null, HttpOutConfig? http = null, IReadOnlyList<OutputColumn>? columns = null, params string[] statuses)
        => new(statuses.Length == 0 ? ["DELIVERED"] : statuses, trigger, format, delimiter, fileName, columns ?? [new("Orden", "orderNumber"), new("Entregada", "deliveredAt"), new("Piezas", "pieces")], http);

    private static string? First(OutputConfig? c, bool remote = true, bool http = false, bool insecure = false, bool secret = false)
        => OutputRules.Validate(c, remote, http, insecure, secret).Values.FirstOrDefault();

    [Fact]
    public void A_valid_configuration_passes_and_the_message_of_each_problem_is_exact()
    {
        Assert.Null(First(Cfg()));
        Assert.Equal(OutputRules.OutputRequiredMessage, First(null));
        Assert.Equal("Escoja al menos un estatus que dispare la salida.", First(Cfg() with { Statuses = [] }));
        Assert.Equal("Estatus desconocido: DONE.", First(Cfg(statuses: "DONE")));
        Assert.Equal("Disparador desconocido: DAILY. Use EVENT o SCHEDULE.", First(Cfg(trigger: "DAILY")));
        Assert.Equal("Formato de salida desconocido: PDF. Use CSV, JSON o XML.", First(Cfg(format: "PDF")));
        Assert.Equal("El delimitador debe ser coma, punto y coma o tabulador.", First(Cfg(delimiter: "|")));
        Assert.Equal("Agregue al menos una columna a la salida.", First(Cfg(columns: [])));
        Assert.Equal("Cada columna necesita un nombre.", First(Cfg(columns: [new(" ", "orderNumber")])));
        Assert.Equal("Hay dos columnas llamadas «a».", First(Cfg(columns: [new("A", "orderNumber"), new("a", "status")])));
        Assert.Equal("Campo desconocido: foo.", First(Cfg(columns: [new("A", "foo")])));
        Assert.Equal(OutputRules.ColumnsMaxMessage, First(Cfg(columns: Enumerable.Range(0, 41).Select(i => new OutputColumn("c" + i, "orderNumber")).ToList())));
    }

    [Fact]
    public void Json_and_xml_need_names_that_work_as_fields_while_csv_accepts_any_header()
    {
        var spaced = new OutputColumn("Número de orden", "orderNumber");
        Assert.Null(First(Cfg("CSV", columns: [spaced])));
        Assert.Equal("El nombre de la columna «Número de orden» debe empezar con una letra o guion bajo y llevar solo letras, números, guion y guion bajo (para JSON y XML).", First(Cfg("JSON", columns: [spaced])));
        Assert.Equal(OutputRules.HeaderNameMessage("1x"), First(Cfg("XML", columns: [new("1x", "status")])));
        Assert.Null(First(Cfg("XML", columns: [new("order_number", "orderNumber"), new("estatus-1", "status")])));
    }

    [Fact]
    public void The_file_name_pattern_is_checked_only_for_files_and_expands_its_markers()
    {
        Assert.Null(First(Cfg(fileName: "entregas-{date}-{id}.{ext}")));
        Assert.Equal(OutputRules.FileNameCharsMessage, First(Cfg(fileName: "../x.csv")));
        Assert.Equal(OutputRules.FileNameCharsMessage, First(Cfg(fileName: "a/b.csv")));
        Assert.Equal("Marcador desconocido en el nombre del archivo: {fecha}. Use {date}, {time}, {id} o {ext}.", First(Cfg(fileName: "x-{fecha}.csv")));
        Assert.Equal(OutputRules.FileNameMaxMessage, First(Cfg(fileName: new string('a', 121))));
        Assert.Null(First(Cfg(fileName: "../x.csv"), remote: false));   // en HTTP no hay nombre de archivo
        var at = new DateTime(2026, 10, 19, 14, 5, 9, DateTimeKind.Utc);
        Assert.Equal("ordenes-20261019-140509.json", OutputRules.ExpandFileName(null, "JSON", at, 7));
        Assert.Equal("e-20261019-7.xml", OutputRules.ExpandFileName("e-{date}-{id}.{ext}", "xml", at, 7));
        Assert.Equal("fijo.csv", OutputRules.ExpandFileName("fijo.csv", "CSV", at, 7));
    }

    [Theory]
    [InlineData(",", ',')]
    [InlineData(";", ';')]
    [InlineData("\t", '\t')]
    [InlineData("tab", '\t')]
    [InlineData(null, ',')]
    public void The_csv_delimiter_is_comma_semicolon_or_tab(string? text, char expected) => Assert.Equal(expected, OutputRules.DelimiterOf(text));

    [Fact]
    public void The_api_configuration_validates_url_method_authentication_and_headers()
    {
        HttpOutConfig H(string url = "https://api.cliente.com/entregas", string method = "POST", string auth = "NONE", string? name = null, params HttpHeaderItem[] headers) => new(url, method, auth, name, headers);
        string? Http(HttpOutConfig? h, bool secret = false, bool insecure = false) => First(Cfg(http: h), remote: false, http: true, insecure: insecure, secret: secret);
        Assert.Null(Http(H()));
        Assert.Equal(OutputRules.HttpRequiredMessage, Http(null));
        Assert.Equal(WebhookRules.UrlHttpsMessage, Http(H("http://api.cliente.com/x")));
        Assert.Equal(WebhookRules.UrlInternalMessage, Http(H("https://10.0.0.5/x")));
        Assert.Null(Http(H("http://127.0.0.1:5999/x"), insecure: true));
        Assert.Equal("Método desconocido: DELETE. Use POST o PUT.", Http(H(method: "DELETE")));
        Assert.Equal("Autenticación desconocida: OAUTH. Use NONE, BEARER, BASIC o HEADER.", Http(H(auth: "OAUTH")));
        Assert.Equal(OutputRules.AuthNameRequiredMessage, Http(H(auth: "BASIC"), secret: true));
        Assert.Equal(OutputRules.AuthNameRequiredMessage, Http(H(auth: "HEADER"), secret: true));
        Assert.Equal(OutputRules.AuthNameInvalidMessage, Http(H(auth: "HEADER", name: "Authorization"), secret: true));
        Assert.Equal(OutputRules.AuthSecretRequiredMessage, Http(H(auth: "BEARER")));
        Assert.Null(Http(H(auth: "BEARER"), secret: true));
        Assert.Null(Http(H(auth: "BASIC", name: "usuario"), secret: true));
        Assert.Null(Http(H(auth: "HEADER", name: "X-Api-Key"), secret: true));
        Assert.Equal(OutputRules.HeaderInvalidMessage("Host"), Http(H(headers: new HttpHeaderItem("Host", "x"))));
        Assert.Equal(OutputRules.HeaderInvalidMessage("X-A"), Http(H(headers: new HttpHeaderItem("X-A", "uno\r\ndos"))));
        Assert.Equal(OutputRules.HeadersMaxMessage, Http(H(headers: Enumerable.Range(0, 11).Select(i => new HttpHeaderItem("X-H" + i, "v")).ToArray())));
        Assert.Null(Http(H(headers: [new HttpHeaderItem("X-Cliente", "acme"), new HttpHeaderItem("X-Version", "2")])));
    }

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> Orders =
    [
        new Dictionary<string, object?> { ["orderNumber"] = "A-1", ["deliveredAt"] = new DateTime(2026, 10, 19, 14, 3, 11, DateTimeKind.Utc), ["pieces"] = 3, ["recipientName"] = "Ana, \"la jefa\"", ["codAmount"] = 12.5m, ["notes"] = null },
        new Dictionary<string, object?> { ["orderNumber"] = "A-2", ["deliveredAt"] = null, ["pieces"] = null, ["recipientName"] = "Beto & Co <b>", ["codAmount"] = 0m, ["notes"] = "línea1\nlínea2" },
    ];

    [Fact]
    public void Csv_quotes_what_needs_it_and_writes_dates_and_numbers_without_culture()
    {
        var cfg = Cfg("CSV", columns: [new("Orden", "orderNumber"), new("Entregada", "deliveredAt"), new("Recibió", "recipientName"), new("COD", "codAmount"), new("Notas", "notes")]);
        var csv = OutputRules.Render(cfg, Orders);
        Assert.Equal("Orden,Entregada,Recibió,COD,Notas\r\nA-1,2026-10-19T14:03:11Z,\"Ana, \"\"la jefa\"\"\",12.5,\r\nA-2,,Beto & Co <b>,0,\"línea1\nlínea2\"\r\n", csv);
        Assert.Equal("Orden;Piezas\r\nA-1;3\r\nA-2;\r\n", OutputRules.Render(Cfg("CSV", delimiter: ";", columns: [new("Orden", "orderNumber"), new("Piezas", "pieces")]), Orders));
        Assert.StartsWith("Orden\tPiezas\r\n", OutputRules.Render(Cfg("CSV", delimiter: "\t", columns: [new("Orden", "orderNumber"), new("Piezas", "pieces")]), Orders));
    }

    [Fact]
    public void Json_keeps_numbers_as_numbers_dates_as_iso_text_and_missing_values_as_null()
    {
        var cfg = Cfg("JSON", columns: [new("orden", "orderNumber"), new("entregada", "deliveredAt"), new("piezas", "pieces"), new("cod", "codAmount"), new("notas", "notes")]);
        using var doc = JsonDocument.Parse(OutputRules.Render(cfg, Orders));
        var items = doc.RootElement.GetProperty("orders").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("A-1", items[0].GetProperty("orden").GetString());
        Assert.Equal("2026-10-19T14:03:11Z", items[0].GetProperty("entregada").GetString());
        Assert.Equal(3, items[0].GetProperty("piezas").GetInt32());
        Assert.Equal(12.5m, items[0].GetProperty("cod").GetDecimal());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("notas").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("entregada").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("piezas").ValueKind);
    }

    [Fact]
    public void Xml_escapes_text_and_drops_characters_xml_cannot_carry()
    {
        var withControl = new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { ["orderNumber"] = "A-3", ["recipientName"] = "Cora\u0001 <x> & \"y\"" } };
        var xml = OutputRules.Render(Cfg("XML", columns: [new("orden", "orderNumber"), new("recibio", "recipientName")]), withControl);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml.TrimStart('﻿'));
        var doc = XDocument.Parse(xml);
        Assert.Equal("A-3", doc.Root!.Element("order")!.Element("orden")!.Value);
        Assert.Equal("Cora <x> & \"y\"", doc.Root.Element("order")!.Element("recibio")!.Value);
        var two = XDocument.Parse(OutputRules.Render(Cfg("XML", columns: [new("orden", "orderNumber")]), Orders));
        Assert.Equal(["A-1", "A-2"], two.Root!.Elements("order").Select(e => e.Element("orden")!.Value).ToArray());
    }

    [Fact]
    public void The_field_catalog_has_unique_codes_a_default_set_and_the_retries_are_the_webhook_ones()
    {
        Assert.Equal(OutputRules.Fields.Count, OutputRules.Fields.Select(f => f.Code).Distinct().Count());
        Assert.All(OutputRules.DefaultColumns, c => Assert.NotNull(OutputRules.FindField(c.Field)));
        Assert.NotNull(OutputRules.FindField(" ORDERNUMBER "));
        Assert.Null(OutputRules.FindField("zzz"));
        Assert.Contains("DELIVERED", OutputRules.OrderStatusCodes);
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddMinutes(1), OutputRules.NextAttemptUtc(1, now));
        Assert.Equal(now.AddHours(12), OutputRules.NextAttemptUtc(5, now));
        Assert.Null(OutputRules.NextAttemptUtc(6, now));
        Assert.Equal("text/csv; charset=utf-8", OutputRules.ContentTypeOf("CSV"));
        Assert.Equal("application/json", OutputRules.ContentTypeOf("json"));
        Assert.Equal("xml", OutputRules.ExtensionOf("XML"));
    }

    [Fact]
    public void The_export_scope_exists_for_the_files_family_and_needs_a_client()
    {
        var scope = IntegrationScopes.Find(IntegrationScopes.FilesOrdersExport);
        Assert.NotNull(scope);
        Assert.True(scope!.NeedsClient);
        Assert.Equal(IntegrationFamilies.Files, scope.Family);
        Assert.False(IntegrationScopes.IsApiScope(IntegrationScopes.FilesOrdersExport));
        Assert.Contains(IntegrationConnectorKinds.Http, IntegrationConnectorKinds.Outbound);
        Assert.DoesNotContain(IntegrationConnectorKinds.Email, IntegrationConnectorKinds.Outbound);
    }
}

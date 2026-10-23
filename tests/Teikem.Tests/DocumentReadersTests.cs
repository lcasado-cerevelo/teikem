using System.Text.Json;
using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-22 — lectores de una integración de correo: qué hace con cada documento que llega (cuerpo, PDF, archivo de datos) según sus condiciones, y a qué destino va.
/// </summary>
public class DocumentReadersTests
{
    private static readonly EmailBodyField[] Fields = { new(1, EmailBodyKinds.Label, Label: "Reference #") };

    private static DocumentReader Reader(string name, string source, params ReaderCondition[] when)
        => new(name, source, When: when, Fields: source == ReaderSources.File ? null : Fields);

    private static ReaderCondition Cond(string scope, string op, string value) => new(scope, op, value);

    private static ReaderInput Body(string subject, string text) => new(ReaderSources.Body, string.Empty, subject, text);
    private static ReaderInput Pdf(string name, string text, string subject = "Documentos") => new(ReaderSources.Pdf, name, subject, text);
    private static ReaderInput File(string name, string subject = "Documentos") => new(ReaderSources.File, name, subject, null);

    [Fact]
    public void A_reader_without_conditions_takes_everything_of_its_source()
    {
        var r = Reader("Pedidos", ReaderSources.Body);
        Assert.True(DocumentReaderRules.Matches(r, Body("Pedido", "Reference #: 1")));
        Assert.False(DocumentReaderRules.Matches(r, Pdf("a.pdf", "x")));
        Assert.False(DocumentReaderRules.Matches(r, File("a.csv")));
    }

    [Fact]
    public void All_the_conditions_must_hold_and_each_one_is_checked_on_its_own_thing()
    {
        var r = Reader("Packing", ReaderSources.Pdf, Cond(ReaderConditionScopes.AttachmentName, EmailMatchOperators.Contains, "packing"), Cond(ReaderConditionScopes.Text, EmailMatchOperators.Contains, "Product / Description"));
        Assert.True(DocumentReaderRules.Matches(r, Pdf("TRU-1_Packing List.pdf", "Ship To\nProduct / Description\tOrdered")));
        Assert.False(DocumentReaderRules.Matches(r, Pdf("Invoice_1.pdf", "Product / Description")));      // el nombre no coincide
        Assert.False(DocumentReaderRules.Matches(r, Pdf("Packing.pdf", "Invoice no.: 1")));                // el texto no coincide
    }

    [Fact]
    public void A_subject_condition_applies_to_every_source()
    {
        var r = Reader("Órdenes de Erica", ReaderSources.Body, Cond(ReaderConditionScopes.Subject, EmailMatchOperators.StartsWith, "Re: PR Orden"));
        Assert.True(DocumentReaderRules.Matches(r, Body("Re: PR Orden del Rep", "x")));
        Assert.False(DocumentReaderRules.Matches(r, Body("Factura", "x")));
    }

    [Fact]
    public void A_condition_on_something_the_document_does_not_have_never_matches()
    {
        // un cuerpo no tiene nombre de adjunto y un archivo de datos no tiene texto
        Assert.False(DocumentReaderRules.Matches(Reader("a", ReaderSources.Body, Cond(ReaderConditionScopes.AttachmentName, EmailMatchOperators.Contains, "x")), Body("s", "x")));
        Assert.False(DocumentReaderRules.Matches(Reader("b", ReaderSources.File, Cond(ReaderConditionScopes.Text, EmailMatchOperators.Contains, "x")), File("x.csv")));
    }

    [Fact]
    public void Each_document_goes_to_the_first_reader_that_takes_it_in_configured_order()
    {
        var readers = new[]
        {
            Reader("Packing list", ReaderSources.Pdf, Cond(ReaderConditionScopes.Text, EmailMatchOperators.Contains, "packing list")),
            Reader("Factura", ReaderSources.Pdf, Cond(ReaderConditionScopes.Text, EmailMatchOperators.Contains, "invoice")),
            Reader("Pedido", ReaderSources.Body),
            Reader("Datos", ReaderSources.File, Cond(ReaderConditionScopes.AttachmentName, EmailMatchOperators.Wildcard, "*.csv")),
        };
        var assigned = DocumentReaderRules.Assign(readers, new[]
        {
            Body("Pedido", "Reference #: 7"),
            Pdf("TRU-WH2271_Packing List.pdf", "PACKING LIST\nShip To"),
            Pdf("Invoice_TRUWH2276.pdf", "INVOICE\nBill to"),
            Pdf("manual.pdf", "Nada que ver"),
            File("ordenes.csv"),
            File("foto.png"),
        });
        Assert.Equal(new[] { "Pedido", "Packing list", "Factura", "Datos" }, assigned.Select(a => a.Reader.Name).ToArray());
    }

    [Fact]
    public void First_only_limits_a_reader_to_its_first_document()
    {
        var readers = new[] { Reader("Primero", ReaderSources.File) with { FirstOnly = true }, Reader("Resto", ReaderSources.File) };
        var assigned = DocumentReaderRules.Assign(readers, new[] { File("a.csv"), File("b.csv"), File("c.csv") });
        Assert.Equal(new[] { "Primero", "Resto", "Resto" }, assigned.Select(a => a.Reader.Name).ToArray());
    }

    [Fact]
    public void Validation_reports_each_problem_with_its_path()
    {
        var bad = new[]
        {
            new DocumentReader("", ReaderSources.Body, Fields: Fields),
            new DocumentReader("Dup", "XYZ", Fields: Fields),
            new DocumentReader("Dup", ReaderSources.Pdf, Target: "NOPE", Fields: null),
            new DocumentReader("Cuerpo con nombre", ReaderSources.Body, When: new[] { Cond(ReaderConditionScopes.AttachmentName, EmailMatchOperators.Contains, "x") }, Fields: Fields),
            new DocumentReader("Regex mala", ReaderSources.Pdf, When: new[] { Cond(ReaderConditionScopes.Text, EmailMatchOperators.Regex, "([") }, Fields: Fields),
        };
        var errors = DocumentReaderRules.Validate(bad);
        Assert.Equal(DocumentReaderRules.NameRequiredMessage, errors["email.readers[0].name"]);
        Assert.Equal(DocumentReaderRules.UnknownSourceMessage("XYZ"), errors["email.readers[1].source"]);
        Assert.Equal(DocumentReaderRules.DuplicateNameMessage("Dup"), errors["email.readers[2].name"]);
        Assert.Equal(DocumentReaderRules.UnknownTargetMessage("NOPE"), errors["email.readers[2].target"]);
        Assert.Equal(DocumentReaderRules.FieldsRequiredMessage, errors["email.readers[2].fields"]);
        Assert.Equal(DocumentReaderRules.ScopeNotForSourceMessage("ATTACHMENT_NAME", "BODY"), errors["email.readers[3].when[0].scope"]);
        Assert.Equal(EmailRules.BadRegexMessage("(["), errors["email.readers[4].when[0]"]);
    }

    [Fact]
    public void A_valid_set_of_readers_passes_and_is_part_of_the_email_config_validation()
    {
        var readers = new[]
        {
            Reader("Pedido", ReaderSources.Body, Cond(ReaderConditionScopes.Subject, EmailMatchOperators.Contains, "orden")),
            Reader("Datos", ReaderSources.File, Cond(ReaderConditionScopes.AttachmentName, EmailMatchOperators.Wildcard, "*.xlsx")),
        };
        Assert.Empty(DocumentReaderRules.Validate(readers));
        var cfg = new EmailConfig("h", 993, true, "u", Readers: new[] { Reader("", ReaderSources.Body) });
        Assert.Contains("email.readers[0].name", EmailRules.Validate(cfg, hasPassword: true).Keys);
        Assert.DoesNotContain(EmailRules.Validate(cfg with { Readers = readers }, hasPassword: true).Keys, k => k.StartsWith("email.readers"));
    }

    [Fact]
    public void Readers_survive_the_json_round_trip_of_the_email_config()
    {
        var id = Guid.NewGuid();
        var cfg = new EmailConfig("h", 993, true, "u", Readers: new[] { new DocumentReader("Pedido", ReaderSources.Body, When: new[] { Cond(ReaderConditionScopes.Subject, EmailMatchOperators.Contains, "orden") }, Fields: Fields, ClientPublicId: id) });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var back = JsonSerializer.Deserialize<EmailConfig>(JsonSerializer.Serialize(cfg, options), options)!;
        var r = Assert.Single(back.Readers!);
        Assert.Equal("Pedido", r.Name);
        Assert.Equal(DocumentTargets.Order, r.Target);
        Assert.Equal(id, r.ClientPublicId);
        Assert.Equal("orden", Assert.Single(r.When!).Value);
        Assert.Equal("Reference #", Assert.Single(r.Fields!).Label);
    }
}

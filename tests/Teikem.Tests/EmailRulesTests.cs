using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Security;
using Xunit;

namespace Teikem.Tests;

// Lote 133 (P11): integración por correo para cualquier cliente — remitente original, filtros, adjuntos por nombre y tipo, campos del cuerpo (etiqueta, expresión regular, bloque), validación y cifrado de la contraseña.
public sealed class EmailRulesTests
{
    private static MailMessageData Mail(string from = "rep@truss.com", string? replyTo = null, string body = "", string subject = "PR Orden 1", Dictionary<string, string>? headers = null, params MailAttachmentData[] attachments)
        => new("<id-1@x>", "7", null, from, replyTo, headers ?? new Dictionary<string, string>(), subject, body, attachments);

    private static MailAttachmentData Att(string name) => new(name, "text/csv", Encoding.UTF8.GetBytes("a,b"));

    private static EmailConfig Cfg(Action<Dictionary<string, object?>>? _ = null) => new("imap.x.com", 993, true, "ordenes@x.com");

    // ---------------------------------------------------------------- remitente original

    [Fact]
    public void The_original_sender_comes_from_the_headers_then_the_forwarded_block_then_the_from()
    {
        var withHeader = Mail(from: "dalyshamel@advance.com", headers: new() { ["X-Original-Sender"] = "Rep Truss <Rep@Truss.com>" });
        Assert.Equal("rep@truss.com", EmailRules.OriginalSender(withHeader));
        Assert.Equal("reply@truss.com", EmailRules.OriginalSender(Mail(from: "a@b.com", replyTo: "Reply <reply@truss.com>")));

        var forwarded = Mail(from: "dalyshamel@advance.com", body: "FYI\n\n---------- Mensaje reenviado ----------\nDe: Formsite <noreply@formsite.com>\nAsunto: PR Orden\n\nReference #: 1");
        Assert.Equal("noreply@formsite.com", EmailRules.OriginalSender(forwarded));
        Assert.Equal("a@b.com", EmailRules.OriginalSender(Mail(from: "A@B.com", body: "sin reenviado")));
        Assert.Equal("x@y.com", EmailRules.OriginalSender(Mail(from: "z@w.com", body: "> From: \"X\" <x@y.com>\n> Subject: hola")));
    }

    [Theory]
    [InlineData("rep@truss.com", "truss.com", true)]
    [InlineData("rep@mail.truss.com", "@truss.com", true)]      // subdominios
    [InlineData("rep@truss.com", "rep@truss.com", true)]
    [InlineData("otro@truss.com", "rep@truss.com", false)]
    [InlineData("rep@trussx.com", "truss.com", false)]
    [InlineData(null, "truss.com", false)]
    public void Allowed_senders_are_emails_or_domains(string? sender, string allowed, bool expected)
        => Assert.Equal(expected, EmailRules.SenderAllowed(sender, new[] { allowed }));

    [Fact]
    public void An_empty_allowed_list_means_anyone()
    {
        Assert.True(EmailRules.SenderAllowed("x@y.com", null));
        Assert.True(EmailRules.SenderAllowed(null, Array.Empty<string>()));
    }

    // ---------------------------------------------------------------- texto y adjuntos

    [Theory]
    [InlineData("EQUALS", "PR Orden 1", "pr orden 1", true)]
    [InlineData("CONTAINS", "Re: PR Orden 7", "orden", true)]
    [InlineData("STARTS", "PR Orden", "pr o", true)]
    [InlineData("ENDS", "PR Orden", "orden", true)]
    [InlineData("WILDCARD", "ordenes-2026.csv", "ordenes-*.csv", true)]
    [InlineData("WILDCARD", "ordenes-2026.xlsx", "ordenes-*.csv", false)]
    [InlineData("REGEX", "PO-1234", @"^PO-\d{4}$", true)]
    [InlineData("REGEX", "PO-12", @"^PO-\d{4}$", false)]
    [InlineData("CONTAINS", "Facturación", "facturacion", true)]   // sin acentos ni mayúsculas
    public void Text_matches_by_each_operator(string op, string text, string value, bool expected)
        => Assert.Equal(expected, EmailRules.TextMatches(text, new EmailTextMatch(op, value)));

    [Fact]
    public void Attachments_are_chosen_by_type_and_mode_and_the_rest_is_ignored()
    {
        var all = new[] { Att("a.csv"), Att("b.pdf"), Att("ordenes-1.csv"), Att("c.TXT") };
        var cfg = Cfg();
        Assert.Equal(new[] { "a.csv", "ordenes-1.csv", "c.TXT" }, EmailRules.SelectAttachments(all, cfg).Select(a => a.Name));
        Assert.Equal(new[] { "a.csv" }, EmailRules.SelectAttachments(all, cfg with { AttachmentMode = EmailAttachmentModes.First }).Select(a => a.Name));
        Assert.Equal(new[] { "ordenes-1.csv" }, EmailRules.SelectAttachments(all, cfg with { AttachmentMode = EmailAttachmentModes.ByName, AttachmentName = new EmailTextMatch("WILDCARD", "ordenes-*") }).Select(a => a.Name));
        Assert.Equal(new[] { "b.pdf" }, EmailRules.SelectAttachments(all, cfg with { AttachmentTypes = new[] { "pdf" } }).Select(a => a.Name));
    }

    [Fact]
    public void Html_is_reduced_to_text_without_scripts_and_with_rows_as_lines()
    {
        var html = "<html><style>p{color:red}</style><body><script>alert(1)</script><p>Reference #: <b>123</b></p><table><tr><td>Caja</td><td>2</td></tr><tr><td>Sobre</td><td>1</td></tr></table>a&nbsp;&amp;&nbsp;b</body></html>";
        var text = EmailRules.HtmlToText(html);
        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain("color:red", text);
        Assert.Contains("Reference #: 123", text);
        Assert.Contains("Caja\t2", text);
        Assert.Contains("a & b", text);
    }

    // ---------------------------------------------------------------- cuerpo

    private const string Formsite = """
        Notificación de Formsite
        Reference #: PR-1001
        Nombre: Colmado Don Pepe
        Teléfono: (787) 555-0100
        Dirección: Calle Sol 12
        Pueblo: Ponce
        Order Summary
        Caja grande    2    $1,250.50
        Sobre          1    $10.00
        Total: $1,260.50
        """;

    private static readonly EmailBodyField[] FormFields =
    [
        new(1, EmailBodyKinds.Label, Label: "Reference #", Required: true),
        new(2, EmailBodyKinds.Label, Label: "nombre"),
        new(3, EmailBodyKinds.Label, Label: "Teléfono", Transform: EmailTransforms.Digits),
        new(4, EmailBodyKinds.Label, Label: "Dirección"),
        new(5, EmailBodyKinds.Regex, Pattern: @"Pueblo:\s*(\w+)", Transform: EmailTransforms.Upper),
        new(6, EmailBodyKinds.Block, Start: "Order Summary", End: "Total", Separator: " ", Columns: new[] { new EmailBlockColumn(6, 1), new EmailBlockColumn(7, 2), new EmailBlockColumn(8, 3) }, Transform: EmailTransforms.None),
    ];

    [Fact]
    public void The_body_is_read_by_label_regex_and_block_with_transforms()
    {
        // el bloque con separador de espacios usa los huecos largos como separador de columnas
        var reading = EmailRules.ReadBody(Formsite, FormFields.Where(f => f.Kind != EmailBodyKinds.Block).ToArray());
        Assert.Empty(reading.Missing);
        Assert.Equal("PR-1001", reading.Single[1]);
        Assert.Equal("Colmado Don Pepe", reading.Single[2]);
        Assert.Equal("7875550100", reading.Single[3]);
        Assert.Equal("Calle Sol 12", reading.Single[4]);
        Assert.Equal("PONCE", reading.Single[5]);

        var withBlock = EmailRules.ReadBody(Formsite, FormFields);
        Assert.Equal(2, withBlock.Lines.Count);
        Assert.Equal(("Caja grande", "2", "$1,250.50"), (withBlock.Lines[0][6], withBlock.Lines[0][7], withBlock.Lines[0][8]));
        Assert.Equal("Sobre", withBlock.Lines[1][6]);
    }

    [Fact]
    public void A_required_field_that_is_missing_is_reported_and_money_is_cleaned()
    {
        var reading = EmailRules.ReadBody("Nombre: X", new[] { new EmailBodyField(1, EmailBodyKinds.Label, Label: "Reference #", Required: true) });
        Assert.Equal(new[] { EmailRules.RequiredFieldMissingMessage(1, "Reference #") }, reading.Missing);
        Assert.False(reading.HasData);
        Assert.Equal("1250.50", EmailRules.Transform("$1,250.50", EmailTransforms.Money));
        Assert.Equal("7875550100", EmailRules.Transform("(787) 555-0100", EmailTransforms.Digits));
    }

    [Fact]
    public void The_body_becomes_csv_rows_for_the_same_template_with_single_values_repeated_per_block_line()
    {
        var reading = EmailRules.ReadBody(Formsite, FormFields);
        var csv = EmailRules.ToCsv(reading, withHeader: true, ',').Replace("\r\n", "\n").TrimEnd().Split('\n');
        Assert.Equal("1,2,3,4,5,6,7,8", csv[0]);
        Assert.Equal("PR-1001,Colmado Don Pepe,7875550100,Calle Sol 12,PONCE,Caja grande,2,\"$1,250.50\"", csv[1]);
        Assert.Equal("PR-1001,Colmado Don Pepe,7875550100,Calle Sol 12,PONCE,Sobre,1,$10.00", csv[2]);
        var noBlock = EmailRules.ToCsv(EmailRules.ReadBody("Reference #: 9", new[] { new EmailBodyField(2, EmailBodyKinds.Label, Label: "Reference #") }), withHeader: false, ';').TrimEnd();
        Assert.Equal(";9", noBlock);
    }

    // ---------------------------------------------------------------- validación

    [Fact]
    public void The_configuration_is_validated_with_exact_messages()
    {
        Assert.Empty(EmailRules.Validate(new EmailConfig("imap.x.com", 993, true, "u@x.com", ReadBody: true, BodyFields: FormFields), hasPassword: true));
        var bad = EmailRules.Validate(new EmailConfig("", 0, true, "", "", null, new EmailTextMatch("REGEX", "("), new[] { "no es correo" }, false, "RARO", null, null, true,
            new[] { new EmailBodyField(0, "X"), new EmailBodyField(1, EmailBodyKinds.Block) }), hasPassword: false);
        Assert.Equal(EmailRules.HostRequiredMessage, bad["email.host"]);
        Assert.Equal(EmailRules.PortMessage, bad["email.port"]);
        Assert.Equal(EmailRules.UsernameRequiredMessage, bad["email.username"]);
        Assert.Equal(EmailRules.PasswordRequiredMessage, bad["email.password"]);
        Assert.Equal(EmailRules.MailboxRequiredMessage, bad["email.mailbox"]);
        Assert.Equal(EmailRules.BadRegexMessage("("), bad["email.subject"]);
        Assert.Equal(EmailRules.SenderFormatMessage("no es correo"), bad["email.allowedSenders"]);
        Assert.Equal(EmailRules.UnknownModeMessage("RARO"), bad["email.attachmentMode"]);
        Assert.Equal(EmailRules.PositionMessage, bad["email.bodyFields[0].position"]);
        Assert.Equal(EmailRules.UnknownKindMessage("X"), bad["email.bodyFields[0].kind"]);
        Assert.Equal(EmailRules.BlockMarkersMessage, bad["email.bodyFields[1].start"]);
        Assert.Equal(EmailRules.BlockColumnsMessage, bad["email.bodyFields[1].columns"]);
        Assert.Equal(EmailRules.NoBodyFieldsMessage, EmailRules.Validate(new EmailConfig("h", 1, false, "u", ReadBody: true), true)["email.bodyFields"]);
        Assert.Equal(EmailRules.NameFilterRequiredMessage, EmailRules.Validate(new EmailConfig("h", 1, false, "u", AttachmentMode: EmailAttachmentModes.ByName), true)["email.attachmentName"]);
    }

    // ---------------------------------------------------------------- MIME y secretos

    [Fact]
    public void An_eml_file_is_read_with_its_attachments_html_text_and_original_sender_headers()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Dalyshamel", "dalyshamel@advance.com"));
        message.To.Add(new MailboxAddress("Ordenes", "ordenes@advance.com"));
        message.Subject = "PR Orden";
        message.Headers.Add("X-Original-Sender", "rep@truss.com");
        message.MessageId = "abc@truss";
        var builder = new BodyBuilder { HtmlBody = "<p>Reference #: <b>77</b></p>" };
        builder.Attachments.Add("ordenes.csv", Encoding.UTF8.GetBytes("a,b\n1,2"), ContentType.Parse("text/csv"));
        message.Body = builder.ToMessageBody();
        using var ms = new MemoryStream();
        message.WriteTo(ms);

        var read = MailMessageMapper.FromEml(ms.ToArray());
        Assert.Equal("abc@truss", read.MessageId.Trim('<', '>'));
        Assert.Equal("PR Orden", read.Subject);
        Assert.Contains("Reference #: 77", read.TextBody);
        var attachment = Assert.Single(read.Attachments);
        Assert.Equal("ordenes.csv", attachment.Name);
        Assert.Equal("a,b\n1,2", Encoding.UTF8.GetString(attachment.Content).Replace("\r\n", "\n"));
        Assert.Equal("rep@truss.com", EmailRules.OriginalSender(read));
    }

    [Fact]
    public void Secrets_are_encrypted_per_company_and_purpose_and_do_not_open_with_another_key()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var protector = new SecretProtector(Options.Create(new FileStoreOptions { MasterKey = key }));
        var blob = protector.Protect(7, "mail", "contraseña-secreta");
        Assert.DoesNotContain("secreta", Encoding.UTF8.GetString(blob));
        Assert.Equal("contraseña-secreta", protector.Unprotect(7, "mail", blob));
        Assert.NotEqual(blob, protector.Protect(7, "mail", "contraseña-secreta"));   // nonce aleatorio
        Assert.Throws<InvalidDataException>(() => protector.Unprotect(8, "mail", blob));       // otra compañía
        Assert.Throws<InvalidDataException>(() => protector.Unprotect(7, "sftp", blob));       // otro propósito
        Assert.False(new SecretProtector(Options.Create(new FileStoreOptions())).IsConfigured);
        Assert.Throws<InvalidOperationException>(() => new SecretProtector(Options.Create(new FileStoreOptions())).Protect(1, "mail", "x"));
    }
}

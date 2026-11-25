using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

// Lote 243: motor de avisos con contenido armado al enviar. Reglas PURAS: Compose (HTML con bloques), RenderText, mensajes exactos y
// la revisión del catálogo (una plantilla por omisión con un bloque en el asunto es un error de programación).
public class NotificationContentTests
{
    private static readonly IReadOnlyDictionary<string, string> Values = new Dictionary<string, string> { ["orderNumber"] = "OV-00012", ["client"] = "Acme <SA>" };

    private static readonly IReadOnlyDictionary<string, NotificationBlock> Blocks = new Dictionary<string, NotificationBlock>
    {
        ["orderDetail"] = new("<table><tr><td>SKU-1</td></tr></table>", "3 líneas · Total $123.45"),
    };

    // ---- Compose ----

    [Theory]
    [InlineData("Se solicitó la orden {orderNumber} de {client}.\n\nGracias")]
    [InlineData("sin variables")]
    [InlineData("")]
    [InlineData(null)]
    public void Compose_without_blocks_is_exactly_the_paragraphs_of_the_rendered_template(string? template)
    {
        var today = NotificationHtml.Paragraphs(NotificationRules.Render(template, Values));
        Assert.Equal(today, NotificationHtml.Compose(template, Values, null));
        Assert.Equal(today, NotificationHtml.Compose(template, Values, new Dictionary<string, NotificationBlock>()));
        // con bloques que la plantilla NO usa, también idéntico
        Assert.Equal(today, NotificationHtml.Compose(template, Values, Blocks));
    }

    [Fact]
    public void Compose_inserts_the_block_html_and_encodes_the_text_around_it()
    {
        var html = NotificationHtml.Compose("Orden {orderNumber} de {client}.\n\n{orderDetail}\n\nGracias", Values, Blocks);
        Assert.Equal("<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Orden OV-00012 de Acme &lt;SA&gt;.</p><table><tr><td>SKU-1</td></tr></table><p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">Gracias</p>", html);
    }

    [Fact]
    public void Compose_escapes_a_script_in_a_variable_but_keeps_the_block_as_is()
    {
        var values = new Dictionary<string, string> { ["client"] = "<script>alert(1)</script>" };
        var html = NotificationHtml.Compose("{client} {orderDetail}", values, Blocks);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<table><tr><td>SKU-1</td></tr></table>", html);
    }

    [Fact]
    public void Compose_with_only_the_block_has_no_empty_paragraphs_and_a_missing_block_is_empty()
    {
        Assert.Equal("<table><tr><td>SKU-1</td></tr></table>", NotificationHtml.Compose("{orderDetail}", Values, Blocks));
        Assert.Equal("<table><tr><td>SKU-1</td></tr></table><table><tr><td>SKU-1</td></tr></table>", NotificationHtml.Compose("{orderDetail}\n{orderDetail}", Values, Blocks));
        // un bloque que no vino (el proveedor devolvió null): sale vacío, sin romper el resto
        var other = new Dictionary<string, NotificationBlock> { ["otro"] = new("<b>x</b>", "x") };
        Assert.Equal("<p>Orden OV-00012 </p>", NotificationHtml.Compose("Orden {orderNumber} {orderDetail}", Values, other));
    }

    // ---- RenderText ----

    [Fact]
    public void RenderText_uses_the_one_line_text_of_each_block()
    {
        Assert.Equal("Orden OV-00012: 3 líneas · Total $123.45", NotificationRules.RenderText("Orden {orderNumber}: {orderDetail}", Values, Blocks));
        Assert.Equal("Orden OV-00012: ", NotificationRules.RenderText("Orden {orderNumber}: {orderDetail}", Values, null));
        Assert.Equal(string.Empty, NotificationRules.RenderText(null, Values, Blocks));
        // RenderText NO codifica (es texto plano)
        Assert.Equal("Acme <SA>", NotificationRules.RenderText("{client}", Values, Blocks));
    }

    [Fact]
    public void Merge_lets_the_provider_win_and_UsesAny_detects_blocks()
    {
        var merged = NotificationRules.Merge(Values, new Dictionary<string, string> { ["client"] = "Otro", ["seller"] = "Ana" });
        Assert.Equal(("OV-00012", "Otro", "Ana"), (merged["orderNumber"], merged["client"], merged["seller"]));
        Assert.True(NotificationRules.UsesAny("x {orderDetail}", new[] { "orderDetail" }));
        Assert.False(NotificationRules.UsesAny("x {orderNumber}", new[] { "orderDetail" }));
        Assert.False(NotificationRules.UsesAny(null, new[] { "orderDetail" }));
    }

    // ---- mensajes exactos ----

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("La variable {orderDetail} es un bloque y solo puede ir en el texto, no en el asunto.", NotificationRules.BlockInSubjectMessage("orderDetail"));
        Assert.Equal("Este evento no tiene el destinatario 'FOO'.", NotificationRules.UnknownContextRecipientMessage("FOO"));
        Assert.Equal("Tipo de destinatario desconocido: 'X'. Use ROLE, USER, EMAIL, PHONE o CONTEXT.", NotificationRules.UnknownRecipientKindMessage("X"));
        Assert.Equal("Este evento no admite vista previa con un documento real.", NotificationRules.ReferenceNotSupportedMessage);
        Assert.Equal("Destinatario de la vista previa desconocido: 'X'. Use STAFF o CUSTOMER.", NotificationRules.UnknownAudienceMessage("X"));
        Assert.Equal("Idioma no válido: 'fr'. Use es o en.", NotificationRules.UnknownLangMessage("fr"));
    }

    [Theory]
    [InlineData("EMAIL", "«Vendedor de la orden» no tiene correo.")]
    [InlineData("SMS", "«Vendedor de la orden» no tiene teléfono.")]
    [InlineData("APP", "«Vendedor de la orden» no tiene usuario en Teikem.")]
    [InlineData("PUSH", "«Vendedor de la orden» no tiene usuario en Teikem.")]
    public void Unreachable_context_recipient_message_depends_on_the_channel(string channel, string expected)
        => Assert.Equal(expected, NotificationRules.ContextUnreachableMessage("Vendedor de la orden", channel));

    [Fact]
    public void The_log_recipient_of_a_context_recipient_is_its_short_label_in_parentheses()
    {
        Assert.Equal("(Vendedor de la orden)", NotificationRules.ContextLogRecipient("Vendedor de la orden"));
        Assert.Equal("(Cliente de la orden)", NotificationRules.ContextLogRecipient("Cliente de la orden (correo principal y contactos marcados)"));
        Assert.Equal("Order's customer", NotificationRules.ShortContextLabel("Order's customer (main email and flagged contacts)"));
    }

    [Fact]
    public void Context_kind_is_a_valid_recipient_kind()
    {
        Assert.Equal(NotificationRecipientKinds.Context, NotificationRules.NormalizeKind("context"));
        Assert.Contains("CONTEXT", NotificationRecipientKinds.All);
    }

    // ---- catálogo ----

    private sealed class OneEvent(NotificationEventDef e) : INotificationEventProvider
    {
        public IEnumerable<NotificationEventDef> Events { get; } = new[] { e };
    }

    private static NotificationEventDef Event(IReadOnlyList<NotificationChannelDefault>? defaults = null, IReadOnlyList<NotificationContextRecipientDef>? context = null)
        => new("CTX_PURE", null, "Prueba", "Test",
            new[] { new NotificationVariable("orderNumber", "Número", "Number", "OV-1"), new NotificationVariable("orderDetail", "Orden", "Order", "(tabla)", IsBlock: true) },
            false, null, "Asunto {orderNumber}", "Subject {orderNumber}", "Texto", "Text", context, defaults);

    [Fact]
    public void The_catalog_rejects_a_channel_default_with_a_block_in_the_subject()
    {
        var bad = Event(new[] { new NotificationChannelDefault("EMAIL", "Orden {orderDetail}", "Order", "{orderDetail}", "{orderDetail}") });
        Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new INotificationEventProvider[] { new OneEvent(bad) }));
        var ok = Event(new[] { new NotificationChannelDefault("EMAIL", "Orden {orderNumber}", "Order {orderNumber}", "{orderDetail}", "{orderDetail}") },
            new[] { new NotificationContextRecipientDef("SELLER", "Vendedor de la orden", "Order's sales rep", true) });
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new OneEvent(ok) });
        var e = catalog.Find("CTX_PURE")!;
        Assert.Equal(new[] { "orderDetail" }, e.BlockNames);
        Assert.Equal("SELLER", e.ContextRecipient("seller")!.Key);
        Assert.Null(e.ContextRecipient("FOO"));
    }

    [Fact]
    public void The_catalog_rejects_a_repeated_context_key()
    {
        var bad = Event(context: new[] { new NotificationContextRecipientDef("SELLER", "a", "a", true), new NotificationContextRecipientDef("SELLER", "b", "b", false) });
        Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new INotificationEventProvider[] { new OneEvent(bad) }));
    }

    // ---- canal de correo ----

    private sealed class RecordingEmail(bool local) : ITransactionalEmailSender
    {
        public EmailMessage? Last;
        public bool IsConfigured => true;
        public bool IsLocalSink => local;
        public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        {
            Last = new EmailMessage(toEmail, toName, subject, htmlContent, companyName);
            return Task.CompletedTask;
        }
        public Task SendAsync(EmailMessage message, CancellationToken ct) { Last = message; return Task.CompletedTask; }
    }

    [Fact]
    public async Task The_email_channel_sends_the_attachments_and_reports_the_local_sink()
    {
        var email = new RecordingEmail(local: true);
        var sender = new EmailChannelSender(email);
        Assert.True(sender.IsLocal);
        Assert.False(new EmailChannelSender(new RecordingEmail(local: false)).IsLocal);
        Assert.False(((INotificationChannelSender)new SmsChannelSender()).IsLocal);   // por omisión, ningún canal es local

        var att = new EmailAttachment("firma-OV-00012.png", "image/png", new byte[] { 1, 2, 3 });
        var outcome = await sender.SendAsync(new NotificationDelivery(1, "Compañía", "X", null, 1, null, " a@x.com ", null, "Asunto", "texto", "<p>texto</p>", null, false,
            new[] { att }), default);
        Assert.Equal("SENT", outcome.Status);
        Assert.Equal(("a@x.com", "Asunto", "<p>texto</p>", "Compañía"), (email.Last!.ToEmail, email.Last.Subject, email.Last.HtmlContent, email.Last.CompanyName));
        Assert.Equal(att, Assert.Single(email.Last.Attachments!));

        await sender.SendAsync(new NotificationDelivery(1, "Compañía", "X", null, 1, null, "a@x.com", null, "Asunto", "texto", "<p>texto</p>", null, false), default);
        Assert.Null(email.Last!.Attachments);   // sin adjuntos: nada
    }
}

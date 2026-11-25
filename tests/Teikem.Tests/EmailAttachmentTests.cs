using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G0) — correo con adjuntos y contratos del contenido de avisos. Brevo se prueba con un HttpMessageHandler falso:
/// nunca red ni llave real (la «llave» es un texto de prueba).
/// </summary>
public class EmailAttachmentTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<Uri?> Urls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created) { Content = new StringContent("{\"messageId\":\"x\"}") };
        }
    }

    /// <summary>Logger que guarda los mensajes ya formateados (para comprobar que nunca va el contenido de un adjunto).</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (BrevoEmailSender Sender, StubHandler Handler, ListLogger<BrevoEmailSender> Log) Brevo(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Brevo:ApiKey"] = "llave-de-prueba-no-real",
            ["Brevo:FromEmail"] = "teikem@example.test",
        };
        foreach (var kv in extra ?? new()) settings[kv.Key] = kv.Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var handler = new StubHandler();
        var log = new ListLogger<BrevoEmailSender>();
        return (new BrevoEmailSender(new HttpClient(handler), config, log), handler, log);
    }

    private static byte[] Bytes(int n, byte fill = 7) => Enumerable.Repeat(fill, n).ToArray();

    [Fact]
    public async Task Brevo_with_attachments_sends_attachment_array_in_base64()
    {
        var (sender, handler, _) = Brevo();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        await sender.SendAsync(new EmailMessage("vendedor@example.test", "Vendedor", "Orden OV-00012", "<p>hola</p>", "Solutions",
            new[] { new EmailAttachment("firma-OV-00012.png", "image/png", png) }), CancellationToken.None);

        Assert.Single(handler.Bodies);
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var att = doc.RootElement.GetProperty("attachment");
        Assert.Equal(1, att.GetArrayLength());
        Assert.Equal("firma-OV-00012.png", att[0].GetProperty("name").GetString());
        Assert.Equal(Convert.ToBase64String(png), att[0].GetProperty("content").GetString());
        Assert.Equal("Solutions", doc.RootElement.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("vendedor@example.test", doc.RootElement.GetProperty("to")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Brevo_without_attachments_json_is_identical_to_classic_send()
    {
        var (a, ha, _) = Brevo();
        var (b, hb, _) = Brevo();
        var (c, hc, _) = Brevo();
        await a.SendAsync("x@example.test", "X", "Asunto", "<p>cuerpo</p>", CancellationToken.None, "Compañía");
        await b.SendAsync(new EmailMessage("x@example.test", "X", "Asunto", "<p>cuerpo</p>", "Compañía"), CancellationToken.None);
        await c.SendAsync(new EmailMessage("x@example.test", "X", "Asunto", "<p>cuerpo</p>", "Compañía", Array.Empty<EmailAttachment>()), CancellationToken.None);

        Assert.Equal(ha.Bodies[0], hb.Bodies[0]);
        Assert.Equal(ha.Bodies[0], hc.Bodies[0]);
        Assert.DoesNotContain("attachment", ha.Bodies[0]);
        using var doc = JsonDocument.Parse(ha.Bodies[0]);
        Assert.Equal(new[] { "sender", "to", "subject", "htmlContent" }, doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Brevo_respects_test_redirect_with_attachments()
    {
        var (sender, handler, _) = Brevo(new() { ["Email:RedirectTo"] = "teikem@cerevelo.com", ["Email:PlusAddressing"] = "false", ["Hosting:EnvironmentLabel"] = "TEST" });
        await sender.SendAsync(new EmailMessage("cliente@suempresa.com", "Cliente", "Orden OV-1", "<p>x</p>", null,
            new[] { new EmailAttachment("firma.png", "image/png", Bytes(10)) }), CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("teikem@cerevelo.com", doc.RootElement.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.StartsWith("[TEST → cliente@suempresa.com]", doc.RootElement.GetProperty("subject").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("attachment").GetArrayLength());
    }

    [Fact]
    public async Task Brevo_omits_attachment_over_cap_and_sends_the_rest_logging_name_and_size_only()
    {
        var (sender, handler, log) = Brevo();
        var big = Bytes((int)EmailAttachmentRules.MaxTotalBytes + 1, 0x41);   // 'A' repetido
        await sender.SendAsync(new EmailMessage("x@example.test", null, "Asunto", "<p>x</p>", null, new[]
        {
            new EmailAttachment("grande.pdf", "application/pdf", big),
            new EmailAttachment("firma.png", "image/png", Bytes(3)),
        }), CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var att = doc.RootElement.GetProperty("attachment");
        Assert.Equal(1, att.GetArrayLength());
        Assert.Equal("firma.png", att[0].GetProperty("name").GetString());

        var warn = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("grande.pdf", warn.Message);
        Assert.Contains(big.Length.ToString(), warn.Message);
        Assert.DoesNotContain("AAAA", warn.Message);   // nunca el contenido (ni en base64: «QUFB…»)
        Assert.DoesNotContain("QUFB", warn.Message);
    }

    [Fact]
    public async Task Brevo_not_configured_sends_nothing_even_with_attachments()
    {
        var (sender, handler, _) = Brevo(new() { ["Brevo:ApiKey"] = "" });
        Assert.False(sender.IsConfigured);
        await sender.SendAsync(new EmailMessage("x@example.test", null, "A", "<p>x</p>", null, new[] { new EmailAttachment("f.png", "image/png", Bytes(2)) }), CancellationToken.None);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task Fake_sender_with_only_classic_send_gets_message_without_attachments()
    {
        var fake = new NoEmailSender();
        ITransactionalEmailSender sender = fake;
        await sender.SendAsync(new EmailMessage("a@example.test", "A", "Asunto", "<p>x</p>", "Compañía",
            new[] { new EmailAttachment("f.png", "image/png", Bytes(2)) }), CancellationToken.None);

        var sent = Assert.Single(fake.Sent);
        Assert.Equal(("a@example.test", "Asunto", "<p>x</p>"), sent);
        Assert.Equal("Compañía", Assert.Single(fake.Companies));
    }

    [Fact]
    public void IsLocalSink_is_false_by_default()
    {
        ITransactionalEmailSender fake = new NoEmailSender();
        Assert.False(fake.IsLocalSink);
        ITransactionalEmailSender brevo = Brevo().Sender;
        Assert.False(brevo.IsLocalSink);
    }

    // ---------------------------------------------------------------- EmailAttachmentRules.Fit

    [Fact]
    public void Fit_null_or_empty_keeps_nothing()
    {
        var (k1, o1) = EmailAttachmentRules.Fit(null);
        Assert.Empty(k1); Assert.Empty(o1);
        var (k2, o2) = EmailAttachmentRules.Fit(Array.Empty<EmailAttachment>());
        Assert.Empty(k2); Assert.Empty(o2);
    }

    [Fact]
    public void Fit_exactly_at_cap_is_kept_and_one_byte_over_is_omitted_in_order()
    {
        var half = (int)(EmailAttachmentRules.MaxTotalBytes / 2);
        var a = new EmailAttachment("a.bin", "application/octet-stream", Bytes(half));
        var b = new EmailAttachment("b.bin", "application/octet-stream", Bytes(half));   // a + b = tope exacto: cabe
        var c = new EmailAttachment("c.bin", "application/octet-stream", Bytes(1));      // ya no cabe
        var (kept, omitted) = EmailAttachmentRules.Fit(new[] { a, b, c });
        Assert.Equal(new[] { "a.bin", "b.bin" }, kept.Select(x => x.FileName));
        Assert.Equal(new[] { "c.bin" }, omitted.Select(x => x.FileName));
        Assert.Equal(5L * 1024 * 1024, EmailAttachmentRules.MaxTotalBytes);
    }

    [Fact]
    public void Fit_a_smaller_later_attachment_still_fits_after_a_big_one_is_omitted()
    {
        const int mb = 1024 * 1024;
        var first = new EmailAttachment("tres-a.bin", "application/octet-stream", Bytes(3 * mb));
        var second = new EmailAttachment("tres-b.bin", "application/octet-stream", Bytes(3 * mb));   // 6 MB: no cabe
        var third = new EmailAttachment("uno.bin", "application/octet-stream", Bytes(1 * mb));       // 4 MB: cabe
        var (kept, omitted) = EmailAttachmentRules.Fit(new[] { first, second, third });
        Assert.Equal(new[] { "tres-a.bin", "uno.bin" }, kept.Select(x => x.FileName));
        Assert.Equal(new[] { "tres-b.bin" }, omitted.Select(x => x.FileName));
    }

    [Fact]
    public void Fit_omits_empty_content_or_nameless_attachments()
    {
        var (kept, omitted) = EmailAttachmentRules.Fit(new[]
        {
            new EmailAttachment("vacio.png", "image/png", Array.Empty<byte>()),
            new EmailAttachment(" ", "image/png", Bytes(1)),
            new EmailAttachment("ok.png", "image/png", Bytes(1)),
        });
        Assert.Equal(new[] { "ok.png" }, kept.Select(x => x.FileName));
        Assert.Equal(2, omitted.Count);
    }

    // ---------------------------------------------------------------- contratos del contenido y catálogo de eventos

    [Fact]
    public void Recipient_kinds_include_context()
    {
        Assert.Equal("CONTEXT", NotificationRecipientKinds.Context);
        Assert.Contains("CONTEXT", NotificationRecipientKinds.All);
    }

    [Theory]
    [InlineData(null, NotificationAudience.Staff)]
    [InlineData("", NotificationAudience.Staff)]
    [InlineData("staff", NotificationAudience.Staff)]
    [InlineData(" CUSTOMER ", NotificationAudience.Customer)]
    public void Audience_codes_parse(string? code, NotificationAudience expected)
        => Assert.Equal(expected, NotificationAudienceCodes.Parse(code));

    [Fact]
    public void Audience_codes_reject_unknown_and_round_trip()
    {
        Assert.Null(NotificationAudienceCodes.Parse("CLIENTE"));
        Assert.Equal("STAFF", NotificationAudienceCodes.ToCode(NotificationAudience.Staff));
        Assert.Equal("CUSTOMER", NotificationAudienceCodes.ToCode(NotificationAudience.Customer));
    }

    [Fact]
    public void Content_skip_carries_reason_and_nothing_else()
    {
        var c = NotificationContent.Skip("La orden OV-00012 se canceló antes de enviar el aviso.");
        Assert.Equal("La orden OV-00012 se canceló antes de enviar el aviso.", c.SkipReason);
        Assert.Empty(c.Values); Assert.Empty(c.Blocks); Assert.Empty(c.Attachments);
        Assert.Empty(NotificationContextResolution.Empty("sin nadie").People);
    }

    private sealed class OneEvent(NotificationEventDef def) : INotificationEventProvider
    {
        public IEnumerable<NotificationEventDef> Events { get; } = new[] { def };
    }

    private static NotificationEventDef Def(
        IReadOnlyList<NotificationContextRecipientDef>? recipients = null,
        IReadOnlyList<NotificationChannelDefault>? defaults = null)
        => new("X_EVENT", null, "Evento", "Event",
            new[]
            {
                new NotificationVariable("orderNumber", "Número", "Number", "OV-00012"),
                new NotificationVariable("orderDetail", "La orden completa (tabla)", "Full order (table)", "…", IsBlock: true),
            },
            IsOptional: false, LinkTemplate: null, "Asunto", "Subject", "Texto", "Text", recipients, defaults);

    [Fact]
    public void Catalog_accepts_event_with_context_recipients_and_channel_defaults()
    {
        var def = Def(
            new[] { new NotificationContextRecipientDef("SELLER", "Vendedor de la orden", "Order's sales rep", true),
                    new NotificationContextRecipientDef("CLIENT", "Cliente de la orden", "Order's customer", false, NotificationAudience.Customer) },
            new[] { new NotificationChannelDefault("EMAIL", "Orden {orderNumber}", "Order {orderNumber}", "Hola\n\n{orderDetail}", "Hi\n\n{orderDetail}") });
        var catalog = new NotificationEventCatalog(new[] { new OneEvent(def) });
        var e = catalog.Find("x_event")!;
        Assert.Equal(new[] { "orderDetail" }, e.BlockNames.ToArray());
        Assert.Equal("SELLER", e.ContextRecipient("seller")!.Key);
        Assert.Null(e.ContextRecipient("FOO"));
        Assert.Equal("Orden {orderNumber}", e.ChannelDefaultFor("email")!.SubjectEs);
        Assert.Null(e.ChannelDefaultFor("SMS"));
        Assert.Equal(NotificationAudience.Staff, e.ContextRecipients![0].Audience);
    }

    [Fact]
    public void Existing_events_without_new_fields_still_build()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new TestEventProvider() });
        var e = catalog.Find(NotificationCodes.TestEvent)!;
        Assert.Null(e.ContextRecipients);
        Assert.Null(e.ChannelDefaults);
        Assert.Empty(e.BlockNames);
        Assert.All(e.Variables, v => Assert.False(v.IsBlock));
    }

    [Theory]
    [InlineData("{orderDetail}", "Order")]
    [InlineData("Orden", "Order {orderDetail}")]
    public void Catalog_rejects_block_variable_in_channel_default_subject(string subjectEs, string subjectEn)
    {
        var def = Def(defaults: new[] { new NotificationChannelDefault("EMAIL", subjectEs, subjectEn, "{orderDetail}", "{orderDetail}") });
        var ex = Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(def) }));
        Assert.Equal("El evento de aviso 'X_EVENT' usa la variable bloque {orderDetail} en el asunto de la plantilla por omisión del canal EMAIL.", ex.Message);
    }

    [Fact]
    public void Catalog_rejects_repeated_context_key()
    {
        var def = Def(new[] { new NotificationContextRecipientDef("SELLER", "a", "a", true), new NotificationContextRecipientDef("SELLER", "b", "b", false) });
        var ex = Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(def) }));
        Assert.Equal("El evento de aviso 'X_EVENT' tiene el destinatario del evento 'SELLER' repetido.", ex.Message);
    }

    [Theory]
    [InlineData("seller")]
    [InlineData("ROUTE-SELLERS")]
    [InlineData("1SELLER")]
    [InlineData("")]
    public void Catalog_rejects_malformed_context_key(string key)
    {
        var def = Def(new[] { new NotificationContextRecipientDef(key, "a", "a", true) });
        var ex = Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(def) }));
        Assert.Contains("clave no válida", ex.Message);
    }

    [Fact]
    public void Catalog_rejects_undeclared_variable_in_channel_default()
    {
        var def = Def(defaults: new[] { new NotificationChannelDefault("EMAIL", "Orden {orderNumber}", "Order {orderNumber}", "{orderDetail} {seller}", "{orderDetail}") });
        Assert.Equal("El evento de aviso 'X_EVENT' usa en la plantilla por omisión del canal EMAIL la variable {seller}, que no declara.",
            Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(def) })).Message);
    }

    [Fact]
    public void Context_key_length_limit_is_40()
    {
        Assert.True(NotificationEventDefRules.IsValidContextKey("ROUTE_SELLERS"));
        Assert.True(NotificationEventDefRules.IsValidContextKey(new string('A', 40)));
        Assert.False(NotificationEventDefRules.IsValidContextKey(new string('A', 41)));
        Assert.False(NotificationEventDefRules.IsValidContextKey(null));
        Assert.False(NotificationEventDefRules.IsValidContextKey("SELLER\n"));
    }

    [Fact]
    public void Catalog_rejects_unknown_or_repeated_channel_default()
    {
        var unknown = Def(defaults: new[] { new NotificationChannelDefault("FAX", "a", "a", "b", "b") });
        Assert.Equal("El evento de aviso 'X_EVENT' tiene una plantilla por omisión del canal desconocido 'FAX'.",
            Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(unknown) })).Message);

        var repeated = Def(defaults: new[] { new NotificationChannelDefault("EMAIL", "a", "a", "b", "b"), new NotificationChannelDefault("EMAIL", "c", "c", "d", "d") });
        Assert.Equal("El evento de aviso 'X_EVENT' tiene la plantilla por omisión del canal EMAIL repetida.",
            Assert.Throws<InvalidOperationException>(() => new NotificationEventCatalog(new[] { new OneEvent(repeated) })).Message);
    }
}

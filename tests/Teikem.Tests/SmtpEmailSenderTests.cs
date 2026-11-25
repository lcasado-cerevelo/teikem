using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Teikem.Infrastructure.Abstractions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G4) — buzón local por SMTP (Mailpit) y la elección del remitente. Nunca red externa ni Brevo: el mensaje se arma sin red
/// y el envío se prueba contra un servidor SMTP mínimo en 127.0.0.1 levantado por la propia prueba.
/// </summary>
public class SmtpEmailSenderTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (SmtpEmailSender Sender, ListLogger<SmtpEmailSender> Log) Sender(Dictionary<string, string?>? values = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();
        var log = new ListLogger<SmtpEmailSender>();
        return (new SmtpEmailSender(config, log), log);
    }

    private static Dictionary<string, string?> Local(string? from = null, int? port = null) => new()
    {
        ["Email:Smtp:Host"] = "localhost",
        ["Email:Smtp:Port"] = port?.ToString(),
        ["Email:Smtp:From"] = from,
    };

    // ---------------------------------------------------------------- elección del remitente (pura)

    [Theory]
    [InlineData("Development", "localhost", true)]
    [InlineData("development", "localhost", true)]
    [InlineData(" Development ", "mailpit", true)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "", false)]
    [InlineData("Development", "   ", false)]
    [InlineData("Production", "localhost", false)]
    [InlineData("Staging", "localhost", false)]
    [InlineData("Test", "localhost", false)]
    [InlineData(null, "localhost", false)]
    [InlineData("", "localhost", false)]
    public void UseLocalSmtp_solo_en_Development_con_Host(string? environment, string? host, bool expected)
        => Assert.Equal(expected, EmailSenderSelection.UseLocalSmtp(environment, host));

    // ---------------------------------------------------------------- configuración

    [Fact]
    public void Sin_Host_no_esta_configurado_y_siempre_es_buzon_local()
    {
        var (s, _) = Sender();
        Assert.False(s.IsConfigured);
        Assert.True(s.IsLocalSink);
        Assert.True(((ITransactionalEmailSender)s).IsLocalSink);
    }

    [Fact]
    public void Con_Host_esta_configurado()
    {
        var (s, _) = Sender(Local());
        Assert.True(s.IsConfigured);
    }

    [Fact]
    public async Task Sin_Host_no_envia_y_lo_anota_sin_el_contenido()
    {
        var (s, log) = Sender();
        await s.SendAsync("ana@example.test", "Ana", "Asunto X", "<p>contenido-secreto-123</p>", CancellationToken.None);
        var w = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, w.Level);
        Assert.Contains("Asunto X", w.Message);
        Assert.Contains("ana@example.test", w.Message);
        Assert.DoesNotContain("contenido-secreto-123", w.Message);
    }

    // ---------------------------------------------------------------- mensaje MIME (sin red)

    [Fact]
    public void BuildMessage_arma_remitente_con_la_compania_destinatario_asunto_y_HTML()
    {
        var (s, _) = Sender(Local());
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", "Ana Pérez", "Orden de venta OV-00012 — Farmacia", "<p>Hola <b>Ana</b></p>", "Advance Logistics"));
        var from = Assert.Single(m.From.Mailboxes);
        Assert.Equal(SmtpEmailSender.DefaultFrom, from.Address);
        Assert.Equal("Advance Logistics", from.Name);
        var to = Assert.Single(m.To.Mailboxes);
        Assert.Equal("ana@example.test", to.Address);
        Assert.Equal("Ana Pérez", to.Name);
        Assert.Equal("Orden de venta OV-00012 — Farmacia", m.Subject);
        Assert.Equal("<p>Hola <b>Ana</b></p>", m.HtmlBody);
        Assert.Empty(m.Attachments);
    }

    [Fact]
    public void BuildMessage_sin_compania_usa_Teikem_y_el_From_configurado()
    {
        var (s, _) = Sender(Local(from: "  avisos@local.test "));
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", null, "A", "<p>x</p>"));
        var from = Assert.Single(m.From.Mailboxes);
        Assert.Equal("avisos@local.test", from.Address);
        Assert.Equal("Teikem", from.Name);
        // sin nombre, el destinatario se muestra con su correo
        Assert.Equal("ana@example.test", Assert.Single(m.To.Mailboxes).Name);
    }

    [Fact]
    public void BuildMessage_limpia_el_nombre_de_la_compania_como_Brevo()
    {
        var (s, _) = Sender(Local());
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", null, "A", "<p>x</p>", "<Acme \"Uno\">"));
        Assert.Equal(EmailSenderNames.Resolve("<Acme \"Uno\">", "Teikem"), Assert.Single(m.From.Mailboxes).Name);
        Assert.Equal("Acme Uno", Assert.Single(m.From.Mailboxes).Name);
    }

    [Fact]
    public void BuildMessage_lleva_los_adjuntos_con_su_nombre_tipo_y_contenido()
    {
        var (s, _) = Sender(Local());
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", null, "A", "<p>x</p>", null,
            new[] { new EmailAttachment("firma-OV-00012.png", "image/png", png), new EmailAttachment("raro.bin", "esto no es un tipo", new byte[] { 9 }) }));
        Assert.Equal("<p>x</p>", m.HtmlBody);
        var parts = m.Attachments.OfType<MimePart>().ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal("firma-OV-00012.png", parts[0].FileName);
        Assert.Equal("image/png", parts[0].ContentType.MimeType);
        using var ms = new MemoryStream();
        parts[0].Content.DecodeTo(ms);
        Assert.Equal(png, ms.ToArray());
        // un tipo que no se puede leer sale como binario genérico, sin fallar
        Assert.Equal("raro.bin", parts[1].FileName);
        Assert.Equal("application/octet-stream", parts[1].ContentType.MimeType);
    }

    [Fact]
    public void BuildMessage_omite_el_adjunto_que_pasa_el_tope_y_lo_anota_sin_el_contenido()
    {
        var (s, log) = Sender(Local());
        var big = new byte[EmailAttachmentRules.MaxTotalBytes + 1];
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", null, "A", "<p>x</p>", null,
            new[] { new EmailAttachment("grande.pdf", "application/pdf", big), new EmailAttachment("chica.png", "image/png", new byte[] { 1, 2 }), new EmailAttachment("vacia.png", "image/png", Array.Empty<byte>()) }));
        var part = Assert.Single(m.Attachments.OfType<MimePart>());
        Assert.Equal("chica.png", part.FileName);
        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("grande.pdf") && w.Contains(big.Length.ToString()));
        Assert.Contains(warnings, w => w.Contains("vacia.png"));
    }

    [Fact]
    public void BuildMessage_sin_adjuntos_es_un_mensaje_HTML_sencillo()
    {
        var (s, _) = Sender(Local());
        using var m = s.BuildMessage(new EmailMessage("ana@example.test", null, "A", "<p>x</p>", null, Array.Empty<EmailAttachment>()));
        Assert.IsType<TextPart>(m.Body);
        Assert.Equal("html", ((TextPart)m.Body).ContentType.MediaSubtype);
    }

    // ---------------------------------------------------------------- envío real contra un SMTP mínimo en 127.0.0.1

    [Fact]
    public async Task SendAsync_entrega_por_SMTP_sin_TLS_al_Host_y_Puerto_configurados()
    {
        using var server = new MiniSmtpServer();
        var (s, log) = Sender(Local(port: server.Port));
        await s.SendAsync(new EmailMessage("ana@example.test", "Ana", "Orden OV-00012", "<p>tabla</p>", "Advance Logistics",
            new[] { new EmailAttachment("firma-OV-00012.png", "image/png", new byte[] { 1, 2, 3 }) }), CancellationToken.None);
        var data = await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("MAIL FROM:<teikem@localhost.test>", server.Commands);
        Assert.Contains("RCPT TO:<ana@example.test>", server.Commands);
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("STARTTLS", StringComparison.OrdinalIgnoreCase) || c.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
        using var parsed = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(data)));
        Assert.Equal("Orden OV-00012", parsed.Subject);
        Assert.Equal("Advance Logistics", Assert.Single(parsed.From.Mailboxes).Name);
        Assert.Equal("firma-OV-00012.png", Assert.Single(parsed.Attachments.OfType<MimePart>()).FileName);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("buzón local"));
    }

    [Fact]
    public async Task SendAsync_firma_vieja_entrega_igual_sin_adjuntos()
    {
        using var server = new MiniSmtpServer();
        var (s, _) = Sender(Local(port: server.Port));
        await s.SendAsync("ana@example.test", null, "Código", "<p>123</p>", CancellationToken.None, "Acme");
        var data = await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var parsed = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(data)));
        Assert.Equal("Código", parsed.Subject);
        Assert.Empty(parsed.Attachments);
    }

    /// <summary>Servidor SMTP mínimo (una conexión) en 127.0.0.1 con puerto libre: guarda los comandos y el DATA recibido.</summary>
    private sealed class MiniSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port { get; }
        public List<string> Commands { get; } = new();
        public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MiniSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(RunAsync);
        }

        private async Task RunAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false));
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 localhost ESMTP prueba");
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    lock (Commands) Commands.Add(line);
                    var cmd = line.ToUpperInvariant();
                    if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await writer.WriteLineAsync("250 localhost");
                    else if (cmd.StartsWith("MAIL FROM") || cmd.StartsWith("RCPT TO") || cmd.StartsWith("RSET") || cmd.StartsWith("NOOP")) await writer.WriteLineAsync("250 OK");
                    else if (cmd == "DATA")
                    {
                        await writer.WriteLineAsync("354 fin con <CRLF>.<CRLF>");
                        var sb = new StringBuilder();
                        string? d;
                        while ((d = await reader.ReadLineAsync()) is not null && d != ".")
                            sb.Append(d.StartsWith("..") ? d[1..] : d).Append("\r\n");
                        await writer.WriteLineAsync("250 OK recibido");
                        Received.TrySetResult(sb.ToString());
                    }
                    else if (cmd == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("502 no implementado");
                }
            }
            catch (Exception ex)
            {
                Received.TrySetException(ex);
            }
        }

        public void Dispose() => _listener.Stop();
    }
}

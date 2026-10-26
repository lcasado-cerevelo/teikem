using System.Net;
using System.Text;
using MimeKit;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Integrations;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-24 — buzones de Microsoft 365 por Graph: el token, la lectura de los NO leídos (más viejo primero, con el filtro del asunto), marcar leído y mover, y los errores en palabras del usuario. Con un manejador de HTTP de mentira.</summary>
public class GraphMailboxReaderTests
{
    private sealed class Reply
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{}";
    }

    private sealed class FakeGraph : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Reply> respond;
        public FakeGraph(Func<HttpRequestMessage, Reply> respond) { this.respond = respond; }
        public List<string[]> Calls { get; } = new List<string[]>();   // método, url, cuerpo
        public Dictionary<string, byte[]> Mime { get; } = new Dictionary<string, byte[]>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            var url = request.RequestUri!.AbsoluteUri;
            Calls.Add(new[] { request.Method.Method, url, body });
            if (url.EndsWith("/$value", StringComparison.Ordinal))
            {
                var parts = url.Substring(0, url.Length - "/$value".Length).Split('/');
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mime[Uri.UnescapeDataString(parts[parts.Length - 1])]) };
            }
            var r = respond(request);
            return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") };
        }
    }

    private const string Token = "{\"access_token\":\"tok-1\",\"expires_in\":3599}";

    private static bool IsLogin(HttpRequestMessage req) => req.RequestUri!.AbsoluteUri.Contains("login.microsoftonline.com");

    /// <summary>El token siempre sale bien; todo lo demás contesta lo dado.</summary>
    private static Reply AfterLogin(HttpRequestMessage req, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        if (IsLogin(req)) return new Reply { Body = Token };
        return new Reply { Status = status, Body = body };
    }

    private static EmailConfig Config(string? subject = null)
    {
        var match = subject is null ? null : new EmailTextMatch("CONTAINS", subject);
        return new EmailConfig("", 993, true, "avisos@contoso.com", "INBOX", "Procesados", match, Provider: EmailProviders.Microsoft365, TenantId: "contoso.onmicrosoft.com", ClientId: "app-1");
    }

    private static byte[] Eml(string subject, string body)
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress("Axis", "ops@axiscare.com"));
        m.To.Add(new MailboxAddress("Avisos", "avisos@contoso.com"));
        m.Subject = subject;
        m.Body = new TextPart("plain") { Text = body };
        using var ms = new MemoryStream();
        m.WriteTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task Lee_los_no_leidos_del_mas_viejo_al_mas_nuevo_y_solo_los_del_asunto()
    {
        var fake = new FakeGraph(req => AfterLogin(req, "{\"value\":[{\"id\":\"m1\",\"subject\":\"Despacho 1\"},{\"id\":\"m2\",\"subject\":\"Oferta\"},{\"id\":\"m3\",\"subject\":\"Despacho 2\"}]}"));
        fake.Mime["m1"] = Eml("Despacho 1", "uno");
        fake.Mime["m3"] = Eml("Despacho 2", "dos");
        var reader = new GraphMailboxReader(new HttpClient(fake));

        var messages = await reader.FetchAsync(Config("Despacho"), "secreto", 10, default);

        Assert.Equal(new[] { "m1", "m3" }, messages.Select(m => m.Key).ToArray());   // el de «Oferta» ni se baja ni se marcará
        Assert.Equal("uno", messages[0].Data.TextBody.Trim());
        var list = fake.Calls.First(c => c[1].Contains("/messages?"))[1];
        Assert.Contains("/users/avisos%40contoso.com/mailFolders/inbox/messages", list);
        Assert.Contains("orderby=receivedDateTime%20asc", list);
        Assert.Contains(Uri.EscapeDataString("receivedDateTime ge 1900-01-01T00:00:00Z"), list);
        Assert.DoesNotContain("isRead", list);   // la carpeta es la verdad: leídos o no, todo lo que está en ella se atiende
        var token = fake.Calls.First(c => c[1].Contains("login.microsoftonline.com"));
        Assert.Contains("contoso.onmicrosoft.com/oauth2/v2.0/token", token[1]);
        Assert.Contains("grant_type=client_credentials", token[2]);
        Assert.Contains("client_secret=secreto", token[2]);
    }

    [Fact]
    public async Task Respeta_el_maximo_por_corrida_y_reutiliza_el_token()
    {
        var fake = new FakeGraph(req => AfterLogin(req, "{\"value\":[{\"id\":\"a\",\"subject\":\"x\"},{\"id\":\"b\",\"subject\":\"x\"},{\"id\":\"c\",\"subject\":\"x\"}]}"));
        foreach (var id in new[] { "a", "b", "c" }) fake.Mime[id] = Eml("x", id);
        var reader = new GraphMailboxReader(new HttpClient(fake));

        var first = await reader.FetchAsync(Config(), "s", 2, default);
        await reader.FetchAsync(Config(), "s", 2, default);

        Assert.Equal(new[] { "a", "b" }, first.Select(m => m.Key).ToArray());
        Assert.Equal(1, fake.Calls.Count(c => c[1].Contains("login.microsoftonline.com")));   // el token se guarda hasta que está por vencer
    }

    [Fact]
    public async Task Marca_leido_y_mueve_a_Procesados_creando_la_carpeta_si_falta()
    {
        var fake = new FakeGraph(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (IsLogin(req)) return new Reply { Body = Token };
            if (req.Method == HttpMethod.Get && url.Contains("mailFolders?")) return new Reply { Body = "{\"value\":[]}" };
            if (req.Method == HttpMethod.Post && url.EndsWith("/mailFolders")) return new Reply { Status = HttpStatusCode.Created, Body = "{\"id\":\"folder-9\"}" };
            return new Reply();
        });
        var reader = new GraphMailboxReader(new HttpClient(fake));

        await reader.MarkProcessedAsync(Config(), "s", new[] { "m1", "m2" }, default);

        Assert.Contains(fake.Calls, c => c[0] == "POST" && c[1].EndsWith("/mailFolders") && c[2].Contains("\"Procesados\""));
        Assert.DoesNotContain(fake.Calls, c => c[0] == "PATCH");   // solo se mueve: no se cambia leído/no leído
        Assert.Equal(2, fake.Calls.Count(c => c[0] == "POST" && c[1].EndsWith("/move") && c[2].Contains("folder-9")));
        Assert.DoesNotContain(fake.Calls, c => c[0] == "DELETE");   // nunca se borra
    }

    [Fact]
    public async Task Probar_la_conexion_cuenta_los_correos_de_la_carpeta()
    {
        var fake = new FakeGraph(req => AfterLogin(req, "{\"totalItemCount\":7}"));
        Assert.Equal(7, await new GraphMailboxReader(new HttpClient(fake)).TestAsync(Config(), "s", default));
    }

    [Theory]
    [InlineData("{\"error\":\"invalid_client\",\"error_description\":\"AADSTS7000215: Invalid client secret provided.\\r\\nTrace ID: x\"}", "Client secret")]
    [InlineData("{\"error\":\"unauthorized_client\",\"error_description\":\"AADSTS700016: Application with identifier 'x' was not found.\"}", "consentimiento")]
    [InlineData("{\"error\":\"invalid_request\",\"error_description\":\"AADSTS90002: Tenant 'x' not found.\"}", "Tenant ID")]
    public async Task Un_secreto_o_tenant_malos_se_explican_en_palabras_del_usuario(string body, string expected)
    {
        var fake = new FakeGraph(req => new Reply { Status = HttpStatusCode.BadRequest, Body = body });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new GraphMailboxReader(new HttpClient(fake)).TestAsync(Config(), "s", default));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Sin_permiso_sobre_el_buzon_explica_el_consentimiento_o_la_politica()
    {
        var fake = new FakeGraph(req => AfterLogin(req, "{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"Access is denied.\"}}", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new GraphMailboxReader(new HttpClient(fake)).TestAsync(Config(), "s", default));
        Assert.Contains("Mail.ReadWrite", ex.Message);
        Assert.Contains("ErrorAccessDenied", ex.Message);
    }

    [Fact]
    public async Task Modo_log_lista_los_que_el_log_no_tiene_del_mas_viejo_al_mas_nuevo_sin_tocar_el_buzon()
    {
        var fake = new FakeGraph(req => AfterLogin(req, "{\"value\":[{\"id\":\"m1\",\"subject\":\"a\"},{\"id\":\"m2\",\"subject\":\"b\"},{\"id\":\"m3\",\"subject\":\"c\"},{\"id\":\"m4\",\"subject\":\"d\"}]}"));
        fake.Mime["m3"] = Eml("c", "tres");
        fake.Mime["m4"] = Eml("d", "cuatro");
        var reader = new GraphMailboxReader(new HttpClient(fake));
        var asked = new List<string>();

        // el log de Teikem ya tiene m1 y m2 (aunque en el buzón sigan como no leídos o ya leídos: da igual)
        var keys = await reader.ListNewKeysAsync(Config(), "s", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), 1, (page, ct) =>
        {
            asked.AddRange(page);
            ISet<string> done = new HashSet<string> { "m1", "m2" };
            return Task.FromResult(done);
        }, default);
        var messages = await reader.GetAsync(Config(), "s", keys, default);

        Assert.Equal(new[] { "m3" }, keys.ToArray());   // el más viejo que falta; el tope (1) corta ahí
        Assert.Equal("tres", messages[0].Data.TextBody.Trim());
        var list = fake.Calls.First(c => c[1].Contains("/messages?"))[1];
        Assert.Contains(Uri.EscapeDataString("receivedDateTime ge 2026-10-01T00:00:00Z"), list);   // desde la fecha configurada
        Assert.DoesNotContain("isRead", list);   // leídos o no: el estado del buzón no importa
        Assert.DoesNotContain(fake.Calls, c => c[0] == "PATCH" || c[1].EndsWith("/move") || c[0] == "DELETE");   // el buzón no se toca
    }

    [Fact]
    public void La_carpeta_es_la_verdad_por_omision_y_la_fecha_desde_se_valida()
    {
        Assert.False(EmailMarkModes.IsLog(null));   // por omisión: la carpeta manda (se mueve a «Procesados»); el log de Teikem es solo una opción explícita
        Assert.True(EmailMarkModes.IsLog("LOG"));
        Assert.False(EmailMarkModes.IsLog("mailbox"));
        Assert.Equal(EmailRules.ProcessedFolderRequiredMessage, EmailRules.Validate(Config() with { ProcessedFolder = "" }, true)["email.processedFolder"]);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), EmailMarkModes.SinceUtc(" 2026-10-01 "));
        Assert.Null(EmailMarkModes.SinceUtc(""));
        Assert.Null(EmailMarkModes.SinceUtc("1/10/2026"));
        Assert.Empty(EmailRules.Validate(Config() with { MarkMode = "MAILBOX", ReadSince = "2026-10-01" }, true));
        Assert.Equal(EmailRules.ReadSinceMessage, EmailRules.Validate(Config() with { ReadSince = "ayer" }, true)["email.readSince"]);
        Assert.Equal(EmailRules.UnknownMarkModeMessage("X"), EmailRules.Validate(Config() with { MarkMode = "X" }, true)["email.markMode"]);
    }

    [Fact]
    public void Microsoft_365_pide_tenant_cliente_y_secreto_en_lugar_de_servidor_y_puerto()
    {
        Assert.Empty(EmailRules.Validate(Config(), true));   // no pide servidor ni puerto
        var bad = EmailRules.Validate(Config() with { TenantId = " ", ClientId = null, Username = "" }, false);
        Assert.Equal(EmailRules.TenantRequiredMessage, bad["email.tenantId"]);
        Assert.Equal(EmailRules.ClientIdRequiredMessage, bad["email.clientId"]);
        Assert.Equal(EmailRules.UsernameRequiredMessage, bad["email.username"]);
        Assert.Equal(EmailRules.ClientSecretRequiredMessage, bad["email.password"]);
        Assert.False(bad.ContainsKey("email.host"));
        Assert.Equal(EmailRules.UnknownProviderMessage("POP3"), EmailRules.Validate(Config() with { Provider = "POP3" }, true)["email.provider"]);
        // IMAP sigue igual
        Assert.Equal(EmailRules.HostRequiredMessage, EmailRules.Validate(new EmailConfig("", 993, true, "u"), true)["email.host"]);
    }
}

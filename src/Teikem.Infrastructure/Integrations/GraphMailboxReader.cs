using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimeKit;
using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Integrations;

/// <summary>
/// 2026-10-24 — buzones de Microsoft 365 (también los compartidos, que no tienen contraseña ni licencia) con una aplicación registrada en Microsoft Entra ID y el API de correo de Microsoft (Graph).
/// Acceso de aplicación (client credentials): nada de contraseña del buzón. En la configuración, <c>Username</c> es la dirección del buzón, <c>TenantId</c> y <c>ClientId</c> identifican la aplicación y el secreto
/// que se pasa como «password» es el secreto de la aplicación (Client secret), guardado cifrado como cualquier otro secreto de conector. Permisos de aplicación necesarios: <c>Mail.ReadWrite</c> (leer, marcar leído y mover a «Procesados»;
/// nunca se borra) con consentimiento del administrador. El administrador puede limitarla a buzones concretos con una política de acceso de aplicaciones de Exchange.
/// Misma regla que IMAP: solo los NO leídos, del más viejo al más nuevo, hasta <c>maxMessages</c> por corrida.
/// </summary>
public sealed class GraphMailboxReader : IMailboxReader
{
    public const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const string LoginBase = "https://login.microsoftonline.com";
    private const int MaxPages = 5;
    /// <summary>Páginas de 100 mensajes que se recorren por corrida buscando correos sin atender (modo «log de Teikem»): 100 páginas = 10 000 correos.</summary>
    private const int MaxScanPages = 100;

    private readonly HttpClient http;
    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> tokens = new();

    public GraphMailboxReader() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }) { }
    public GraphMailboxReader(HttpClient http) => this.http = http;   // para las pruebas (un manejador de HTTP de mentira)

    // ---------------------------------------------------------------- token

    private async Task<string> TokenAsync(EmailConfig c, string secret, CancellationToken ct)
    {
        var tenant = (c.TenantId ?? string.Empty).Trim();
        var clientId = (c.ClientId ?? string.Empty).Trim();
        var key = $"{tenant}|{clientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
        if (tokens.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return cached.Token;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{LoginBase}/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "client_credentials", ["scope"] = "https://graph.microsoft.com/.default",
            }),
        };
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(TokenErrorMessage(body));
        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Microsoft no devolvió un token de acceso.");
        var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3000;
        tokens[key] = (token, DateTime.UtcNow.AddSeconds(seconds));
        return token;
    }

    /// <summary>El motivo que da Microsoft al pedir el token, con una pista en palabras del usuario para los errores más comunes.</summary>
    public static string TokenErrorMessage(string body)
    {
        string? description = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            description = doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException) { /* cuerpo que no es JSON: se usa tal cual */ }
        description = (description ?? body).Split('\n', '\r')[0].Trim();
        var hint = description switch
        {
            var x when x.Contains("AADSTS7000215") || x.Contains("AADSTS7000222") => "El secreto de la aplicación (Client secret) no es válido o venció: cree uno nuevo en Microsoft Entra ID y péguelo aquí. Use el VALOR del secreto, no su identificador.",
            var x when x.Contains("AADSTS700016") => "La aplicación no existe en ese tenant: revise el Client ID y que el administrador del tenant haya dado su consentimiento.",
            var x when x.Contains("AADSTS90002") || x.Contains("AADSTS900023") => "No se encontró el tenant: revise el Tenant ID.",
            var x when x.Contains("AADSTS65001") || x.Contains("AADSTS500011") => "Falta el consentimiento del administrador de la aplicación en ese tenant.",
            _ => null,
        };
        return hint is null ? $"Microsoft rechazó las credenciales de la aplicación: {description}" : $"{hint} ({description})";
    }

    // ---------------------------------------------------------------- llamadas

    private async Task<string> SendAsync(HttpMethod method, string url, string token, string? json, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                var wait = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, ct);
                continue;
            }
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException(GraphErrorMessage(res.StatusCode, body));
            return body;
        }
    }

    private async Task<byte[]> SendBytesAsync(string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(GraphErrorMessage(res.StatusCode, await res.Content.ReadAsStringAsync(ct)));
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>El error de Graph en palabras del usuario: sin permiso (consentimiento o política de acceso), buzón inexistente u otro.</summary>
    public static string GraphErrorMessage(HttpStatusCode status, string body)
    {
        string? code = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                code = e.TryGetProperty("code", out var c) ? c.GetString() : null;
                message = e.TryGetProperty("message", out var m) ? m.GetString() : null;
            }
        }
        catch (JsonException) { /* sin cuerpo JSON */ }
        var detail = string.IsNullOrWhiteSpace(code) ? $"HTTP {(int)status}" : $"{code}: {message}";
        return status switch
        {
            HttpStatusCode.Unauthorized => $"Microsoft no aceptó el token de la aplicación ({detail}).",
            HttpStatusCode.Forbidden => $"La aplicación no tiene permiso sobre este buzón: falta el permiso de aplicación Mail.ReadWrite con consentimiento del administrador, o una política de acceso de aplicaciones lo bloquea ({detail}).",
            HttpStatusCode.NotFound when code is "ErrorInvalidUser" or "MailboxNotEnabledForRESTAPI" or "ResourceNotFound" => $"No se encontró el buzón: revise la dirección ({detail}).",
            _ => detail,
        };
    }

    private static string UserPath(EmailConfig c) => $"{GraphBase}/users/{Uri.EscapeDataString(c.Username.Trim())}";

    // ---------------------------------------------------------------- carpetas

    private async Task<string> FolderIdAsync(EmailConfig c, string token, string name, bool create, CancellationToken ct)
    {
        if (string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase)) return "inbox";
        var escaped = name.Replace("'", "''");
        var body = await SendAsync(HttpMethod.Get, $"{UserPath(c)}/mailFolders?$filter={Uri.EscapeDataString($"displayName eq '{escaped}'")}&$select=id", token, null, ct);
        using var doc = JsonDocument.Parse(body);
        foreach (var f in doc.RootElement.GetProperty("value").EnumerateArray()) return f.GetProperty("id").GetString()!;
        if (!create) throw new InvalidOperationException($"No existe la carpeta «{name}» en el buzón.");
        var made = await SendAsync(HttpMethod.Post, $"{UserPath(c)}/mailFolders", token, JsonSerializer.Serialize(new { displayName = name }), ct);
        using var madeDoc = JsonDocument.Parse(made);
        return madeDoc.RootElement.GetProperty("id").GetString()!;
    }

    // ---------------------------------------------------------------- IMailboxReader

    public async Task<IReadOnlyList<MailboxMessage>> FetchAsync(EmailConfig c, string password, int maxMessages, CancellationToken ct)
    {
        var token = await TokenAsync(c, password, ct);
        var folder = await FolderIdAsync(c, token, c.Mailbox, false, ct);
        var max = Math.Clamp(maxMessages, 1, 200);
        // «receivedDateTime» primero en el filtro: Graph lo exige para poder ordenar de más viejo a más nuevo
        var filter = Uri.EscapeDataString("receivedDateTime ge 1900-01-01T00:00:00Z and isRead eq false");
        string? next = $"{UserPath(c)}/mailFolders/{Uri.EscapeDataString(folder)}/messages?$filter={filter}&$orderby=receivedDateTime asc&$select=id,subject&$top=100";
        var ids = new List<string>();
        for (var page = 0; next is not null && page < MaxPages && ids.Count < max; page++)
        {
            using var doc = JsonDocument.Parse(await SendAsync(HttpMethod.Get, next, token, null, ct));
            foreach (var m in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var subject = m.TryGetProperty("subject", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                if (!EmailRules.TextMatches(subject, c.Subject)) continue;   // otro tema: ni se baja ni se marca
                ids.Add(m.GetProperty("id").GetString()!);
                if (ids.Count >= max) break;
            }
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }
        var result = new List<MailboxMessage>();
        foreach (var id in ids)
        {
            var mime = await SendBytesAsync($"{UserPath(c)}/messages/{Uri.EscapeDataString(id)}/$value", token, ct);
            using var ms = new MemoryStream(mime);
            result.Add(new MailboxMessage(MailMessageMapper.Map(MimeMessage.Load(ms), id), id));
        }
        return result;
    }

    public async Task MarkProcessedAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return;
        var token = await TokenAsync(c, password, ct);
        var target = string.IsNullOrWhiteSpace(c.ProcessedFolder) ? null : await FolderIdAsync(c, token, c.ProcessedFolder, true, ct);
        foreach (var id in keys)
        {
            var path = $"{UserPath(c)}/messages/{Uri.EscapeDataString(id)}";
            await SendAsync(new HttpMethod("PATCH"), path, token, "{\"isRead\":true}", ct);
            if (target is not null) await SendAsync(HttpMethod.Post, $"{path}/move", token, JsonSerializer.Serialize(new { destinationId = target }), ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListNewKeysAsync(EmailConfig c, string password, DateTime? sinceUtc, int need, Func<IReadOnlyList<string>, CancellationToken, Task<ISet<string>>> alreadyLogged, CancellationToken ct)
    {
        var token = await TokenAsync(c, password, ct);
        var folder = await FolderIdAsync(c, token, c.Mailbox, false, ct);
        var from = (sinceUtc ?? new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var filter = Uri.EscapeDataString($"receivedDateTime ge {from}");
        string? next = $"{UserPath(c)}/mailFolders/{Uri.EscapeDataString(folder)}/messages?$filter={filter}&$orderby=receivedDateTime asc&$select=id,subject&$top=100";
        var result = new List<string>();
        for (var page = 0; next is not null && result.Count < need && page < MaxScanPages; page++)
        {
            using var doc = JsonDocument.Parse(await SendAsync(HttpMethod.Get, next, token, null, ct));
            var keys = new List<string>();
            foreach (var m in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var subject = m.TryGetProperty("subject", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                if (EmailRules.TextMatches(subject, c.Subject)) keys.Add(m.GetProperty("id").GetString()!);
            }
            var done = keys.Count == 0 ? new HashSet<string>() : await alreadyLogged(keys, ct);
            foreach (var k in keys) { if (!done.Contains(k)) { result.Add(k); if (result.Count >= need) break; } }
            next = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
        }
        return result;
    }

    public async Task<IReadOnlyList<MailboxMessage>> GetAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct)
    {
        if (keys.Count == 0) return Array.Empty<MailboxMessage>();
        var token = await TokenAsync(c, password, ct);
        var result = new List<MailboxMessage>();
        foreach (var id in keys)
        {
            var mime = await SendBytesAsync($"{UserPath(c)}/messages/{Uri.EscapeDataString(id)}/$value", token, ct);
            using var ms = new MemoryStream(mime);
            result.Add(new MailboxMessage(MailMessageMapper.Map(MimeMessage.Load(ms), id), id));
        }
        return result;
    }

    public async Task<int> TestAsync(EmailConfig c, string password, CancellationToken ct)
    {
        var token = await TokenAsync(c, password, ct);
        var folder = await FolderIdAsync(c, token, c.Mailbox, false, ct);
        using var doc = JsonDocument.Parse(await SendAsync(HttpMethod.Get, $"{UserPath(c)}/mailFolders/{Uri.EscapeDataString(folder)}?$select=unreadItemCount", token, null, ct));
        return doc.RootElement.TryGetProperty("unreadItemCount", out var n) && n.TryGetInt32(out var count) ? count : 0;
    }
}

/// <summary>Escoge el lector según el tipo de buzón de la integración: IMAP (servidor, usuario, contraseña) o Microsoft 365 (aplicación de Entra ID).</summary>
public sealed class RoutingMailboxReader(ImapMailboxReader imap, GraphMailboxReader graph) : IMailboxReader
{
    private IMailboxReader For(EmailConfig c) => EmailProviders.IsMicrosoft365(c.Provider) ? graph : imap;

    public Task<IReadOnlyList<MailboxMessage>> FetchAsync(EmailConfig c, string password, int maxMessages, CancellationToken ct) => For(c).FetchAsync(c, password, maxMessages, ct);
    public Task MarkProcessedAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct) => For(c).MarkProcessedAsync(c, password, keys, ct);
    public Task<int> TestAsync(EmailConfig c, string password, CancellationToken ct) => For(c).TestAsync(c, password, ct);
    public Task<IReadOnlyList<string>> ListNewKeysAsync(EmailConfig c, string password, DateTime? sinceUtc, int need, Func<IReadOnlyList<string>, CancellationToken, Task<ISet<string>>> alreadyLogged, CancellationToken ct) => For(c).ListNewKeysAsync(c, password, sinceUtc, need, alreadyLogged, ct);
    public Task<IReadOnlyList<MailboxMessage>> GetAsync(EmailConfig c, string password, IReadOnlyList<string> keys, CancellationToken ct) => For(c).GetAsync(c, password, keys, ct);
}

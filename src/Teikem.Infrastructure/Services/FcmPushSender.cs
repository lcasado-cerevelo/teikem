using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Configuración de Firebase Cloud Messaging (avisos emergentes al teléfono del chofer). La clave de la cuenta de servicio es un SECRETO: va en la configuración del servidor
/// (`Push:Fcm:ServiceAccountJsonPath` = ruta al archivo, o `Push:Fcm:ServiceAccountJson` = su contenido), nunca en el repositorio. Sin ella, los avisos solo quedan en la bandeja de la app.
/// </summary>
public sealed class FcmOptions
{
    public string? ServiceAccountJsonPath { get; set; }
    public string? ServiceAccountJson { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceAccountJson) || (!string.IsNullOrWhiteSpace(ServiceAccountJsonPath) && File.Exists(ServiceAccountJsonPath));
}

public enum PushOutcome { Sent, InvalidToken, Failed }

public interface IPushSender
{
    bool IsConfigured { get; }
    Task<PushOutcome> SendAsync(string deviceToken, string title, string? body, IReadOnlyDictionary<string, string>? data, CancellationToken ct);
}

/// <summary>Envía por la API HTTP v1 de FCM. El token de acceso (OAuth2, JWT RS256 firmado con la clave de la cuenta de servicio) se pide por su cuenta y se guarda ~50 minutos.</summary>
public sealed class FcmPushSender(HttpClient http, IOptions<FcmOptions> options, ILogger<FcmPushSender> logger) : IPushSender
{
    private sealed record ServiceAccount(string ProjectId, string ClientEmail, string PrivateKey);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (string Token, DateTime ExpiresUtc)? _token;

    public bool IsConfigured => options.Value.IsConfigured;

    private ServiceAccount Load()
    {
        var o = options.Value;
        var json = !string.IsNullOrWhiteSpace(o.ServiceAccountJson) ? o.ServiceAccountJson! : File.ReadAllText(o.ServiceAccountJsonPath!);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new ServiceAccount(r.GetProperty("project_id").GetString()!, r.GetProperty("client_email").GetString()!, r.GetProperty("private_key").GetString()!);
    }

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> AccessTokenAsync(ServiceAccount sa, CancellationToken ct)
    {
        if (_token is { } t && t.ExpiresUtc > DateTime.UtcNow.AddMinutes(5)) return t.Token;
        await Gate.WaitAsync(ct);
        try
        {
            if (_token is { } t2 && t2.ExpiresUtc > DateTime.UtcNow.AddMinutes(5)) return t2.Token;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = B64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iss"] = sa.ClientEmail, ["scope"] = "https://www.googleapis.com/auth/firebase.messaging", ["aud"] = "https://oauth2.googleapis.com/token", ["iat"] = now, ["exp"] = now + 3600,
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(sa.PrivateKey);
            var sig = B64Url(rsa.SignData(Encoding.UTF8.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var res = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = $"{header}.{claims}.{sig}",
            }), ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            _token = (token, DateTime.UtcNow.AddMinutes(50));
            return token;
        }
        finally { Gate.Release(); }
    }

    public async Task<PushOutcome> SendAsync(string deviceToken, string title, string? body, IReadOnlyDictionary<string, string>? data, CancellationToken ct)
    {
        if (!IsConfigured) return PushOutcome.Failed;
        try
        {
            var sa = Load();
            var access = await AccessTokenAsync(sa, ct);
            var message = new Dictionary<string, object>
            {
                ["token"] = deviceToken,
                ["notification"] = new Dictionary<string, string?> { ["title"] = title, ["body"] = body },
                ["android"] = new Dictionary<string, object> { ["priority"] = "high", ["notification"] = new Dictionary<string, string> { ["channel_id"] = "avisos" } },
            };
            if (data is { Count: > 0 }) message["data"] = data;
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{sa.ProjectId}/messages:send")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return PushOutcome.Sent;
            var text = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode is 404 or 400 && (text.Contains("UNREGISTERED") || text.Contains("INVALID_ARGUMENT"))) return PushOutcome.InvalidToken;
            logger.LogWarning("FCM respondió {Status}: {Body}", (int)res.StatusCode, text.Length > 300 ? text[..300] : text);
            return PushOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or IOException)
        {
            logger.LogWarning(ex, "No se pudo enviar el aviso emergente");
            return PushOutcome.Failed;
        }
    }
}

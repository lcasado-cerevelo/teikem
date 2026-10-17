using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Auth:Turnstile — reto anti-robot de Cloudflare Turnstile para los logins de los portales (decidido con Luis, 2026-10-17; el login del staff queda como está). Apagado por omisión.
/// Se pide solo después de <see cref="RequireAfterFailures"/> intentos fallidos seguidos de la cuenta, así que no molesta a quien entra bien. SiteKey va al cliente; SecretKey NUNCA sale del servidor
/// (se pone en la configuración o en una variable de ambiente, no en el repositorio).
/// </summary>
public sealed class TurnstileOptions
{
    public bool Enabled { get; set; }
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }
    public int RequireAfterFailures { get; set; } = 3;
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}

public interface ITurnstileVerifier
{
    /// <summary>Encendido y con las dos llaves puestas.</summary>
    bool Enabled { get; }
    /// <summary>Intentos fallidos de la cuenta a partir de los cuales se exige el reto.</summary>
    int RequireAfterFailures { get; }
    string? SiteKey { get; }
    /// <summary>¿Es válido el token que dio el widget? Un fallo de red cuenta como inválido (el reto no se salta cayendo el servicio).</summary>
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct);
}

public sealed class CloudflareTurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options) : ITurnstileVerifier
{
    private TurnstileOptions O => options.Value;
    public bool Enabled => O.Enabled && !string.IsNullOrWhiteSpace(O.SiteKey) && !string.IsNullOrWhiteSpace(O.SecretKey);
    public int RequireAfterFailures => Math.Max(1, O.RequireAfterFailures);
    public string? SiteKey => O.SiteKey;

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        if (!Enabled) return true;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        try
        {
            var form = new List<KeyValuePair<string, string>> { new("secret", O.SecretKey!), new("response", token) };
            if (!string.IsNullOrWhiteSpace(remoteIp)) form.Add(new("remoteip", remoteIp));
            using var res = await http.PostAsync(O.VerifyUrl, new FormUrlEncodedContent(form), ct);
            if (!res.IsSuccessStatusCode) return false;
            var body = await res.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken: ct);
            return body?.Success == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}

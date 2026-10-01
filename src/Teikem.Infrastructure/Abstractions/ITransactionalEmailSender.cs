using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Correo transaccional (2026-09-30: código de verificación del primer ingreso). Implementación real: Brevo (variables de entorno
/// <c>Brevo__ApiKey</c> y <c>Brevo__FromEmail</c>). Sin ApiKey, solo deja constancia en el log del destinatario y el asunto,
/// NUNCA del contenido (lleva códigos).
/// </summary>
public interface ITransactionalEmailSender
{
    /// <summary>true si hay proveedor configurado (si no, el correo no sale).</summary>
    bool IsConfigured { get; }
    Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct);
}

/// <summary>Brevo, API de correo transaccional (POST https://api.brevo.com/v3/smtp/email con el encabezado api-key).</summary>
public sealed class BrevoEmailSender(HttpClient http, IConfiguration config, ILogger<BrevoEmailSender> logger) : ITransactionalEmailSender
{
    private string? ApiKey => config["Brevo:ApiKey"];
    private string? FromEmail => config["Brevo:FromEmail"];
    private string FromName => config["Brevo:FromName"] ?? "Teikem";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);

    public async Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Sin Brevo configurado (Brevo__ApiKey / Brevo__FromEmail): el correo '{Subject}' para {Email} no se envió.", subject, toEmail);
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.Add("api-key", ApiKey);
        req.Headers.Add("accept", "application/json");
        req.Content = JsonContent.Create(new
        {
            sender = new { email = FromEmail, name = FromName },
            to = new[] { new { email = toEmail, name = toName ?? toEmail } },
            subject,
            htmlContent,
        });
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            logger.LogError("Brevo rechazó el correo '{Subject}' para {Email}: {Status} {Body}", subject, toEmail, (int)res.StatusCode, body.Length > 300 ? body[..300] : body);
            throw new InvalidOperationException("No se pudo enviar el correo.");
        }
        logger.LogInformation("Correo '{Subject}' enviado a {Email} por Brevo.", subject, toEmail);
    }
}

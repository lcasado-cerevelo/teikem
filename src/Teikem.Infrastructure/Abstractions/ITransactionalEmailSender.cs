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

    /// <summary>
    /// <paramref name="companyName"/> (2026-10-08): la compañía a la que pertenece el correo; el remitente se muestra con el nombre de la compañía («Advance Logistics»); el «via Teikem» que se ve junto lo agrega el propio Gmail (autenticación del dominio), no la aplicación.
    /// Sin compañía (usuario en varias, o ninguna conocida) se usa el nombre general (Brevo:FromName, por defecto «Teikem»).
    /// La DIRECCIÓN del remitente es siempre la misma (Brevo:FromEmail).
    /// </summary>
    Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null);
}

public static class EmailSenderNames
{
    /// <summary>Máximo de caracteres de la compañía en el nombre del remitente (el resto se corta).</summary>
    public const int MaxCompanyChars = 80;

    /// <summary>Nombre que ve quien recibe el correo: el de la compañía (sin agregarle nada), o <paramref name="defaultName"/> sin compañía.</summary>
    public static string Resolve(string? companyName, string defaultName)
    {
        var clean = new string((companyName ?? string.Empty).Where(c => !char.IsControl(c) && c is not '<' and not '>' and not '"').ToArray()).Trim();
        if (clean.Length == 0) return defaultName;
        if (clean.Length > MaxCompanyChars) clean = clean[..MaxCompanyChars].TrimEnd();
        return clean;
    }
}

/// <summary>Brevo, API de correo transaccional (POST https://api.brevo.com/v3/smtp/email con el encabezado api-key).</summary>
public sealed class BrevoEmailSender(HttpClient http, IConfiguration config, ILogger<BrevoEmailSender> logger) : ITransactionalEmailSender
{
    private string? ApiKey => config["Brevo:ApiKey"];
    private string? FromEmail => config["Brevo:FromEmail"];
    private string FromName => config["Brevo:FromName"] ?? "Teikem";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);

    public async Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
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
            sender = new { email = FromEmail, name = EmailSenderNames.Resolve(companyName, FromName) },
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

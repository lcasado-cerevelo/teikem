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

    /// <summary>
    /// Lote 243: true si el correo va a un buzón local de desarrollo (Mailpit por SMTP) y nunca sale a la calle. Por omisión false
    /// (Brevo y los correos falsos de las pruebas no lo son).
    /// </summary>
    bool IsLocalSink => false;

    /// <summary>
    /// Lote 243: correo con adjuntos (p. ej. la firma de la orden de venta). La implementación por omisión llama al
    /// <see cref="SendAsync(string, string?, string, string, CancellationToken, string?)"/> de siempre y DESCARTA los adjuntos, para que
    /// las implementaciones que no los soportan (los correos falsos de las pruebas) sigan funcionando sin cambios.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken ct)
        => SendAsync(message.ToEmail, message.ToName, message.Subject, message.HtmlContent, ct, message.CompanyName);
}

/// <summary>Lote 243: un archivo adjunto de un correo (nombre con extensión, tipo MIME y contenido).</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Lote 243: un correo completo; <paramref name="Attachments"/> null o vacío = sin adjuntos.</summary>
public sealed record EmailMessage(
    string ToEmail,
    string? ToName,
    string Subject,
    string HtmlContent,
    string? CompanyName = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Lote 243: tope de los adjuntos de un correo (lógica pura; la prueba unitaria la cubre).</summary>
public static class EmailAttachmentRules
{
    /// <summary>Máximo de bytes sumando todos los adjuntos de un correo (5 MB).</summary>
    public const long MaxTotalBytes = 5L * 1024 * 1024;

    /// <summary>
    /// Reparte los adjuntos, EN ORDEN, entre los que caben (Kept) y los que no (Omitted): uno se guarda si con él la suma no pasa
    /// de <see cref="MaxTotalBytes"/>; si no, se omite y se sigue con el siguiente (uno más chico todavía puede caber). Un adjunto
    /// sin contenido o sin nombre se omite.
    /// </summary>
    public static (IReadOnlyList<EmailAttachment> Kept, IReadOnlyList<EmailAttachment> Omitted) Fit(IEnumerable<EmailAttachment>? attachments)
    {
        var kept = new List<EmailAttachment>();
        var omitted = new List<EmailAttachment>();
        long total = 0;
        foreach (var a in attachments ?? Array.Empty<EmailAttachment>())
        {
            if (a is null) continue;
            if (a.Content is null || a.Content.Length == 0 || string.IsNullOrWhiteSpace(a.FileName)) { omitted.Add(a); continue; }
            if (total + a.Content.Length > MaxTotalBytes) { omitted.Add(a); continue; }
            total += a.Content.Length;
            kept.Add(a);
        }
        return (kept, omitted);
    }
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

    /// <summary>Email:RedirectTo (2026-10-19): en test todo el correo va al buzón de Cerevelo; la etiqueta del asunto sale de Hosting:EnvironmentLabel.</summary>
    private EmailRedirectOptions Redirect()
    {
        var o = config.GetSection(EmailRedirectOptions.Section).Get<EmailRedirectOptions>() ?? new EmailRedirectOptions();
        if (string.IsNullOrWhiteSpace(o.Label)) o.Label = config["Hosting:EnvironmentLabel"];
        return o;
    }

    public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        => SendCoreAsync(toEmail, toName, subject, htmlContent, companyName, null, ct);

    /// <summary>Lote 243: con adjuntos. Sin adjuntos el JSON que se manda a Brevo es idéntico al de siempre.</summary>
    public Task SendAsync(EmailMessage message, CancellationToken ct)
        => SendCoreAsync(message.ToEmail, message.ToName, message.Subject, message.HtmlContent, message.CompanyName, message.Attachments, ct);

    private async Task SendCoreAsync(string toEmail, string? toName, string subject, string htmlContent, string? companyName, IReadOnlyList<EmailAttachment>? attachments, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Sin Brevo configurado (Brevo__ApiKey / Brevo__FromEmail): el correo '{Subject}' para {Email} no se envió.", subject, toEmail);
            return;
        }
        var r = EmailRedirectRules.Apply(Redirect(), toEmail, toName, subject, htmlContent);
        if (r.Redirected) logger.LogInformation("Correo '{Subject}' para {Email} redirigido a {Target} (Email:RedirectTo, ambiente de pruebas).", subject, toEmail, r.ToEmail);
        toEmail = r.ToEmail; toName = r.ToName; subject = r.Subject; htmlContent = r.HtmlContent;

        var (kept, omitted) = EmailAttachmentRules.Fit(attachments);
        // del adjunto omitido solo se anota el nombre y el tamaño, NUNCA el contenido
        foreach (var a in omitted)
            logger.LogWarning("El adjunto '{FileName}' ({Bytes} bytes) del correo '{Subject}' para {Email} se omitió: vacío o pasa el tope de {Max} bytes por correo.",
                a.FileName, a.Content?.Length ?? 0, subject, toEmail, EmailAttachmentRules.MaxTotalBytes);

        var sender = new { email = FromEmail, name = EmailSenderNames.Resolve(companyName, FromName) };
        var to = new[] { new { email = toEmail, name = toName ?? toEmail } };
        object payload = kept.Count == 0
            ? new { sender, to, subject, htmlContent }
            : new { sender, to, subject, htmlContent, attachment = kept.Select(a => new { name = a.FileName, content = Convert.ToBase64String(a.Content) }).ToArray() };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.Add("api-key", ApiKey);
        req.Headers.Add("accept", "application/json");
        req.Content = JsonContent.Create(payload, payload.GetType());
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

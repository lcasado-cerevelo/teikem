using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Lote 243: buzón local de desarrollo. Manda el correo por SMTP SIN TLS ni autenticación a un buzón que lo atrapa y lo muestra
/// (Mailpit: SMTP en 1025, página en http://localhost:8025), así que el correo se ve completo (HTML y adjuntos) y NUNCA sale a la calle.
/// Configuración: <c>Email:Smtp:Host</c> (sin él no está configurado), <c>Email:Smtp:Port</c> (1025 por omisión) y
/// <c>Email:Smtp:From</c> (teikem@localhost.test por omisión). Solo se usa en Development (ver <see cref="EmailSenderSelection"/>).
/// No aplica el desvío de TEST (<c>Email:RedirectTo</c>): el buzón local ya atrapa todo y las pruebas necesitan ver el destinatario real.
/// Del correo solo se anota en el log el destinatario y el asunto, NUNCA el contenido.
/// </summary>
public sealed class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : ITransactionalEmailSender
{
    /// <summary>Puerto SMTP por omisión (el de Mailpit).</summary>
    public const int DefaultPort = 1025;

    /// <summary>Dirección del remitente por omisión (dominio reservado para pruebas: no existe fuera de la máquina).</summary>
    public const string DefaultFrom = "teikem@localhost.test";

    /// <summary>Nombre del remitente cuando el correo no es de una compañía conocida.</summary>
    public const string DefaultFromName = "Teikem";

    private string? Host => config["Email:Smtp:Host"];

    private int Port => int.TryParse(config["Email:Smtp:Port"], out var p) && p is > 0 and <= 65535 ? p : DefaultPort;

    private string From => string.IsNullOrWhiteSpace(config["Email:Smtp:From"]) ? DefaultFrom : config["Email:Smtp:From"]!.Trim();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>Siempre true: este remitente solo entrega a un buzón local de desarrollo.</summary>
    public bool IsLocalSink => true;

    public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
        => SendAsync(new EmailMessage(toEmail, toName, subject, htmlContent, companyName), ct);

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Sin buzón local configurado (Email__Smtp__Host): el correo '{Subject}' para {Email} no se envió.", message.Subject, message.ToEmail);
            return;
        }
        using var mime = BuildMessage(message);
        using var client = new SmtpClient { Timeout = 30_000 };
        await client.ConnectAsync(Host!.Trim(), Port, SecureSocketOptions.None, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Correo '{Subject}' enviado a {Email} por el buzón local ({Host}:{Port}).", message.Subject, message.ToEmail, Host, Port);
    }

    /// <summary>
    /// Arma el mensaje MIME (público para probarlo sin red): remitente <c>Email:Smtp:From</c> con el nombre de la compañía
    /// (<see cref="EmailSenderNames.Resolve"/>), destinatario, asunto, cuerpo HTML y los adjuntos que caben en
    /// <see cref="EmailAttachmentRules.MaxTotalBytes"/> (los demás se omiten con una advertencia: nombre y tamaño, nunca el contenido).
    /// </summary>
    public MimeMessage BuildMessage(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(EmailSenderNames.Resolve(message.CompanyName, DefaultFromName), From));
        var to = message.ToEmail.Trim();
        mime.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(message.ToName) ? to : message.ToName.Trim(), to));
        mime.Subject = message.Subject ?? string.Empty;

        var body = new BodyBuilder { HtmlBody = message.HtmlContent ?? string.Empty };
        var (kept, omitted) = EmailAttachmentRules.Fit(message.Attachments);
        foreach (var a in omitted)
            logger.LogWarning("El adjunto '{FileName}' ({Bytes} bytes) del correo '{Subject}' para {Email} se omitió: vacío o pasa el tope de {Max} bytes por correo.",
                a.FileName, a.Content?.Length ?? 0, message.Subject, message.ToEmail, EmailAttachmentRules.MaxTotalBytes);
        foreach (var a in kept)
        {
            var type = ContentType.TryParse(a.ContentType ?? string.Empty, out var parsed) ? parsed : new ContentType("application", "octet-stream");
            body.Attachments.Add(a.FileName, a.Content, type);
        }
        mime.Body = body.ToMessageBody();
        return mime;
    }
}

/// <summary>
/// Lote 243: qué remitente de correo usa el API (lógica pura; la usa el registro de servicios). El buzón local por SMTP gana SOLO en
/// Development con <c>Email:Smtp:Host</c> puesto, aunque haya llave de Brevo: así un API de desarrollo con buzón local nunca manda
/// correo real. Fuera de Development el Host se ignora y se usa Brevo como siempre.
/// </summary>
public static class EmailSenderSelection
{
    /// <summary>Nombre del ambiente de desarrollo (igual que <c>Environments.Development</c> de ASP.NET Core).</summary>
    public const string DevelopmentEnvironment = "Development";

    /// <summary>true solo si el ambiente es Development y hay Host de SMTP.</summary>
    public static bool UseLocalSmtp(string? environmentName, string? smtpHost)
        => string.Equals(environmentName?.Trim(), DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(smtpHost);
}

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// 2026-10-22 — Correo de la invitación al portal de clientes (la de un usuario del cliente y la de su consignatario: las dos pasan por
/// <c>PortalUserService.InviteAsync</c>). Reglas puras del contenido: el enlace va a <c>Portal:WebBaseUrl</c> (la dirección del portal de cada ambiente) y
/// lleva el correo y el token; el texto se escapa porque trae nombres que escribe la gente.
/// </summary>
public static class InvitationEmail
{
    public const string Subject = "Lo invitaron al portal de clientes";
    public const string LinkPath = "/accept-invite";

    /// <summary>Enlace de la invitación: <c>{baseUrl}/accept-invite?email=…&amp;token=…</c> (el token y el correo codificados).</summary>
    public static string Link(string baseUrl, string email, string token)
        => $"{baseUrl.TrimEnd('/')}{LinkPath}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    public static string Html(string link, string clientName, string? companyName, DateTime expiresAtUtc)
    {
        var who = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(companyName) ? clientName : companyName.Trim());
        var client = WebUtility.HtmlEncode(clientName);
        var expires = expiresAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC";
        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px">
              <h2 style="color:#0B2C66">Lo invitaron al portal de clientes</h2>
              <p>{who} lo invitó a entrar al portal de clientes de <b>{client}</b>, donde puede seguir los envíos y ver sus facturas.</p>
              <p>Pulse el botón para elegir su contraseña y entrar:</p>
              <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#1F6FE5;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none">Elegir mi contraseña</a></p>
              <p>El enlace se puede usar una sola vez y vale hasta {expires}. Si usted no esperaba esta invitación, ignore este correo.</p>
            </div>
            """;
    }
}

/// <summary>
/// Envío real de la invitación al portal por el proveedor de correo (Brevo), con el enlace al portal del ambiente (<c>Portal:WebBaseUrl</c>). Sin esa dirección
/// (o sin proveedor) la invitación no sale y queda constancia en el log; el administrador puede «Reenviar invitación» cuando esté configurado. Un fallo del
/// proveedor no tumba la invitación: la cuenta ya quedó creada y el error lo registra el proveedor.
/// </summary>
public sealed class EmailInvitationSender(ITransactionalEmailSender email, IConfiguration config, ITenantContext tenant, TeikemDbContext db, ILogger<EmailInvitationSender> logger) : IInvitationSender
{
    public async Task SendInviteAsync(string toEmail, string token, string clientName, DateTime expiresAtUtc, CancellationToken ct)
    {
        var baseUrl = config["Portal:WebBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("Invitación al portal de clientes para {Email} (cliente '{Client}'); vence {ExpiresAtUtc:u}. Falta Portal:WebBaseUrl (la dirección del portal): el enlace no se envió.", toEmail, clientName, expiresAtUtc);
            return;
        }
        var companyName = tenant.TenantId is int tid
            ? await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId == tid).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;
        var link = InvitationEmail.Link(baseUrl, toEmail, token);
        try { await email.SendAsync(toEmail, null, InvitationEmail.Subject, InvitationEmail.Html(link, clientName, companyName, expiresAtUtc), ct, companyName); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("No se pudo enviar la invitación al portal para {Email} (cliente '{Client}'): {Message}", toEmail, clientName, ex.Message);
        }
    }
}

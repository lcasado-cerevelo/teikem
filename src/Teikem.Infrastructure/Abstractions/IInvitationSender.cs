using Microsoft.Extensions.Logging;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Envío del enlace de invitación al portal de clientes (Lote 2). Punto de integración para un proveedor de correo
/// (SMTP, SendGrid...). El token es el de restablecimiento de ASP.NET Identity (DataProtector, vida Portal:InviteHours):
/// solo debe viajar al destinatario, nunca a logs ni a auditoría.
/// </summary>
public interface IInvitationSender
{
    Task SendInviteAsync(string email, string token, string clientName, DateTime expiresAtUtc, CancellationToken ct);
}

/// <summary>
/// Implementación por defecto: no hay proveedor de correo todavía, así que solo deja constancia en el log del
/// destinatario, el cliente y la expiración. NUNCA escribe el token (en Development el API lo devuelve en la
/// respuesta con Portal:ReturnInviteTokenInResponse=true para poder probar el ciclo completo).
/// </summary>
public sealed class LoggingInvitationSender(ILogger<LoggingInvitationSender> logger) : IInvitationSender
{
    public Task SendInviteAsync(string email, string token, string clientName, DateTime expiresAtUtc, CancellationToken ct)
    {
        logger.LogInformation("Invitación al portal de clientes para {Email} (cliente '{Client}'); vence {ExpiresAtUtc:u}. Sin proveedor de correo: el enlace no se envió.",
            email, clientName, expiresAtUtc);
        return Task.CompletedTask;
    }
}

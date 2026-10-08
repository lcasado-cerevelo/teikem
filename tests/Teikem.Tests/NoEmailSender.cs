using Teikem.Infrastructure.Abstractions;

namespace Teikem.Tests;

/// <summary>Correo de prueba: guarda lo enviado en memoria (el contenido trae el código de verificación).</summary>
public sealed class NoEmailSender : ITransactionalEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];
    /// <summary>Compañía con la que se mandó cada correo (misma posición que <see cref="Sent"/>); null = remitente general.</summary>
    public List<string?> Companies { get; } = [];
    public bool IsConfigured => true;
    public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct, string? companyName = null)
    {
        Sent.Add((toEmail, subject, htmlContent));
        Companies.Add(companyName);
        return Task.CompletedTask;
    }
}

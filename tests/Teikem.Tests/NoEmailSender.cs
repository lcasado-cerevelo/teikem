using Teikem.Infrastructure.Abstractions;

namespace Teikem.Tests;

/// <summary>Correo de prueba: guarda lo enviado en memoria (el contenido trae el código de verificación).</summary>
public sealed class NoEmailSender : ITransactionalEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];
    public bool IsConfigured => true;
    public Task SendAsync(string toEmail, string? toName, string subject, string htmlContent, CancellationToken ct)
    {
        Sent.Add((toEmail, subject, htmlContent));
        return Task.CompletedTask;
    }
}

namespace Teikem.Infrastructure.Abstractions;

/// <summary>Plano 3 de auditoría: escribe SecurityEvent (login, MFA, permiso denegado, revocación...).</summary>
public interface ISecurityEventWriter
{
    /// <param name="eventType">Código de SecurityEventType.</param>
    /// <param name="outcome">Código de SecurityOutcome.</param>
    Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default);
}

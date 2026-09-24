using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Escribe SecurityEvent en su propio scope/DbContext para que quede registrado aunque la operación principal
/// falle o se revierta (un intento de acceso denegado tiene que quedar aunque el request termine en 403).
/// </summary>
public sealed class SecurityEventWriter(IServiceScopeFactory scopes, ITenantContext tenant, ILookupCache lookups, ILogger<SecurityEventWriter> logger) : ISecurityEventWriter
{
    public async Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
    {
        try
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.SecurityEventType, eventType, ct);
            var outcomeId = await lookups.GetIdAsync(LookupDomains.SecurityOutcome, outcome, ct);
            using var scope = scopes.CreateScope();
            var tc = (TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tc.TenantFilterDisabled = true;
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            db.SuppressAudit = true;
            db.SecurityEvents.Add(new SecurityEvent
            {
                TenantId = tenantId ?? tenant.TenantId, UserId = userId ?? tenant.UserId,
                EventTypeLookupId = typeId, OutcomeLookupId = outcomeId,
                IpAddress = tenant.IpAddress, UserAgent = tenant.UserAgent is { Length: > 300 } ua ? ua[..300] : tenant.UserAgent,
                DetailJson = detail is null ? null : JsonSerializer.Serialize(detail),
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo registrar SecurityEvent {Type}/{Outcome}", eventType, outcome);
        }
    }
}

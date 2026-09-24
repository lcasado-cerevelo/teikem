using Teikem.Domain.Common;

namespace Teikem.Domain.Audit;

/// <summary>Plano 1 de auditoría: qué cambió (diff campo a campo), escrito por el SaveChangesInterceptor.</summary>
public class AuditLog : ITenantScoped
{
    public long AuditLogId { get; set; }
    public int TenantId { get; set; }
    public int EntityTypeLookupId { get; set; }
    public int EntityId { get; set; }
    public int ActionLookupId { get; set; }
    public int? UserId { get; set; }
    public string? ChangesJson { get; set; }
    public Guid? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Catalogs.LookupCode? EntityType { get; set; }
    public Catalogs.LookupCode? Action { get; set; }
}

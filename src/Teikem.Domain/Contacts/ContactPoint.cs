using Teikem.Domain.Common;

namespace Teikem.Domain.Contacts;

/// <summary>Asociación polimórfica: N contactos tipados por entidad dueña (OwnerEntity catálogo + OwnerId).</summary>
[AuditEntity(Constants.EntityTypes.ContactPoint)]
public class ContactPoint : ITenantScoped, ISoftDeletable
{
    public int ContactPointId { get; set; }
    public int TenantId { get; set; }
    public int OwnerEntityLookupId { get; set; }
    public int OwnerId { get; set; }
    public int ContactTypeLookupId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Catalogs.LookupCode? OwnerEntity { get; set; }
    public Catalogs.LookupCode? ContactType { get; set; }
}

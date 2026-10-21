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
    /// <summary>Correo; o, en los tipos de teléfono (PHONE, MOBILE, FAX, WHATSAPP), el número nacional con SOLO dígitos (sin formato ni prefijo: el formato lo pone `Tenant.PhoneMask` al mostrarlo). Ver PhoneRules.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Prefijo internacional del teléfono, solo dígitos y sin «+» («1», «593»); null en los correos. Al guardar un teléfono siempre se llena (el de la compañía si no se indicó otro).</summary>
    public string? CountryCallingCode { get; set; }
    public string? Extension { get; set; }
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Catalogs.LookupCode? OwnerEntity { get; set; }
    public Catalogs.LookupCode? ContactType { get; set; }
}

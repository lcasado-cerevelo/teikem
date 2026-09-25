using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Cliente del operador logístico (el que contrata). Sus direcciones viven en Location por tipo y dueño:
/// CORPORATE = física, BILLING = postal (NULL = misma que la física), PICKUP/BOTH = almacenes / puntos de recogido,
/// DELIVERY = consignatarios. DefaultPickupLocationId apunta a uno de sus PICKUP/BOTH; NULL = se recoge en la corporativa.
/// Numeración (Lote 2): quién asigna el número de orden/factura y los patrones (NULL = patrón por defecto del sistema).
/// </summary>
[AuditEntity(Constants.EntityTypes.Client)]
public class Client : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int ClientId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public decimal? CreditLimit { get; set; }
    public int? PaymentTermLookupId { get; set; }
    public int? CurrencyLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    // --- Lote 2 ---
    /// <summary>Almacén (PICKUP/BOTH) donde se recoge por defecto; NULL = en la dirección corporativa.</summary>
    public int? DefaultPickupLocationId { get; set; }
    public bool ClientAssignsOrderNumber { get; set; }
    public bool ClientAssignsInvoiceNumber { get; set; }
    public string? OrderNumberFormat { get; set; }
    public string? InvoiceNumberFormat { get; set; }
    public string? PackageNumberFormat { get; set; }

    public StatusCode? Status { get; set; }
    public LookupCode? PaymentTerm { get; set; }
    public LookupCode? Currency { get; set; }
    public Location? DefaultPickupLocation { get; set; }
    public ICollection<ClientContact> Contacts { get; set; } = new List<ClientContact>();
    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    public ICollection<Location> Locations { get; set; } = new List<Location>();
}

/// <summary>
/// Persona de contacto del cliente. Sus teléfonos/correos son ContactPoint con OwnerEntity CLIENT_CONTACT.
/// No lleva TenantId: se alcanza SIEMPRE a través de su Client (filtrado por tenant), nunca por id suelto.
/// </summary>
[AuditEntity(Constants.EntityTypes.ClientContact)]
public class ClientContact : ISoftDeletable
{
    public int ClientContactId { get; set; }
    public int ClientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Role { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;

    public Client? Client { get; set; }
}

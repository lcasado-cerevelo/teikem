using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Consignatario o localización. ClientId NULL = compartida del operador. Por tipo: PICKUP/BOTH (almacenes),
/// DELIVERY (consignatarios), CORPORATE/BILLING (direcciones del cliente). GeoPoint (GEOGRAPHY) no se mapea en este lote.
/// AllowDupInvoice: el consignatario acepta facturas repetidas (lo consume el lote de Órdenes, R36).
/// </summary>
[AuditEntity(Constants.EntityTypes.Location)]
public class Location : ITenantScoped, ISoftDeletable
{
    public int LocationId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int? ClientId { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public int LocationTypeLookupId { get; set; }
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public int CountryLookupId { get; set; }
    /// <summary>Zona tarifaria (RateZone no se mapea en este lote; sin navegación).</summary>
    public int? RateZoneId { get; set; }
    public int DefaultServiceMinutes { get; set; }
    public TimeOnly? DefaultWindowStart { get; set; }
    public TimeOnly? DefaultWindowEnd { get; set; }
    public string? AccessNotes { get; set; }
    public string? DeliveryNotes { get; set; }
    public bool AllowDupInvoice { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Client? Client { get; set; }
    public LookupCode? LocationType { get; set; }
    public LookupCode? Country { get; set; }
}

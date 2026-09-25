using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Parada de la orden (PICKUP secuencia 1, DELIVERY secuencia 2 en este lote). Guarda el snapshot de la Location en el
/// momento de la captura (Snap*, ServiceMinutes, Notes = DeliveryNotes, ventana = RequestedDate + ventana por defecto; L236).
/// No lleva TenantId: se alcanza SOLO a través de su TransportOrder (filtrada por tenant); se audita bajo TRANSPORT_ORDER.
/// Su historial de StopStatus vive bajo EntityType ORDER_STOP. GeoPoint (GEOGRAPHY) no se mapea.
/// </summary>
[AuditEntity(Constants.EntityTypes.TransportOrder)]
public class OrderStop : IHasStatus
{
    public int OrderStopId { get; set; }
    public int TransportOrderId { get; set; }
    public int StopTypeLookupId { get; set; }
    public int Sequence { get; set; }
    public int? LocationId { get; set; }
    public string? SnapName { get; set; }
    public string SnapLine1 { get; set; } = string.Empty;
    public string? SnapLine2 { get; set; }
    public string SnapCity { get; set; } = string.Empty;
    public string? SnapState { get; set; }
    public string? SnapPostalCode { get; set; }
    public string SnapCountryCode { get; set; } = "PR";
    public int? GeocodeAccuracyLookupId { get; set; }
    public DateTime? WindowStartUtc { get; set; }
    public DateTime? WindowEndUtc { get; set; }
    public int ServiceMinutes { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Notes { get; set; }

    public TransportOrder? Order { get; set; }
    public LookupCode? StopType { get; set; }
    public Location? Location { get; set; }
    public StatusCode? Status { get; set; }
}

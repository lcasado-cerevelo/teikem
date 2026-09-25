using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Referencia externa de la orden (PO, BOL, etc.; LookupDomains.OrderRefType). Se administra dentro de POST/PATCH por
/// reemplazo completo (DECISIÓN 20). No lleva TenantId: se alcanza SOLO a través de su TransportOrder.
/// </summary>
[AuditEntity(Constants.EntityTypes.TransportOrder)]
public class OrderReference
{
    public int OrderReferenceId { get; set; }
    public int TransportOrderId { get; set; }
    public int RefTypeLookupId { get; set; }
    public string RefValue { get; set; } = string.Empty;
    public string? Source { get; set; }

    public TransportOrder? Order { get; set; }
    public LookupCode? RefType { get; set; }
}

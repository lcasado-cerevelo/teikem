using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Cita de muelle (módulo CROSSDOCK, D30): sin solapamiento por muelle; ARRIVED ocupa el muelle; ASN o Trip, nunca
/// ambos (CK_DockAppt_Ref).
/// </summary>
[AuditEntity(Constants.EntityTypes.DockAppointment)]
public class DockAppointment : ITenantScoped, IHasStatus
{
    public int DockAppointmentId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int WarehouseDockId { get; set; }
    public int DirectionLookupId { get; set; }
    public int? AsnId { get; set; }
    public int? TripId { get; set; }
    public DateTime ScheduledStartUtc { get; set; }
    public DateTime? ScheduledEndUtc { get; set; }
    public int StatusCodeId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }

    public StatusCode? Status { get; set; }
}

/// <summary>Plan de cruce de muelle XD-#####: OPEN → ALLOCATED → COMPLETED, con zona de staging CROSSDOCK o STAGING.</summary>
[AuditEntity(Constants.EntityTypes.CrossDockPlan)]
public class CrossDockPlan : ITenantScoped, IHasStatus
{
    public int CrossDockPlanId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int StatusCodeId { get; set; }
    public int? StagingZoneId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<CrossDockAllocation> Allocations { get; set; } = new List<CrossDockAllocation>();
}

/// <summary>
/// Asignación de una línea de recibo a una orden (D29). ConfirmedQty = cantidad cubierta al confirmar el recibo (NULL con el
/// recibo abierto); AllocatedQty − ConfirmedQty = faltante outbound visible (L320). Lo confirmado queda RESERVADO en staging
/// hasta moverlo. Sin TenantId: por su plan filtrado.
/// </summary>
[AuditEntity(Constants.EntityTypes.CrossDockPlan)]
public class CrossDockAllocation : IHasStatus
{
    public int CrossDockAllocationId { get; set; }
    public int CrossDockPlanId { get; set; }
    public int ReceiptLineId { get; set; }
    public int TransportOrderId { get; set; }
    public int? CargoLineId { get; set; }
    public decimal AllocatedQty { get; set; }
    public decimal? ConfirmedQty { get; set; }
    public int StatusCodeId { get; set; }
    public int? WarehouseTaskId { get; set; }
    public long? InventoryTransactionId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}

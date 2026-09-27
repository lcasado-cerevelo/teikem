using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Tarea de almacén de la cola unificada (R17, D41): PUTAWAY, REPLENISH, COUNT, CROSSDOCK (con handler) y PICK,
/// PACK, LOAD (sin handler en este lote). WarehouseTaskId pasa a INT (D19) para historial de estatus y RefId int. Sin
/// AuditLog: su rastro es EntityStatusHistory (WAREHOUSE_TASK). Las crea WarehouseTaskWriter (historial null → PENDING).
/// </summary>
public class WarehouseTask : ITenantScoped, IHasStatus
{
    public int WarehouseTaskId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int TaskTypeLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public int? ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public decimal? Quantity { get; set; }
    public int? FromBinId { get; set; }
    public int? ToBinId { get; set; }
    public int? RefEntityLookupId { get; set; }
    public int? RefId { get; set; }
    public int? AssignedToUserId { get; set; }
    public int Priority { get; set; } = 100;
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public StatusCode? Status { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Compras mínimas y faltantes (Ajustes de inventario). Firma posicional FIJA. La PO se identifica por PublicId; el
// proveedor y las líneas por id int, resueltos dentro de su encabezado filtrado.

public sealed record SupplierRequest(string? Name, string? ContactName = null, string? Phone = null, string? Email = null,
    string? PaymentTerm = null, string? Notes = null);

public sealed record SupplierPatchRequest(string? Name = null, string? ContactName = null, string? Phone = null, string? Email = null,
    string? PaymentTerm = null, string? Notes = null, string? RowVersion = null);

public sealed record SupplierDto(int Id, string Name, string? ContactName, string? Phone, string? Email, string? PaymentTermCode, string? Notes,
    bool IsActive, string RowVersion);

public sealed record PurchaseOrderLineRequest(Guid? ProductPublicId, decimal? QtyOrdered, decimal? UnitCost = null);

public sealed record PurchaseOrderCreateRequest(int? SupplierId, Guid? WarehousePublicId = null, DateOnly? OrderDate = null,
    DateOnly? ExpectedDate = null, string? Currency = null, string? Notes = null, IReadOnlyList<PurchaseOrderLineRequest>? Lines = null);

/// <summary>
/// PATCH de la orden de compra. SupplierId y WarehousePublicId (ajuste del 2026-09-30, al final: la firma anterior no cambia)
/// = mismos identificadores que el alta; null = sin cambio. Solo cambian mientras la orden está en DRAFT (409 en otro estatus
/// si el valor es distinto del actual); se validan como en el alta.
/// </summary>
public sealed record PurchaseOrderPatchRequest(DateOnly? ExpectedDate = null, string? Notes = null, IReadOnlyList<PurchaseOrderLineRequest>? Lines = null,
    string? RowVersion = null, int? SupplierId = null, Guid? WarehousePublicId = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record PurchaseOrderStatusRequest(string? Comment = null, string? RowVersion = null);

/// <summary>
/// Filtros de la lista de órdenes de compra. Lote 12: SupplierIds y WarehousePublicIds (varios) se combinan con los
/// singulares (compatibilidad): la orden aparece si su proveedor / almacén es cualquiera de los indicados.
/// </summary>
public sealed record PurchaseOrderQuery(string[]? Status = null, int? SupplierId = null, Guid? WarehousePublicId = null, DateOnly? From = null,
    DateOnly? To = null, string? Search = null, int Skip = 0, int Take = 100, int[]? SupplierIds = null, Guid[]? WarehousePublicIds = null);

public sealed record PurchaseOrderLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReceived,
    decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal LineTotal);

public sealed record PurchaseOrderDto(int Id, Guid PublicId, string Number, int SupplierId, string SupplierName, Guid WarehousePublicId,
    string WarehouseCode, DateOnly OrderDate, DateOnly? ExpectedDate, string StatusCode, string Status, string? CurrencyCode, string? Notes,
    decimal Total, bool HasShortage, bool CanEdit, bool CanCancel, bool CanDelete, bool IsActive, IReadOnlyList<PurchaseOrderLineDto> Lines,
    string RowVersion);

public sealed record PurchaseOrderPageDto(int Total, int Skip, int Take, IReadOnlyList<PurchaseOrderDto> Items);

public sealed record ShortageResolutionDto(int Id, string ActionCode, string Action, decimal Quantity, string? ReasonCode, string? Reason,
    string? Notes, Guid? ReorderPurchaseOrderPublicId, string? ReorderPurchaseOrderNumber, long? InventoryTransactionId, DateTime CreatedAtUtc,
    string? CreatedBy);

public sealed record ShortageLineDto(int PurchaseOrderLineId, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered,
    decimal QtyReceived, decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal PendingCost, IReadOnlyList<ShortageResolutionDto> Resolutions);

// Fase 7 (Ajustes de inventario): el almacén de la orden va al final (aditivo) para el ajuste manual (posición del almacén).
public sealed record PoShortageSummaryDto(Guid PublicId, string Number, int SupplierId, string SupplierName, int LinesWithShortage,
    decimal QtyPending, decimal PendingCost, Guid WarehousePublicId, string WarehouseCode);

public sealed record ShortageResolveRequest(string? Action, decimal? Quantity = null, string? Reason = null, string? Notes = null, int? BinId = null,
    int? LotId = null, LotInput? Lot = null, IReadOnlyList<string>? SerialNumbers = null, string? RowVersion = null);

public sealed record ShortageResolveResultDto(ShortageLineDto Line, PurchaseOrderDto PurchaseOrder, PurchaseOrderDto? Reorder);

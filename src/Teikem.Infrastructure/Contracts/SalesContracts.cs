namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 57 — órdenes de venta (módulo SALES_ORDERS). Permisos sales.view / sales.create / sales.cancel / sales.price.
// ======================================================================================================================

/// <summary>Una línea a vender. Con UnitPrice vacío se usa el precio de venta del producto; escribir otro exige sales.price.</summary>
public sealed record SalesOrderLineRequest(Guid? ProductPublicId = null, decimal? Quantity = null, decimal? UnitPrice = null, int? LotId = null);

/// <summary>
/// Orden de venta a crear o a reemplazar (PATCH, solo en borrador). WarehousePublicId vacío = el único almacén activo. ClearConsignee quita el consignatario.
/// </summary>
public sealed record SalesOrderRequest(Guid? WarehousePublicId = null, Guid? ClientPublicId = null, Guid? ConsigneePublicId = null, bool? ClearConsignee = null,
    DateOnly? RequiredDate = null, bool? ClearRequiredDate = null, string? Notes = null, IReadOnlyList<SalesOrderLineRequest>? Lines = null);

public sealed record SalesOrderLineDto(int Id, int LineNo, Guid ProductPublicId, string Sku, string ProductName, int? LotId, decimal QtyOrdered, decimal QtyReserved,
    decimal QtyBackordered, decimal QtyShipped, bool IsClosed, decimal UnitPrice, decimal LineSubtotal, decimal TaxRatePct, decimal TaxAmount, bool IsTaxExempt);

public sealed record SalesOrderListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, decimal Subtotal, decimal TaxAmount, decimal Total,
    int LineCount, bool HasBackorder, string? RequestedByName, DateTime CreatedAtUtc);

public sealed record SalesOrderDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid? ConsigneePublicId, string? ConsigneeName, Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, string? Notes,
    decimal Subtotal, decimal TaxAmount, decimal Total, bool HasBackorder, string? RequestedByName, DateTime? RequestedAtUtc, DateTime CreatedAtUtc,
    IReadOnlyList<SalesOrderLineDto> Lines);

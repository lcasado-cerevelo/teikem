namespace Teikem.Infrastructure.Contracts;

// Lote 156 — lo que el cliente crea desde su portal. Siempre a nombre del cliente de la sesión (no hay campo de cliente).

public sealed record PortalProductCreateRequest(string? Sku, string? Name, string? BaseUom = null, string? TrackingType = null, string? Barcode = null, string? Brand = null,
    string? Model = null, decimal? WeightKg = null, decimal? VolumeM3 = null, decimal? PurchaseCost = null);

/// <summary>Edición de un producto ya creado por el cliente: null = sin cambio; barcode/brand/model "" = quitarlos. El SKU, el seguimiento y la unidad no se cambian desde el portal.</summary>
public sealed record PortalProductPatchRequest(string? Name = null, string? Barcode = null, string? Brand = null, string? Model = null, decimal? WeightKg = null, decimal? VolumeM3 = null,
    decimal? PurchaseCost = null);

public sealed record PortalArrivalLineRequest(Guid? ProductPublicId, decimal? ExpectedQty, string? LotNumber = null, decimal? UnitPrice = null, DateOnly? ExpiryDate = null);

public sealed record PortalArrivalRequest(Guid? WarehousePublicId = null, string? Reference = null, DateOnly? ExpectedDate = null, IReadOnlyList<PortalArrivalLineRequest>? Lines = null);

public sealed record PortalExitLineRequest(Guid? ProductPublicId, decimal? Quantity, int? LotId = null);

public sealed record PortalExitRequest(Guid? WarehousePublicId = null, string? Reference = null, string? Destination = null, DateOnly? RequiredDate = null, string? Notes = null,
    IReadOnlyList<PortalExitLineRequest>? Lines = null);

public sealed record PortalExitHeaderRequest(string? Reference = null, string? Destination = null, DateOnly? RequiredDate = null, string? Notes = null);

public sealed record PortalExitLineModifyRequest(decimal? Quantity);

public sealed record PortalWarehouseOptionDto(Guid PublicId, string Code, string Name);

public sealed record PortalArrivalLineDto(string Sku, string ProductName, decimal ExpectedQty, string? LotNumber, decimal? UnitPrice, DateOnly? ExpiryDate = null, Guid? ProductPublicId = null, string? TrackingType = null);

/// <summary>Un aviso de llegada del cliente. CanCancel = todavía esperado y sin recibo abierto.</summary>
public sealed record PortalArrivalDto(int Id, Guid WarehousePublicId, string WarehouseCode, string? Reference, DateOnly? ExpectedDate, string StatusCode, string Status, bool CanCancel,
    IReadOnlyList<PortalArrivalLineDto> Lines, string Number = "");

public sealed record PortalExitLineDto(int LineId, string Sku, string ProductName, string? LotNumber, decimal QtyOrdered, decimal QtyReserved, decimal QtyBackordered, decimal QtyShipped);

/// <summary>Un aviso de salida del cliente (sin datos del personal ni de las recolecciones).</summary>
public sealed record PortalExitDto(Guid PublicId, string Number, string StatusCode, string Status, Guid WarehousePublicId, string WarehouseCode, string? Reference, string? Destination,
    DateOnly RequestedDate, DateOnly? RequiredDate, string? Notes, bool HasBackorder, bool CanCancel, IReadOnlyList<PortalExitLineDto> Lines);

public sealed record PortalExitListItemDto(Guid PublicId, string Number, string StatusCode, string Status, string WarehouseCode, string? Reference, string? Destination, DateOnly RequestedDate,
    DateOnly? RequiredDate, int LineCount, decimal QtyOrdered, decimal QtyShipped, bool HasBackorder);

public sealed record PortalExitPageDto(int Total, int Skip, int Take, IReadOnlyList<PortalExitListItemDto> Items);

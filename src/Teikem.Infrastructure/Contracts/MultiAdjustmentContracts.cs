namespace Teikem.Infrastructure.Contracts;

// Ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md).

/// <summary>Una posición contada: SystemQty es lo que vio la pantalla (se contrasta con el saldo bloqueado); CountedQty vacío = no se toca.</summary>
public sealed record MultiAdjustmentLineInput(Guid? ProductPublicId, int? BinId, int? LotId, decimal? SystemQty, decimal? CountedQty);

public sealed record MultiAdjustmentRequest(Guid? WarehousePublicId, string? Notes, IReadOnlyList<MultiAdjustmentLineInput>? Lines);

/// <summary>Una posición con existencia de un producto (para llenar la pantalla).</summary>
public sealed record MultiAdjustmentPositionDto(int BinId, string BinCode, string ZoneCode, int? LotId, string? LotNumber, decimal QtyOnHand, decimal QtyReserved);

public sealed record MultiAdjustmentProductDto(Guid ProductPublicId, string Sku, string Name, string TrackingTypeCode, bool IsActive,
    IReadOnlyList<MultiAdjustmentPositionDto> Positions);

public sealed record MultiAdjustmentDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, string Notes,
    int LineCount, int ProductCount, decimal UnitsUp, decimal UnitsDown, DateTime CreatedAtUtc, int? CreatedByUserId, string? CreatedByName);

public sealed record MultiAdjustmentLineDto(Guid ProductPublicId, string Sku, string ProductName, int BinId, string BinCode, string ZoneCode,
    int? LotId, string? LotNumber, decimal SystemQty, decimal CountedQty, decimal Difference, long? InventoryTransactionId);

public sealed record MultiAdjustmentDetailDto(MultiAdjustmentDto Header, IReadOnlyList<MultiAdjustmentLineDto> Lines);

public sealed record MultiAdjustmentQuery(Guid[]? WarehousePublicIds = null, DateOnly? From = null, DateOnly? To = null, int[]? UserIds = null,
    int Skip = 0, int Take = 50);

public sealed record MultiAdjustmentPageDto(int Total, int Skip, int Take, IReadOnlyList<MultiAdjustmentDto> Items);

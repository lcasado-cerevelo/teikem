using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0: la firma posicional COMPLETA de los contratos de Inventario y almacén queda fijada por reflexión (nombre,
/// tipo y nulabilidad de cada parámetro en orden), tal como la publicó el plan: ninguna pieza la cambia. Además: todos son
/// sealed records; los PATCH recogen llaves desconocidas en Extra; ninguna solicitud lleva TenantId, InventoryScope ni ids int
/// de encabezados con PublicId; InventoryScope.Any no fija dueño; los tipos de costura (WmsSeams) viven fuera de Contracts; y
/// OrderDetailDto no cambia.
/// </summary>
public class WmsContractsTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    private static string[] Signature(Type t)
        => t.GetConstructors().Single().GetParameters().Select(p => $"{TypeName(p.ParameterType, Nullability.Create(p))} {p.Name}").ToArray();

    private static string TypeName(Type t, NullabilityInfo info)
    {
        if (Nullable.GetUnderlyingType(t) is Type u) return TypeName(u, info) + "?";
        var name = t switch
        {
            _ when t == typeof(int) => "int",
            _ when t == typeof(long) => "long",
            _ when t == typeof(bool) => "bool",
            _ when t == typeof(decimal) => "decimal",
            _ when t == typeof(double) => "double",
            _ when t == typeof(string) => "string",
            _ when t.IsArray => TypeName(t.GetElementType()!, info.ElementType!) + "[]",
            _ when t.IsGenericType => $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select((a, i) => TypeName(a, info.GenericTypeArguments[i])))}>",
            _ => t.Name,
        };
        return !t.IsValueType && info.ReadState == NullabilityState.Nullable ? name + "?" : name;
    }

    public static readonly TheoryData<Type, string> Signatures = new()
    {
        { typeof(WarehouseCreateRequest), "string? Code, string? Name, string? Line1, string? City, string? State, string? PostalCode, string? Country, string? ReceivingMode" },   // Lote 16
        { typeof(WarehousePatchRequest), "string? Name, string? Line1, string? City, string? State, string? PostalCode, string? Country, string? RowVersion, string? ReceivingMode, int? DefaultReceivingBinId, bool? ClearDefaultReceivingBin" },   // Lote 16
        { typeof(WarehouseDeactivateRequest), "string? Comment, string? RowVersion" },
        { typeof(WarehouseDto), "int Id, Guid PublicId, string Code, string Name, string? Line1, string? City, string? State, string? PostalCode, string CountryCode, string StatusCode, string Status, bool IsActive, int ZoneCount, int BinCount, int DockCount, decimal QtyOnHand, string RowVersion, IReadOnlyList<string> ZoneTypeCodes, string ReceivingModeCode, string? ReceivingMode, int? DefaultReceivingBinId, string? DefaultReceivingBinCode" },   // Lote 16
        { typeof(WarehouseZoneRequest), "string? Code, string? Name, string? ZoneType" },
        { typeof(WarehouseZonePatchRequest), "string? Name, string? ZoneType, string? Code" },
        { typeof(WarehouseZoneDto), "int Id, string Code, string Name, string? ZoneTypeCode, string? ZoneType, bool IsActive, int BinCount, int OccupiedBinCount, long CapacityQty, decimal QtyOnHandInCapacityBins, decimal QtyOnHand, int BinsWithoutCapacity" },
        { typeof(WarehouseBinRequest), "int? ZoneId, string? Code, string? Aisle, string? Rack, string? Level, string? Position, decimal? MaxWeightKg, int? MaxCapacityQty" },
        { typeof(WarehouseBinPatchRequest), "string? Aisle, string? Rack, string? Level, string? Position, decimal? MaxWeightKg, bool? ClearMaxWeight, int? MaxCapacityQty, bool? ClearMaxCapacity" },
        { typeof(WarehouseBinQuery), "int? ZoneId, string? Search, bool IncludeInactive, bool OnlyWithStock, int[]? ZoneIds, string? Aisle, string? Rack, string? Level, string? Position, Guid[]? ProductPublicIds, string[]? Occupancy, int[]? BinIds, int Skip, int Take, bool? IsProvisional" },
        { typeof(WarehouseBinDto), "int Id, int ZoneId, string ZoneCode, string? ZoneTypeCode, string Code, string? Aisle, string? Rack, string? Level, string? Position, decimal? MaxWeightKg, bool IsActive, decimal QtyOnHand, int ProductCount, int? MaxCapacityQty, string Occupancy, Guid? SingleProductPublicId, string? SingleProductSku, string? SingleProductName, bool IsProvisional, int? ProvisionalCycleCountId, DateTime? ProvisionalCreatedAtUtc" },
        { typeof(WarehouseBinPageDto), "int Total, int Skip, int Take, IReadOnlyList<WarehouseBinDto> Items" },
        // Informe "Productos por posición".
        { typeof(BinProductsQuery), "WarehouseBinQuery Filter, int Skip, int Take" },
        { typeof(BinProductDto), "Guid ProductPublicId, string Sku, string Name, string? Barcode" },
        { typeof(BinProductsDto), "int BinId, string Code, int ZoneId, string ZoneCode, string? Aisle, string? Rack, string? Level, string? Position, bool IsActive, IReadOnlyList<BinProductDto> Products" },
        { typeof(BinProductsPageDto), "int Total, int Skip, int Take, DateTime GeneratedAtUtc, IReadOnlyList<BinProductsDto> Items" },
        { typeof(WarehouseBinCapacityRequest), "int[]? ZoneIds, string? Aisle, string? Rack, string? Level, string? Position, string? Search, int[]? BinIds, bool IncludeInactive, bool OnlyWithoutCapacity, bool AllBins, int? MaxCapacityQty, bool Clear" },
        { typeof(WarehouseBinCapacityResultDto), "int Matched, int Changed" },
        { typeof(WarehouseDockRequest), "string? Code, string? DockType" },
        { typeof(WarehouseDockPatchRequest), "string? DockType" },
        { typeof(WarehouseDockStatusRequest), "string? Status, string? Comment" },
        { typeof(WarehouseDockDto), "int Id, string Code, string DockTypeCode, string DockType, string StatusCode, string Status, string? StatusColor, bool IsActive" },
        { typeof(WarehouseDetailDto), "WarehouseDto Warehouse, IReadOnlyList<WarehouseZoneDto> Zones, IReadOnlyList<WarehouseDockDto> Docks" },
        { typeof(LotInput), "string? Number, DateOnly? ManufactureDate, DateOnly? ExpiryDate" },
        { typeof(ProductCreateRequest), "string? Sku, string? Name, Guid? OwnerClientPublicId, int? CategoryId, string? BaseUom, string? TrackingType, decimal? WeightKg, decimal? VolumeM3, string? Barcode, decimal? PurchaseCost, decimal? SalePrice, Guid? PreferredWarehousePublicId, int? PreferredBinId, decimal? MinQty, decimal? MinPickQty, decimal? MaxPickQty, string? Brand, string? Model" },
        { typeof(ProductPatchRequest), "string? Name, Guid? OwnerClientPublicId, bool? ClearOwner, int? CategoryId, bool? ClearCategory, string? BaseUom, string? TrackingType, decimal? WeightKg, decimal? VolumeM3, string? Barcode, bool? ClearBarcode, decimal? PurchaseCost, decimal? SalePrice, Guid? PreferredWarehousePublicId, int? PreferredBinId, bool? ClearPreferred, decimal? MinQty, decimal? MinPickQty, decimal? MaxPickQty, string? RowVersion, string? Brand, string? Model" },
        { typeof(ProductListQuery), "string? Search, int[]? CategoryIds, Guid? OwnerClientPublicId, bool? OwnOnly, bool ActiveOnly, Guid? WarehousePublicId, bool OnlyAvailable, int Skip, int Take, bool SelectorOrder, bool BelowMin, Guid[]? WarehousePublicIds, Guid[]? ProductPublicIds, string? Name, string[]? Brands, bool SerialOnly, bool SerialMissing, bool OnlyOnHand, bool Unavailable" },
        { typeof(ProductListItemDto), "int Id, Guid PublicId, string Sku, string Name, int? CategoryId, string? CategoryName, Guid? OwnerClientPublicId, string? OwnerName, bool IsOwn, string BaseUomCode, string TrackingTypeCode, string? Barcode, decimal? PurchaseCost, decimal? SalePrice, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? MinQty, bool IsBelowMin, bool IsActive, string? Brand, string? Model" },
        { typeof(ProductPageDto), "int Total, int Skip, int Take, IReadOnlyList<ProductListItemDto> Items" },
        { typeof(ProductDetailDto), "ProductListItemDto Product, decimal? WeightKg, decimal? VolumeM3, Guid? PreferredWarehousePublicId, string? PreferredWarehouseCode, int? PreferredBinId, string? PreferredBinCode, decimal? MinPickQty, decimal? MaxPickQty, bool HasMovements, string RowVersion" },
        { typeof(ProductCategoryRequest), "string? Name, int? ParentId" },
        { typeof(ProductCategoryPatchRequest), "string? Name, int? ParentId, bool? ClearParent" },
        { typeof(ProductCategoryDto), "int Id, string Name, int? ParentId, string Path, bool IsActive, int ProductCount" },
        { typeof(LotDto), "int Id, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, int? DaysToExpiry, decimal QtyOnHand, bool IsActive" },
        { typeof(SerialDto), "int Id, string SerialNumber, int? LotId, string? LotNumber, string? StatusCode, string? Status, Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode" },
        { typeof(BalanceQuery), "Guid[]? WarehousePublicIds, int[]? BinIds, Guid[]? ProductPublicIds, int[]? CategoryIds, string? LotNumber, bool IncludeZero, bool OnlyAvailable, string? Search, int Skip, int Take, bool ActiveProductsOnly" },
        { typeof(BalanceDto), "int Id, Guid WarehousePublicId, string WarehouseCode, int? BinId, string? BinCode, string? ZoneCode, string? ZoneTypeCode, Guid ProductPublicId, string Sku, string ProductName, string? CategoryName, string? OwnerName, bool IsOwn, int? LotId, string? LotNumber, DateOnly? ExpiryDate, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? CostValue, decimal? SaleValue, DateTime UpdatedAtUtc" },
        { typeof(BalancePageDto), "int Total, int Skip, int Take, decimal TotalOnHand, decimal TotalAvailable, IReadOnlyList<BalanceDto> Items" },
        { typeof(KardexQuery), "DateOnly? From, DateOnly? To, string[]? Types, Guid[]? WarehousePublicIds, int[]? BinIds, Guid[]? ProductPublicIds, int[]? CategoryIds, string? LotNumber, string? SerialNumber, string? RefEntity, int? RefId, string? Search, int Skip, int Take, string[]? Brands, string? Name, Guid[]? OwnerClientPublicIds, bool IncludeOwn, string[]? Reasons, string? Direction, Guid[]? FromWarehousePublicIds, Guid[]? ToWarehousePublicIds, bool ManualOnly" },
        { typeof(KardexRowDto), "long Id, DateTime CreatedAtUtc, string TypeCode, string Type, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, decimal SignedQuantity, string? FromWarehouseCode, string? FromBinCode, string? ToWarehouseCode, string? ToBinCode, string Position, string? LotNumber, string? SerialNumber, string? RefEntityCode, int? RefId, string? RefLabel, string? ReasonCode, string? Reason, string? Notes, int? UserId, string? UserName, string? OwnerName, string? CategoryName" },
        { typeof(KardexPageDto), "int Total, int Skip, int Take, IReadOnlyList<KardexRowDto> Items" },
        { typeof(AdjustmentRequest), "Guid? ProductPublicId, Guid? WarehousePublicId, int? BinId, decimal? Quantity, string? Reason, string? Notes, int? LotId, LotInput? Lot, IReadOnlyList<string>? SerialNumbers" },
        { typeof(TransferRequest), "Guid? ProductPublicId, int? FromBinId, int? ToBinId, decimal? Quantity, Guid? FromWarehousePublicId, Guid? ToWarehousePublicId, int? LotId, IReadOnlyList<string>? SerialNumbers, string? Notes" },
        { typeof(MovementResultDto), "IReadOnlyList<KardexRowDto> Transactions, IReadOnlyList<BalanceDto> Balances" },
        { typeof(GenealogyDestinationDto), "string RefEntityCode, int RefId, string? RefLabel, Guid? OrderPublicId, string? PackBatchNumber, string? ClientName, string? ConsigneeName, decimal Quantity" },
        { typeof(GenealogyDto), "Guid ProductPublicId, string Sku, string ProductName, int LotId, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, decimal QtyIn, decimal QtyOut, decimal QtyOnHand, IReadOnlyList<KardexRowDto> Movements, IReadOnlyList<GenealogyDestinationDto> Destinations" },
        { typeof(SerialTraceDto), "SerialDto Serial, Guid ProductPublicId, string Sku, IReadOnlyList<KardexRowDto> Movements, IReadOnlyList<StatusHistoryDto> StatusHistory" },
        { typeof(ReconciliationRowDto), "Guid ProductPublicId, string Sku, string WarehouseCode, string? BinCode, string? LotNumber, decimal LedgerQty, decimal BalanceQty" },
        { typeof(ReconciliationDto), "DateTime CheckedAtUtc, int BalancesChecked, IReadOnlyList<ReconciliationRowDto> Mismatches" },
        // Lote 14 (P0/P1): resumen y detalle del Kárdex, dueños, conciliación que guarda descuadres y búsqueda de posiciones.
        { typeof(KardexSummaryDto), "int Movements, int InCount, decimal InQty, int OutCount, decimal OutQty, int InternalCount" },
        { typeof(KardexDocumentDto), "string EntityCode, string EntityLabel, int Id, Guid? PublicId, string? Number, string? StatusCode, string? Status, DateTime? DateUtc, string? PartyName, string? Reference, KardexDocumentDto? Parent" },
        { typeof(KardexDetailDto), "KardexRowDto Transaction, string? OwnerName, string? CategoryName, DateOnly? LotExpiryDate, KardexDocumentDto? Document, IReadOnlyList<KardexRowDto> Related, bool RelatedTruncated" },
        { typeof(InventoryOwnerDto), "Guid? ClientPublicId, string Name, bool IsOwn" },
        { typeof(ReconciliationRunRequest), "Guid[]? ProductPublicIds" },
        { typeof(ReconciliationRunDto), "DateTime CheckedAtUtc, int ProductsChecked, int BalancesChecked, int Opened, int StillOpen, int SelfCorrected, IReadOnlyList<ReconciliationRowDto> Mismatches" },
        { typeof(InventoryDiscrepancyQuery), "string[]? Status, Guid[]? WarehousePublicIds, Guid[]? ProductPublicIds, int[]? CategoryIds, int[]? BinIds, string[]? Kinds, DateOnly? From, DateOnly? To, int Skip, int Take" },
        { typeof(InventoryDiscrepancyDto), "Guid PublicId, string KindCode, string Kind, Guid ProductPublicId, string Sku, string ProductName, Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode, int? LotId, string? LotNumber, decimal LedgerQty, decimal BalanceQty, decimal Difference, string StatusCode, string Status, string TriggerCode, string Trigger, DateTime DetectedAtUtc, DateTime LastCheckedAtUtc, int CheckCount, DateTime? ClosedAtUtc, string? ResolvedByName, string? ResolutionNotes, decimal? CorrectedFromQty, decimal? CorrectedToQty, long? LastTxnId, string RowVersion" },
        { typeof(InventoryDiscrepancyPageDto), "int Total, int Skip, int Take, int OpenCount, IReadOnlyList<InventoryDiscrepancyDto> Items" },
        { typeof(InventoryDiscrepancyDetailDto), "InventoryDiscrepancyDto Discrepancy, decimal? CurrentReserved, IReadOnlyList<KardexRowDto> RecentMovements, IReadOnlyList<StatusHistoryDto> History" },
        { typeof(DiscrepancyResolveRequest), "string? Action, string? Notes, string? RowVersion" },
        { typeof(BinSearchItemDto), "int Id, string Code, string? ZoneCode, Guid WarehousePublicId, string WarehouseCode, bool IsActive, bool IsProvisional" },
        { typeof(AsnLineRequest), "Guid? ProductPublicId, decimal? ExpectedQty, string? LotNumber" },
        { typeof(AsnCreateRequest), "Guid? WarehousePublicId, Guid? ClientPublicId, string? Reference, DateOnly? ExpectedDate, IReadOnlyList<AsnLineRequest>? Lines" },
        { typeof(AsnLineDto), "int Id, Guid ProductPublicId, string Sku, string ProductName, decimal ExpectedQty, string? LotNumber, int? PurchaseOrderLineId" },
        { typeof(AsnDto), "int Id, Guid WarehousePublicId, string WarehouseCode, Guid? ClientPublicId, string? ClientName, Guid? PurchaseOrderPublicId, string? PurchaseOrderNumber, string? Reference, DateOnly? ExpectedDate, string StatusCode, string Status, bool IsActive, IReadOnlyList<AsnLineDto> Lines, Guid? ReceiptPublicId, string? ReceiptNumber" },
        { typeof(AsnQuery), "Guid? WarehousePublicId, string[]? Status, Guid? ClientPublicId, string? Search, string? Reference, DateOnly? ExpectedFrom, DateOnly? ExpectedTo" },
        { typeof(ReceiptLineRequest), "Guid? ProductPublicId, decimal? ReceivedQty, LotInput? Lot, IReadOnlyList<string>? SerialNumbers, int? StagingBinId, decimal? ExpectedQty, int? TargetBinId, string? TargetBinCode" },   // Lote 16
        { typeof(ReceiptCreateRequest), "Guid? WarehousePublicId, string? Type, int? AsnId, Guid? PurchaseOrderPublicId, int? DockId, int? StagingBinId, IReadOnlyList<ReceiptLineRequest>? Lines, bool Confirm, string? Carrier, string? Reference, string? ReceivingMode" },   // Lote 16
        { typeof(ReceiptLineUpdateRequest), "decimal? ReceivedQty, LotInput? Lot, bool? ClearLot, IReadOnlyList<string>? SerialNumbers, int? StagingBinId, Guid? ProductPublicId, decimal? ExpectedQty, bool? ClearExpected, int? TargetBinId, bool? ClearTargetBin" },   // Lote 16
        { typeof(ReceiptHeaderUpdateRequest), "string? Type, Guid? WarehousePublicId, int? DockId, bool? ClearDock, int? StagingBinId, bool? ClearStagingBin, string? Carrier, string? Reference, string? RowVersion, string? ReceivingMode" },   // Lote 16
        { typeof(ReceiptConfirmRequest), "string? Comment, string? RowVersion" },
        { typeof(ReceiptQuery), "Guid? WarehousePublicId, string[]? Status, string[]? Types, DateOnly? From, DateOnly? To, Guid[]? ProductPublicIds, bool? HasVariance, string? Search, int Skip, int Take, string[]? Variance, string? Phase, bool IncludeLines" },
        { typeof(ReceiptListItemDto), "int Id, Guid PublicId, string Number, string TypeCode, string Type, string Origin, string? OriginRef, string? SenderName, Guid WarehousePublicId, string WarehouseCode, string? DockCode, string StatusCode, string Status, int LineCount, decimal ExpectedQty, decimal ReceivedQty, decimal VarianceQty, bool HasVariance, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc, string? Carrier, string? Reference, DateOnly? ExpectedDate, int? DefaultStagingBinId, string? DefaultStagingBinCode, int? DockId, bool IsOpen, int PendingPutawayCount, IReadOnlyList<ReceiptLineDto>? Lines, string ReceivingModeCode, string? ReceivingMode" },   // Lote 16
        { typeof(ReceiptLineDto), "int Id, int? AsnLineId, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode, decimal? ExpectedQty, decimal ReceivedQty, decimal VarianceQty, int? LotId, string? LotNumber, DateOnly? ExpiryDate, IReadOnlyList<string> SerialNumbers, int? StagingBinId, string? StagingBinCode, long? AdjustmentTxnId, decimal? UnitCost, decimal AllocatedToCrossDock, int? TargetBinId, string? TargetBinCode, string? TargetZoneTypeCode, decimal? TargetFreeQty" },   // Lote 16
        { typeof(ReceiptTargetSuggestionDto), "int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason, int? MaxCapacityQty, decimal QtyOnHand, decimal ClaimedQty, decimal? FreeQty, bool Fits" },   // Lote 16
        { typeof(ReceiptApplySuggestionsRequest), "string? RowVersion" },   // Lote 16
        { typeof(ReceiptApplySuggestionsResultDto), "ReceiptDetailDto Receipt, int Assigned, int WithoutSuggestion" },   // Lote 16
        { typeof(ReceiptDetailDto), "ReceiptListItemDto Header, IReadOnlyList<ReceiptLineDto> Lines, IReadOnlyList<WarehouseTaskDto> PutawayTasks, string RowVersion, bool CanDelete" },
        { typeof(ReceiptPageDto), "int Total, int Skip, int Take, IReadOnlyList<ReceiptListItemDto> Items" },
        { typeof(WarehouseTaskDto), "int Id, string TypeCode, string Type, string StatusCode, string Status, int Priority, Guid WarehousePublicId, string WarehouseCode, Guid? ProductPublicId, string? Sku, string? ProductName, int? LotId, string? LotNumber, string? SerialNumber, decimal? Quantity, int? FromBinId, string? FromBinCode, int? ToBinId, string? ToBinCode, string? RefEntityCode, int? RefId, string? RefLabel, int? AssignedToUserId, string? AssignedToName, bool CompletableFromQueue, DateTime CreatedAtUtc, DateTime? CompletedAtUtc" },
        { typeof(WarehouseTaskQuery), "Guid? WarehousePublicId, string[]? Types, string[]? Status, bool AssignedToMe, int? AssignedUserId, bool IncludeClosed, int Skip, int Take" },
        { typeof(WarehouseTaskPageDto), "int Total, int Skip, int Take, IReadOnlyList<WarehouseTaskDto> Items" },
        { typeof(TaskAssignRequest), "int? UserId" },
        { typeof(TaskCompleteRequest), "int? ToBinId, decimal? Quantity, IReadOnlyList<string>? SerialNumbers, string? Comment" },
        { typeof(TaskCancelRequest), "string? Comment" },
        { typeof(ReplenishmentRunRequest), "Guid? WarehousePublicId" },
        { typeof(ReplenishmentResultDto), "int ProductsEvaluated, int TasksCreated, int SkippedWithOpenTask, int SkippedNoReserve, IReadOnlyList<WarehouseTaskDto> Tasks" },
        { typeof(PutawaySuggestionDto), "int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason, string RotationClass, int? MaxCapacityQty, decimal? FreeQty" },   // Lote 16
        { typeof(CycleCountCreateRequest), "Guid? WarehousePublicId, int[]? ZoneIds, int[]? BinIds, Guid[]? ProductPublicIds, int[]? CategoryIds, bool AllowEmpty, bool AssignToMe" },
        { typeof(CycleCountDto), "int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status, int LineCount, int CountedLines, int? VarianceLines, decimal? NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive, int BinCount, string? BinCode, string? ZoneCode, string? OriginCode, int? TaskId, string? AssignedToName, string? Origin, DateTime? ChangesFromUtc, DateTime? ChangesToUtc, int? AssignedToUserId, int CorrectedLines, string? CreatedByName, IReadOnlyList<string>? CapturedByNames" },
        { typeof(CycleCountLineDto), "int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName, string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal? SystemQty, decimal? CountedQty, decimal? VarianceQty, IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal? CurrentQty, decimal? ReconciledSystemQty, bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId, string? Barcode, decimal? CapturedQty, string? CapturedByName, int? CapturedByUserId, DateTime? CapturedAtUtc, string? CorrectedByName, int? CorrectedByUserId, DateTime? CorrectedAtUtc, bool WasCorrected, bool BinIsProvisional" },
        { typeof(CycleCountDetailDto), "CycleCountDto Count, IReadOnlyList<CycleCountLineDto> Lines, string RowVersion, bool IsBlind, IReadOnlyList<CountSkippedLineDto>? SkippedLines" },
        { typeof(CycleCountQuery), "Guid[]? WarehousePublicIds, string[]? Status, DateOnly? From, DateOnly? To, int[]? BinIds, Guid[]? ProductPublicIds, int[]? CategoryIds, string? Search, int[]? ZoneIds, string[]? Origins, int Skip, int Take" },
        { typeof(CycleCountPageDto), "int Total, int Skip, int Take, IReadOnlyList<CycleCountDto> Items" },
        { typeof(CycleCountFromChangesRequest), "Guid? WarehousePublicId, DateTime? FromUtc, DateTime? ToUtc, int[]? ZoneIds, bool IncludeEmpty" },
        { typeof(CycleCountChangesPreviewDto), "DateTime FromUtc, DateTime ToUtc, int Movements, int Positions, int PositionsWithOpenCount, int PositionsInactive, int Lines, int MaxPositions, Guid WarehousePublicId, string WarehouseCode, int PositionsEmpty, DateTime? LastChangesToUtc, string? Problem" },
        { typeof(CycleCountBatchResultDto), "CycleCountChangesPreviewDto Window, IReadOnlyList<CycleCountDto> Counts" },
        { typeof(CycleCountLinesQuery), "int[]? BinIds, Guid[]? ProductPublicIds, int[]? CategoryIds, bool? OnlyVariance, bool? OnlyPending, string? Search" },
        { typeof(CountCaptureItem), "int LineId, decimal? CountedQty, IReadOnlyList<string>? SerialNumbers" },
        { typeof(CountCaptureRequest), "IReadOnlyList<CountCaptureItem>? Lines, string? RowVersion" },
        { typeof(CountAddLineRequest), "int? BinId, Guid? ProductPublicId, int? LotId, LotInput? Lot, decimal? CountedQty, IReadOnlyList<string>? SerialNumbers" },
        { typeof(CountReconcileRequest), "string? Comment, string? RowVersion" },
        { typeof(PickBatchLineRequest), "Guid? ProductPublicId, decimal? Quantity, int? BinId, int? LotId, IReadOnlyList<string>? SerialNumbers" },
        { typeof(PickBatchCreateRequest), "Guid? WarehousePublicId, IReadOnlyList<PickBatchLineRequest>? Lines, PickBatchPackRequest? Pack" },
        { typeof(PickBatchQuery), "DateOnly? From, DateOnly? To, Guid[]? ProductPublicIds, string[]? Status, string? OrderNumber, string? InvoiceNumber, string? Search, bool IncludeDeleted, int Skip, int Take" },
        { typeof(PickBatchLineDto), "int Id, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, int BinId, string BinCode, int? LotId, string? LotNumber, string? SerialNumber, decimal? UnitCost, long IssueTxnId, long? ReversalTxnId" },
        { typeof(PickBatchDto), "int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status, DateTime CollectedAtUtc, string? CollectedBy, DateTime? PackedAtUtc, Guid? OrderPublicId, string? OrderNumber, string? PackBatchNumber, string? ClientInvoiceNumber, string? OrderStatusCode, string? OrderStatus, string? ClientName, string? DisplayNumbers, bool CanPack, bool CanDelete, decimal TotalQty, decimal? TotalCost, IReadOnlyList<PickBatchLineDto> Lines, bool IsActive, string RowVersion" },
        { typeof(PickBatchPageDto), "int Total, int Skip, int Take, IReadOnlyList<PickBatchDto> Items" },
        { typeof(PickBatchPackRequest), "OrderCreateRequest? Order, string? RowVersion" },
        { typeof(PickBatchPackResultDto), "PickBatchDto Batch, OrderDetailDto Order" },
        { typeof(PickBatchDeleteRequest), "string? Comment, string? RowVersion" },
        { typeof(SupplierRequest), "string? Name, string? ContactName, string? Phone, string? Email, string? PaymentTerm, string? Notes" },
        { typeof(SupplierPatchRequest), "string? Name, string? ContactName, string? Phone, string? Email, string? PaymentTerm, string? Notes, string? RowVersion" },
        { typeof(SupplierDto), "int Id, string Name, string? ContactName, string? Phone, string? Email, string? PaymentTermCode, string? Notes, bool IsActive, string RowVersion" },
        { typeof(PurchaseOrderLineRequest), "Guid? ProductPublicId, decimal? QtyOrdered, decimal? UnitCost" },
        { typeof(PurchaseOrderCreateRequest), "int? SupplierId, Guid? WarehousePublicId, DateOnly? OrderDate, DateOnly? ExpectedDate, string? Currency, string? Notes, IReadOnlyList<PurchaseOrderLineRequest>? Lines" },
        { typeof(PurchaseOrderPatchRequest), "DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseOrderLineRequest>? Lines, string? RowVersion, int? SupplierId, Guid? WarehousePublicId" },
        { typeof(PurchaseOrderStatusRequest), "string? Comment, string? RowVersion" },
        { typeof(PurchaseOrderQuery), "string[]? Status, int? SupplierId, Guid? WarehousePublicId, DateOnly? From, DateOnly? To, string? Search, int Skip, int Take, int[]? SupplierIds, Guid[]? WarehousePublicIds" },
        { typeof(PurchaseOrderLineDto), "int Id, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReceived, decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal LineTotal" },
        { typeof(PurchaseOrderDto), "int Id, Guid PublicId, string Number, int SupplierId, string SupplierName, Guid WarehousePublicId, string WarehouseCode, DateOnly OrderDate, DateOnly? ExpectedDate, string StatusCode, string Status, string? CurrencyCode, string? Notes, decimal Total, bool HasShortage, bool CanEdit, bool CanCancel, bool CanDelete, bool IsActive, IReadOnlyList<PurchaseOrderLineDto> Lines, string RowVersion" },
        { typeof(PurchaseOrderPageDto), "int Total, int Skip, int Take, IReadOnlyList<PurchaseOrderDto> Items" },
        { typeof(ShortageResolutionDto), "int Id, string ActionCode, string Action, decimal Quantity, string? ReasonCode, string? Reason, string? Notes, Guid? ReorderPurchaseOrderPublicId, string? ReorderPurchaseOrderNumber, long? InventoryTransactionId, DateTime CreatedAtUtc, string? CreatedBy" },
        { typeof(ShortageLineDto), "int PurchaseOrderLineId, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReceived, decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal PendingCost, IReadOnlyList<ShortageResolutionDto> Resolutions" },
        { typeof(PoShortageSummaryDto), "Guid PublicId, string Number, int SupplierId, string SupplierName, int LinesWithShortage, decimal QtyPending, decimal PendingCost, Guid WarehousePublicId, string WarehouseCode" },
        { typeof(ShortageResolveRequest), "string? Action, decimal? Quantity, string? Reason, string? Notes, int? BinId, int? LotId, LotInput? Lot, IReadOnlyList<string>? SerialNumbers, string? RowVersion" },
        { typeof(ShortageResolveResultDto), "ShortageLineDto Line, PurchaseOrderDto PurchaseOrder, PurchaseOrderDto? Reorder" },
        { typeof(DockAppointmentRequest), "Guid? WarehousePublicId, int? DockId, string? Direction, DateTime? ScheduledStartUtc, DateTime? ScheduledEndUtc, int? AsnId, Guid? TripPublicId" },
        { typeof(DockAppointmentPatchRequest), "DateTime? ScheduledStartUtc, DateTime? ScheduledEndUtc, int? DockId" },
        { typeof(DockAppointmentStatusRequest), "string? Status, string? Comment" },
        { typeof(DockAppointmentQuery), "Guid? WarehousePublicId, int? DockId, DateTime? FromUtc, DateTime? ToUtc, string[]? Status" },
        { typeof(DockAppointmentDto), "int Id, Guid WarehousePublicId, int DockId, string DockCode, string DockTypeCode, string DockStatusCode, string DirectionCode, string Direction, DateTime ScheduledStartUtc, DateTime? ScheduledEndUtc, int? AsnId, string? AsnReference, Guid? TripPublicId, string? TripCode, string StatusCode, string Status, string? StatusColor" },
        { typeof(CrossDockPlanRequest), "Guid? WarehousePublicId, int? StagingZoneId" },
        { typeof(CrossDockAllocationRequest), "int? ReceiptLineId, Guid? OrderPublicId, decimal? Quantity, int? CargoLineId" },
        { typeof(CrossDockAllocationDto), "int Id, int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId, string Sku, string? LotNumber, Guid OrderPublicId, string PackBatchNumber, string ClientName, decimal Quantity, decimal? ConfirmedQty, decimal ShortQty, string StatusCode, string Status, int? TaskId, long? InventoryTransactionId" },
        { typeof(CrossDockPlanDto), "int Id, string Number, Guid WarehousePublicId, string WarehouseCode, int? StagingZoneId, string? StagingZoneCode, string StatusCode, string Status, int AllocationCount, decimal AllocatedQty, decimal MovedQty, decimal ShortQty, DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<CrossDockAllocationDto> Allocations" },
        { typeof(CrossDockCandidateDto), "int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId, string Sku, string? LotNumber, int? StagingBinId, string? StagingBinCode, decimal BaseQty, decimal AllocatedQty, decimal Allocatable" },
        { typeof(CrossDockMoveRequest), "string? Comment" },
        { typeof(InventoryScope), "int? OwnerClientId" },
        { typeof(OrderDeletionOptions), "string? AllowedSourceEntityType" },
    };

    private static readonly Type[] WithExtra = { typeof(ProductPatchRequest), typeof(PurchaseOrderPatchRequest), typeof(WarehouseBinPatchRequest), typeof(WarehouseDockPatchRequest), typeof(WarehousePatchRequest), typeof(WarehouseZonePatchRequest) };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Positional_signature_is_fixed(Type type, string expected)
        => Assert.Equal(expected.Split(", "), Signature(type));

    private static IEnumerable<Type> AllTypes() => Signatures.Select(row => (Type)row[0]);

    private static IEnumerable<Type> Requests() => AllTypes().Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal));

    [Fact]
    public void All_contracts_are_sealed_records()
    {
        foreach (var t in AllTypes())
        {
            Assert.True(t.IsSealed, $"{t.Name} no es sealed");
            Assert.NotNull(t.GetMethod("<Clone>$"));
        }
    }

    [Fact]
    public void Patch_requests_collect_unknown_keys_in_Extra()
    {
        foreach (var t in WithExtra)
        {
            var extra = t.GetProperty("Extra");
            Assert.NotNull(extra);
            Assert.NotNull(extra!.GetCustomAttribute<JsonExtensionDataAttribute>());
            Assert.Equal(typeof(IDictionary<string, JsonElement>), extra.PropertyType);
        }
        var req = JsonSerializer.Deserialize<ProductPatchRequest>("{\"sku\":\"X\",\"name\":\"Y\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(req.Extra!.ContainsKey("sku"));
        Assert.Equal("Y", req.Name);
        Assert.Null(typeof(ProductCreateRequest).GetProperty("Extra"));
    }

    [Fact]
    public void Requests_never_carry_the_tenant_the_scope_nor_internal_header_ids()
    {
        var headerIds = new[] { "WarehouseId", "ProductId", "ReceiptHeaderId", "ReceiptId", "PurchaseOrderId", "PickBatchId", "WarehouseIds", "ProductIds" };
        foreach (var t in Requests())
        {
            var props = t.GetProperties();
            Assert.DoesNotContain(props, p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(props, p => p.PropertyType == typeof(InventoryScope));
            Assert.DoesNotContain(props, p => headerIds.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Inventory_scope_any_has_no_owner()
    {
        Assert.Null(InventoryScope.Any.OwnerClientId);
        Assert.Equal(7, new InventoryScope(7).OwnerClientId);
    }

    [Fact]
    public void Seam_types_do_not_live_in_the_contracts_namespace()
    {
        foreach (var t in new[]
                 {
                     typeof(InventoryPosting), typeof(StockReservation), typeof(BalanceRebuildResult), typeof(WarehouseTaskSpec), typeof(PutawaySuggestion),
                     typeof(IWarehouseTaskHandler), typeof(IPurchaseOrderReceiving), typeof(IReceiptConfirmationParticipant), typeof(IOrderInventoryLines),
                     typeof(PurchaseOrderForReceipt), typeof(PurchaseOrderPendingLine), typeof(PurchaseOrderReceiptQty), typeof(OrderInventoryLine),
                 })
            Assert.Equal("Teikem.Infrastructure.Wms", t.Namespace);
        Assert.Equal(new[] { "string TxnType", "int ProductId", "decimal Quantity", "int? LotId", "int? SerialId", "string? SerialNumber", "int? FromWarehouseId",
            "int? FromBinId", "int? ToWarehouseId", "int? ToBinId", "string? RefEntityType", "int? RefId", "string? ReasonCode", "string? Notes", "bool FromReserved" },
            Signature(typeof(InventoryPosting)));
        Assert.Equal(new[] { "int ProductId", "int WarehouseId", "int BinId", "int? LotId", "decimal Quantity", "IReadOnlyList<string>? SerialNumbers" }, Signature(typeof(StockReservation)));
        // Lote 16: cupo, unidades en la posición y Fits al final (con valor por defecto); RotationClass sigue en su lugar.
        Assert.Equal(new[] { "int BinId", "string BinCode", "string ZoneCode", "string? ZoneTypeCode", "string ReasonCode", "string Reason", "string RotationClass",
            "int? MaxCapacityQty", "decimal BinQty", "bool Fits" }, Signature(typeof(PutawaySuggestion)));
        Assert.Equal("Teikem.Infrastructure.Wms", typeof(PutawaySuggestionOptions).Namespace);
        Assert.Equal(new[] { "int TransportOrderId", "int PickBatchId", "string PickBatchNumber", "int ProductId", "Guid ProductPublicId", "string Sku",
            "string ProductName", "int? LotId", "string? LotNumber", "int? SerialId", "string? SerialNumber", "decimal Quantity", "decimal? UnitCost", "decimal? SalePrice" },
            Signature(typeof(OrderInventoryLine)));
        var handler = typeof(IWarehouseTaskHandler);
        Assert.Equal(new[] { "CompleteAsync", "LockReferencesAsync", "get_NotFromQueueMessage", "get_RequiredPermission", "get_TaskType" },
            handler.GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(typeof(Task<decimal>), handler.GetMethod("CompleteAsync")!.ReturnType);
    }

    [Fact]
    public void Order_detail_dto_is_not_touched_by_the_lote()
    {
        // El Lote 6 no agrega parámetros a OrderDetailDto (D45: las líneas de producto salen de IOrderInventoryLines).
        var tail = typeof(OrderDetailDto).GetConstructors().Single().GetParameters().TakeLast(2).Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "AssignedTripPublicId", "AssignedTripCode" }, tail);
        Assert.DoesNotContain(typeof(OrderDetailDto).GetProperties(), p => p.Name.Contains("Product", StringComparison.Ordinal) || p.Name.Contains("PickBatch", StringComparison.Ordinal));
    }
    /// <summary>
    /// Auditoría automática (CLAUDE.md): cada entidad WMS con historial de cambios lleva [AuditEntity] con su EntityType; el
    /// saldo, el ledger (solo inserción; su rastro ES el Kárdex), la tarea y la serie (rastro = EntityStatusHistory + ledger)
    /// NO se auditan, para no duplicar volumen. Agregar o quitar el atributo rompe esta prueba.
    /// </summary>
    public static IEnumerable<object?[]> AuditMap() => new[]
    {
        new object?[] { typeof(Warehouse), EntityTypes.Warehouse },
        new object?[] { typeof(WarehouseZone), EntityTypes.Warehouse },
        new object?[] { typeof(WarehouseBin), EntityTypes.Warehouse },
        new object?[] { typeof(WarehouseDock), EntityTypes.WarehouseDock },
        new object?[] { typeof(ProductCategory), EntityTypes.ProductCategory },   // Lote 7A: propio, para no confundir su baja con la de un producto
        new object?[] { typeof(Product), EntityTypes.Product },
        new object?[] { typeof(InventoryLot), EntityTypes.Product },
        new object?[] { typeof(Supplier), EntityTypes.Supplier },
        new object?[] { typeof(PurchaseOrder), EntityTypes.PurchaseOrder },
        new object?[] { typeof(PurchaseOrderLine), EntityTypes.PurchaseOrder },
        new object?[] { typeof(PurchaseOrderShortageResolution), EntityTypes.PurchaseOrder },
        new object?[] { typeof(Asn), EntityTypes.Asn },
        new object?[] { typeof(AsnLine), EntityTypes.Asn },
        new object?[] { typeof(ReceiptHeader), EntityTypes.Receipt },
        new object?[] { typeof(ReceiptLine), EntityTypes.Receipt },
        new object?[] { typeof(CycleCount), EntityTypes.CycleCount },
        new object?[] { typeof(CycleCountLine), EntityTypes.CycleCount },
        new object?[] { typeof(PickBatch), EntityTypes.PickBatch },
        new object?[] { typeof(PickBatchLine), EntityTypes.PickBatch },
        new object?[] { typeof(DockAppointment), EntityTypes.DockAppointment },
        new object?[] { typeof(CrossDockPlan), EntityTypes.CrossDockPlan },
        new object?[] { typeof(CrossDockAllocation), EntityTypes.CrossDockPlan },
        new object?[] { typeof(StockBalance), null },
        new object?[] { typeof(InventoryTransaction), null },
        new object?[] { typeof(InventorySerial), null },
        new object?[] { typeof(WarehouseTask), null },
        new object?[] { typeof(InventoryDiscrepancy), EntityTypes.InventoryDiscrepancy },   // Lote 14: alta, estatus y resolución (cifras de revisión [NotAudited])
    };

    [Theory]
    [MemberData(nameof(AuditMap))]
    public void Wms_entities_are_audited_exactly_as_planned(Type entity, string? entityTypeCode)
    {
        var attr = entity.GetCustomAttribute<AuditEntityAttribute>();
        Assert.Equal(entityTypeCode, attr?.EntityTypeCode);
    }

    [Fact]
    public void The_audit_map_covers_every_wms_entity()
    {
        var mapped = AuditMap().Select(r => (Type)r[0]!).ToHashSet();
        var entities = typeof(Warehouse).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(Warehouse).Namespace && t.IsClass && !t.IsAbstract && t.IsPublic
                        && !(t.IsSealed && t.IsAbstract) && t.GetProperties().Any(pr => pr.Name.EndsWith("Id", StringComparison.Ordinal))
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();
        var missing = entities.Where(t => !mapped.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "Entidades WMS sin decisión de auditoría en el mapa: " + string.Join(", ", missing));
    }
}

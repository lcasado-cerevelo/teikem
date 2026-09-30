namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Inventario, Kárdex y trazabilidad. Firma posicional FIJA. Quantity del Kárdex = valor del ledger CON signo (D3);
// SignedQuantity = según la perspectiva del filtro de ubicación.

/// <summary>
/// Alcance por dueño del inventario (D44), con el mismo patrón que OrderScope: OwnerClientId null = operador interno (Any);
/// fijado = Portal (Lote 8): solo productos de ese cliente, el resto 404 sin oráculo. Tipo interno de servicio: nunca se
/// deserializa desde HTTP.
/// </summary>
public sealed record InventoryScope(int? OwnerClientId)
{
    public static readonly InventoryScope Any = new((int?)null);
}

public sealed record BalanceQuery(Guid[]? WarehousePublicIds = null, int[]? BinIds = null, Guid[]? ProductPublicIds = null,
    int[]? CategoryIds = null, string? LotNumber = null, bool IncludeZero = false, bool OnlyAvailable = false, string? Search = null,
    int Skip = 0, int Take = 200, bool ActiveProductsOnly = false);

public sealed record BalanceDto(int Id, Guid WarehousePublicId, string WarehouseCode, int? BinId, string? BinCode, string? ZoneCode,
    string? ZoneTypeCode, Guid ProductPublicId, string Sku, string ProductName, string? CategoryName, string? OwnerName, bool IsOwn,
    int? LotId, string? LotNumber, DateOnly? ExpiryDate, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? CostValue,
    decimal? SaleValue, DateTime UpdatedAtUtc);

public sealed record BalancePageDto(int Total, int Skip, int Take, decimal TotalOnHand, decimal TotalAvailable, IReadOnlyList<BalanceDto> Items);

/// <summary>
/// Filtros del Kárdex. Lote 12: Brands (marca del producto igual a alguna, sin distinguir mayúsculas) y Name (el nombre del
/// producto contiene el texto, sin distinguir mayúsculas), para que el Reporte de ajustes use los filtros de Productos.
/// </summary>
public sealed record KardexQuery(DateOnly? From = null, DateOnly? To = null, string[]? Types = null, Guid[]? WarehousePublicIds = null,
    int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, string? LotNumber = null, string? SerialNumber = null,
    string? RefEntity = null, int? RefId = null, string? Search = null, int Skip = 0, int Take = 200, string[]? Brands = null,
    string? Name = null);

/// <summary>Fila del Kárdex. Quantity = valor del ledger CON signo; SignedQuantity = según la perspectiva del filtro.</summary>
public sealed record KardexRowDto(long Id, DateTime CreatedAtUtc, string TypeCode, string Type, Guid ProductPublicId, string Sku,
    string ProductName, decimal Quantity, decimal SignedQuantity, string? FromWarehouseCode, string? FromBinCode, string? ToWarehouseCode,
    string? ToBinCode, string Position, string? LotNumber, string? SerialNumber, string? RefEntityCode, int? RefId, string? RefLabel,
    string? ReasonCode, string? Reason, string? Notes, int? UserId, string? UserName);

public sealed record KardexPageDto(int Total, int Skip, int Take, IReadOnlyList<KardexRowDto> Items);

/// <summary>Ajuste manual ±: Quantity CON signo (+ entra a la posición, − sale); motivo del catálogo AdjustmentReason.</summary>
public sealed record AdjustmentRequest(Guid? ProductPublicId, Guid? WarehousePublicId, int? BinId, decimal? Quantity, string? Reason,
    string? Notes = null, int? LotId = null, LotInput? Lot = null, IReadOnlyList<string>? SerialNumbers = null);

/// <summary>Transferencia entre posiciones o almacenes: una sola fila TRANSFER (D40).</summary>
public sealed record TransferRequest(Guid? ProductPublicId, int? FromBinId, int? ToBinId, decimal? Quantity, Guid? FromWarehousePublicId = null,
    Guid? ToWarehousePublicId = null, int? LotId = null, IReadOnlyList<string>? SerialNumbers = null, string? Notes = null);

public sealed record MovementResultDto(IReadOnlyList<KardexRowDto> Transactions, IReadOnlyList<BalanceDto> Balances);

public sealed record GenealogyDestinationDto(string RefEntityCode, int RefId, string? RefLabel, Guid? OrderPublicId, string? PackBatchNumber,
    string? ClientName, string? ConsigneeName, decimal Quantity);

public sealed record GenealogyDto(Guid ProductPublicId, string Sku, string ProductName, int LotId, string LotNumber, DateOnly? ManufactureDate,
    DateOnly? ExpiryDate, decimal QtyIn, decimal QtyOut, decimal QtyOnHand, IReadOnlyList<KardexRowDto> Movements,
    IReadOnlyList<GenealogyDestinationDto> Destinations);

public sealed record SerialTraceDto(SerialDto Serial, Guid ProductPublicId, string Sku, IReadOnlyList<KardexRowDto> Movements,
    IReadOnlyList<StatusHistoryDto> StatusHistory);

public sealed record ReconciliationRowDto(Guid ProductPublicId, string Sku, string WarehouseCode, string? BinCode, string? LotNumber,
    decimal LedgerQty, decimal BalanceQty);

public sealed record ReconciliationDto(DateTime CheckedAtUtc, int BalancesChecked, IReadOnlyList<ReconciliationRowDto> Mismatches);

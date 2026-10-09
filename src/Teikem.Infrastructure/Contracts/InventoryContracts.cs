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
/// Lote 14 (al final, con valor por defecto): From/To son días LOCALES de la compañía (hora de Puerto Rico); dueño
/// (OwnerClientPublicIds + IncludeOwn = productos propios), motivos de ajuste (Reasons), dirección IN/OUT con la perspectiva
/// de SignedQuantity, almacén de origen y de destino (cada uno contra su lado) y ManualOnly (movimientos sin documento).
/// </summary>
public sealed record KardexQuery(DateOnly? From = null, DateOnly? To = null, string[]? Types = null, Guid[]? WarehousePublicIds = null,
    int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, string? LotNumber = null, string? SerialNumber = null,
    string? RefEntity = null, int? RefId = null, string? Search = null, int Skip = 0, int Take = 200, string[]? Brands = null,
    string? Name = null, Guid[]? OwnerClientPublicIds = null, bool IncludeOwn = false, string[]? Reasons = null, string? Direction = null,
    Guid[]? FromWarehousePublicIds = null, Guid[]? ToWarehousePublicIds = null, bool ManualOnly = false);

/// <summary>
/// Fila del Kárdex. Quantity = valor del ledger CON signo; SignedQuantity = según la perspectiva del filtro. Lote 14:
/// OwnerName (cliente dueño o 'Propio') y CategoryName del producto.
/// </summary>
public sealed record KardexRowDto(long Id, DateTime CreatedAtUtc, string TypeCode, string Type, Guid ProductPublicId, string Sku,
    string ProductName, decimal Quantity, decimal SignedQuantity, string? FromWarehouseCode, string? FromBinCode, string? ToWarehouseCode,
    string? ToBinCode, string Position, string? LotNumber, string? SerialNumber, string? RefEntityCode, int? RefId, string? RefLabel,
    string? ReasonCode, string? Reason, string? Notes, int? UserId, string? UserName, string? OwnerName = null, string? CategoryName = null);

public sealed record KardexPageDto(int Total, int Skip, int Take, IReadOnlyList<KardexRowDto> Items);

/// <summary>
/// Lote 14 (D13) — resumen del Kárdex con los mismos filtros de la lista: movimientos, entradas (número y unidades), salidas
/// (número y unidades) e internos (transferencias sin filtro de ubicación o con los dos lados dentro), con la perspectiva de
/// SignedQuantity. En la pestaña Saldos, "En mano" y "Disponible" salen de BalancePageDto (TotalOnHand, TotalAvailable).
/// </summary>
public sealed record KardexSummaryDto(int Movements, int InCount, decimal InQty, int OutCount, decimal OutQty, int InternalCount);

/// <summary>
/// Lote 14 — documento de origen de un movimiento para abrirlo desde el detalle: EntityCode (EntityType), su etiqueta, id
/// interno, PublicId (recibo, recolección, orden de compra, orden de transporte y producto; el conteo se abre por id), número,
/// estatus, fecha, parte (cliente o proveedor), Reference (dato secundario: orden de la recolección, tipo de tarea, etc.) y
/// Parent (documento padre de una tarea de almacén).
/// </summary>
public sealed record KardexDocumentDto(string EntityCode, string EntityLabel, int Id, Guid? PublicId, string? Number, string? StatusCode,
    string? Status, DateTime? DateUtc, string? PartyName, string? Reference = null, KardexDocumentDto? Parent = null);

/// <summary>
/// Lote 14 — detalle de un movimiento: la fila, dueño, categoría, vencimiento del lote, documento de origen y los movimientos
/// relacionados (del mismo documento; sin documento, los del mismo asiento), con tope de 200 (RelatedTruncated).
/// </summary>
public sealed record KardexDetailDto(KardexRowDto Transaction, string? OwnerName, string? CategoryName, DateOnly? LotExpiryDate,
    KardexDocumentDto? Document, IReadOnlyList<KardexRowDto> Related, bool RelatedTruncated);

/// <summary>Lote 14 — dueño del inventario para el filtro: cliente (PublicId y nombre) o "Propio" (IsOwn, sin cliente).</summary>
public sealed record InventoryOwnerDto(Guid? ClientPublicId, string Name, bool IsOwn);

/// <summary>Lote 14 — "Ejecutar conciliación": productos elegidos (≤ 200) o, vacío, todo el tenant.</summary>
public sealed record ReconciliationRunRequest(Guid[]? ProductPublicIds = null);

/// <summary>
/// Lote 14 — resultado de una conciliación que PERSISTE descuadres: productos y saldos revisados, abiertos nuevos, abiertos que
/// siguen descuadrados, cerrados solos y las filas descuadradas (mismo formato que GET /inventory/reconciliation).
/// </summary>
public sealed record ReconciliationRunDto(DateTime CheckedAtUtc, int ProductsChecked, int BalancesChecked, int Opened, int StillOpen,
    int SelfCorrected, IReadOnlyList<ReconciliationRowDto> Mismatches);

/// <summary>
/// Lote 14 (P2, D14) — estado de la revisión automática en segundo plano para la compañía: encendida (configuración), con
/// consumidor activo, productos pendientes (en cola o esperando en el worker; 0 = al día), productos revisados, avisos
/// descartados por cola llena, hora de la última revisión, último error y productos cuya revisión falló. Contadores en
/// memoria desde el arranque del servidor.
/// </summary>
public sealed record ReconciliationStatusDto(bool Enabled, bool Consuming, int Pending, long Processed, long Dropped,
    DateTime? LastProcessedAtUtc, string? LastError, long Failed = 0);

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

// ---------------- 2026-10-08 — Daños ----------------

/// <summary>
/// Reportar un daño. <c>Origin</c> RECEIPT (llegó dañado: indique el recibo; esas unidades no entran como buenas) o WAREHOUSE (se dañó en el
/// almacén: indique la posición donde estaba). <c>Disposition</c> QUARANTINE (a una posición de cuarentena; si no se indica, la del almacén) o
/// DISCARD (se desecha de una vez). <c>Cause</c>: ARRIVED_DAMAGED, TRANSIT_ACCIDENT, WAREHOUSE_ACCIDENT u OTHER.
/// </summary>
public sealed record DamageReportRequest(string? Origin, Guid? WarehousePublicId, Guid? ProductPublicId, int? LotId, LotInput? Lot, int? FromBinId, decimal? Quantity,
    string? Cause, string? Disposition, int? QuarantineBinId = null, Guid? ReceiptPublicId = null, string? Notes = null, string? FinalDestination = null);

/// <summary>Dar salida (destino final obligatorio, nota opcional) o recuperar (posición de guardado a la que vuelve) lo que está en cuarentena.</summary>
/// <summary>FinalDestination (obligatorio al desechar lo que está en cuarentena): DISCARDED_WASTE, RETURNED_TO_SUPPLIER, DONATED, SOLD_AS_SALVAGE u otro del catálogo DamageFinalDestination.</summary>
public sealed record DamageResolveRequest(int? ToBinId = null, string? Notes = null, string? FinalDestination = null);

public sealed record DamageReportDto(int Id, Guid PublicId, string Code, string OriginCode, string Origin, string CauseCode, string Cause,
    Guid WarehousePublicId, string WarehouseCode, Guid ProductPublicId, string Sku, string ProductName, string? LotNumber, decimal Quantity,
    int? FromBinId, string? FromBinCode, int? QuarantineBinId, string? QuarantineBinCode, Guid? ReceiptPublicId, string? ReceiptNumber, string? Notes,
    string StatusCode, string Status, DateTime ReportedAtUtc, string? ReportedByName, DateTime? ResolvedAtUtc, string? ResolvedByName, string? ResolutionNotes,
    string? FinalDestinationCode = null, string? FinalDestination = null, bool IsReserved = false);

public sealed record DamageReportPageDto(int Total, int Skip, int Take, IReadOnlyList<DamageReportDto> Items);

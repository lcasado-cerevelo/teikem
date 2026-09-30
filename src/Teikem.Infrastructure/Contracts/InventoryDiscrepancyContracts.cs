namespace Teikem.Infrastructure.Contracts;

// Lote 14 (P1) — descuadres Kárdex ↔ saldo (D5). Firma posicional: solo se agrega al final, con valor por defecto.

/// <summary>
/// Filtros de la lista de descuadres: estatus (OPEN, RESOLVED, DISMISSED, SELF_CORRECTED), almacenes, productos, categorías
/// (con sus subcategorías), posiciones, tipo (BALANCE, PRODUCT_TOTAL) y fecha de detección (días locales de la compañía).
/// </summary>
public sealed record InventoryDiscrepancyQuery(string[]? Status = null, Guid[]? WarehousePublicIds = null, Guid[]? ProductPublicIds = null,
    int[]? CategoryIds = null, int[]? BinIds = null, string[]? Kinds = null, DateOnly? From = null, DateOnly? To = null, int Skip = 0,
    int Take = 50);

/// <summary>
/// Descuadre: clave (producto, almacén, posición, lote; sin almacén = total del producto), cifras de la última revisión
/// (Difference = saldo − Kárdex), estatus, origen, fechas, revisiones, resolución (quién, nota, de → a) y RowVersion (base64)
/// para resolver con control de concurrencia.
/// </summary>
public sealed record InventoryDiscrepancyDto(Guid PublicId, string KindCode, string Kind, Guid ProductPublicId, string Sku, string ProductName,
    Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode, int? LotId, string? LotNumber, decimal LedgerQty,
    decimal BalanceQty, decimal Difference, string StatusCode, string Status, string TriggerCode, string Trigger, DateTime DetectedAtUtc,
    DateTime LastCheckedAtUtc, int CheckCount, DateTime? ClosedAtUtc, string? ResolvedByName, string? ResolutionNotes,
    decimal? CorrectedFromQty, decimal? CorrectedToQty, long? LastTxnId, string RowVersion);

/// <summary>Página de descuadres; OpenCount = abiertos con los mismos filtros salvo el estatus.</summary>
public sealed record InventoryDiscrepancyPageDto(int Total, int Skip, int Take, int OpenCount, IReadOnlyList<InventoryDiscrepancyDto> Items);

/// <summary>Ficha: el descuadre, lo reservado hoy en la clave (o el producto), los últimos 20 movimientos y el historial de estatus.</summary>
public sealed record InventoryDiscrepancyDetailDto(InventoryDiscrepancyDto Discrepancy, decimal? CurrentReserved,
    IReadOnlyList<KardexRowDto> RecentMovements, IReadOnlyList<StatusHistoryDto> History);

/// <summary>
/// Resolver: Action REBUILD_BALANCE (el saldo toma lo que da el Kárdex) o DISMISS (descartar; nota obligatoria). Notes ≤ 500.
/// RowVersion = el de la ficha (409 si otro lo cambió).
/// </summary>
public sealed record DiscrepancyResolveRequest(string? Action, string? Notes, string? RowVersion);

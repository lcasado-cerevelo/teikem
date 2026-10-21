namespace Teikem.Infrastructure.Contracts;

// Lote 181 (H1) — contabilización de compras y de despachos.

/// <summary>Generar (o ver antes de generar) una corrida: de qué (PURCHASES o DISPATCHES), el período (días de la compañía) y una nota opcional.</summary>
public sealed record AccountingRunRequest(string? Kind, DateOnly? PeriodStart, DateOnly? PeriodEnd, string? Note = null);

public sealed record AccountingRunCancelRequest(string? Comment);

/// <summary>Una línea: lo que entró (recibo) o salió (despacho) con el costo ya congelado.</summary>
public sealed record AccountingLineDto(int Id, DateOnly DocDate, string DocumentNumber, string? ReferenceNumber, string? PartyName, string? WarehouseCode, string Sku, string ProductName, string? LotNumber,
    decimal Quantity, decimal? UnitCost, decimal? TotalCost, string? CostSource);

/// <summary>Lo que entraría en la corrida; no guarda nada.</summary>
public sealed record AccountingPreviewDto(string Kind, int LineCount, decimal TotalAmount, int UnpricedLineCount, IReadOnlyList<AccountingLineDto> Lines);

public sealed record AccountingRunListItemDto(Guid PublicId, string Kind, string Number, string StatusCode, string Status, string StatusColor, DateOnly PeriodStart, DateOnly PeriodEnd, int LineCount,
    decimal TotalAmount, int UnpricedLineCount, DateTime GeneratedAtUtc);

public sealed record AccountingRunDto(Guid PublicId, string Kind, string Number, string StatusCode, string Status, string StatusColor, DateOnly PeriodStart, DateOnly PeriodEnd, string Currency, int LineCount,
    decimal TotalAmount, int UnpricedLineCount, string? Note, DateTime GeneratedAtUtc, string? GeneratedByName, DateTime? AcceptedAtUtc, string? AcceptedByName, DateTime? ExportedAtUtc, string? ExportedByName,
    string RowVersion, IReadOnlyList<AccountingLineDto> Lines);

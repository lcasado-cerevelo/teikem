namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Lote 72 (P5) — POST /billing/runs. ClientPublicId vacío = todos los clientes con algo por facturar. Por omisión entra TODO lo pendiente hasta la fecha final (lo despachado antes del
/// período que no se facturó no se pierde); con OnlyInPeriod solo lo despachado dentro de [inicio, fin].
/// </summary>
public sealed record BillingRunRequest(Guid? ClientPublicId = null, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null, string? Note = null, bool? OnlyInPeriod = null);

public sealed record BillingSettingsDto(string SalesBillingTrigger, int InvoiceDueDays);

/// <summary>PUT /billing/settings: null = sin cambio.</summary>
public sealed record BillingSettingsRequest(string? SalesBillingTrigger = null, int? InvoiceDueDays = null);

public sealed record InvoiceListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly? IssueDate,
    DateOnly? DueDate, decimal Subtotal, decimal TaxAmount, decimal Total, decimal Paid, decimal Balance, int LineCount, int? RunId);

public sealed record InvoiceLineDto(int Id, string Description, string ChargeType, Guid? ProductPublicId, string? Sku, string? SalesOrderNumber, decimal Quantity, decimal UnitAmount,
    decimal? OriginalUnitAmount, bool IsAdjusted, string? AdjustmentNote, decimal LineSubtotal, decimal? TaxRatePct, decimal TaxAmount, bool IsTaxExempt);

public sealed record InvoiceDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly? IssueDate,
    DateOnly? DueDate, string Currency, decimal Subtotal, decimal TaxAmount, decimal Total, decimal Paid, decimal Balance, int? RunId, string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines);

public sealed record BillingRunDto(int Id, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, string StatusLabel, string StatusColor, Guid? ClientPublicId, string? ClientName, string? Note,
    int InvoiceCount, decimal TotalAmount, DateTime GeneratedAtUtc, DateTime? ReviewedAtUtc, DateTime? ApprovedAtUtc, DateTime? ExportedAtUtc, IReadOnlyList<InvoiceListItemDto> Invoices);

public sealed record BillingRunListItemDto(int Id, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, string StatusLabel, string StatusColor, string? ClientName, int InvoiceCount,
    decimal TotalAmount, DateTime GeneratedAtUtc);

/// <summary>PATCH de una línea de factura: el nuevo monto unitario y el motivo (obligatorio).</summary>
public sealed record InvoiceLineAdjustRequest(decimal? UnitAmount = null, string? Note = null);

public sealed record InvoiceVoidRequest(string? Comment = null);

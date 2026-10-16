namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Lote 72 (P5) — POST /billing/runs. ClientPublicId vacío = todos los clientes con algo por facturar. Por omisión entra TODO lo pendiente hasta la fecha final (lo despachado antes del
/// período que no se facturó no se pierde); con OnlyInPeriod solo lo despachado dentro de [inicio, fin]. Lote 74: IncludeProducts (venta de producto) e IncludeFreight (flete de las órdenes de
/// transporte) son true por omisión; al menos uno debe ir.
/// </summary>
public sealed record BillingRunRequest(Guid? ClientPublicId = null, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null, string? Note = null, bool? OnlyInPeriod = null, bool? IncludeProducts = null, bool? IncludeFreight = null);

public sealed record BillingSettingsDto(string SalesBillingTrigger, int InvoiceDueDays);

/// <summary>PUT /billing/settings: null = sin cambio.</summary>
public sealed record BillingSettingsRequest(string? SalesBillingTrigger = null, int? InvoiceDueDays = null);

public sealed record InvoiceListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly? IssueDate,
    DateOnly? DueDate, decimal Subtotal, decimal TaxAmount, decimal Total, decimal Paid, decimal Balance, int LineCount, int? RunId);

public sealed record InvoiceLineDto(int Id, string Description, string ChargeType, Guid? ProductPublicId, string? Sku, string? SalesOrderNumber, decimal Quantity, decimal UnitAmount,
    decimal? OriginalUnitAmount, bool IsAdjusted, string? AdjustmentNote, decimal LineSubtotal, decimal? TaxRatePct, decimal TaxAmount, bool IsTaxExempt, string? TransportOrderNumber = null, int? RateComponentId = null);

public sealed record InvoiceDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, DateOnly? IssueDate,
    DateOnly? DueDate, string Currency, decimal Subtotal, decimal TaxAmount, decimal Total, decimal Paid, decimal Balance, int? RunId, string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<PaymentDto>? Payments = null);

public sealed record BillingRunDto(int Id, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, string StatusLabel, string StatusColor, Guid? ClientPublicId, string? ClientName, string? Note,
    int InvoiceCount, decimal TotalAmount, DateTime GeneratedAtUtc, DateTime? ReviewedAtUtc, DateTime? ApprovedAtUtc, DateTime? ExportedAtUtc, IReadOnlyList<InvoiceListItemDto> Invoices);

public sealed record BillingRunListItemDto(int Id, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, string StatusLabel, string StatusColor, string? ClientName, int InvoiceCount,
    decimal TotalAmount, DateTime GeneratedAtUtc);

/// <summary>PATCH de una línea de factura: el nuevo monto unitario y el motivo (obligatorio).</summary>
public sealed record InvoiceLineAdjustRequest(decimal? UnitAmount = null, string? Note = null);

public sealed record InvoiceVoidRequest(string? Comment = null);

// ---------------------------------------------------------------- Lote 75 — pagos y estado de cuenta

/// <summary>POST /billing/invoices/{id}/payments: monto (hasta 2 decimales, sin pasar del saldo), método (ACH, CHECK, CARD, CASH), fecha del pago (hoy por omisión) y referencia (cheque, confirmación…).</summary>
public sealed record PaymentRequest(decimal? Amount = null, string? Method = null, DateOnly? PaidDate = null, string? Reference = null);

public sealed record PaymentVoidRequest(string? Reason = null);

public sealed record PaymentDto(int Id, decimal Amount, string Method, string MethodLabel, DateOnly PaidDate, string? Reference, DateTime? VoidedAtUtc, string? VoidReason);

/// <summary>Un movimiento del estado de cuenta: la factura emitida (débito) o un pago (crédito), con el saldo corrido. Kind = INVOICE o PAYMENT.</summary>
public sealed record StatementLineDto(DateOnly Date, string Kind, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance, Guid? InvoicePublicId);

/// <summary>Saldos abiertos por antigüedad al día de consulta: por vencer y vencidos de 1–30, 31–60, 61–90 y más de 90 días.</summary>
public sealed record AgingDto(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);

public sealed record StatementDto(Guid ClientPublicId, string ClientName, DateOnly From, DateOnly To, decimal OpeningBalance, decimal TotalInvoiced, decimal TotalPaid, decimal ClosingBalance,
    IReadOnlyList<StatementLineDto> Lines, AgingDto Aging);

public sealed record ReceivableRowDto(Guid ClientPublicId, string ClientName, int OpenInvoices, decimal Balance, AgingDto Aging, DateOnly? OldestDueDate);

public sealed record ReceivablesDto(DateOnly AsOf, IReadOnlyList<ReceivableRowDto> Rows, AgingDto Total);

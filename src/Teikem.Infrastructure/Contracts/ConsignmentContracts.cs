namespace Teikem.Infrastructure.Contracts;

// Lote 224 (P6 del pedido de vendedores) — Contabilidad → Facturación → pestaña Consignación.

/// <summary>
/// Una factura con algo en consignación. Consigned = lo que se dejó (las líneas de órdenes de venta en consignación, con su IVU y su cargo de envío); Returned = lo devuelto sin
/// vender (notas de crédito aprobadas o aplicadas de esas líneas, p. ej. por la devolución de venta); Paid = pagos de consignación no anulados; Balance = lo que queda por pagar.
/// InvoiceTotal / InvoiceBalance = la factura entera (puede llevar también líneas que no son de consignación).
/// </summary>
public sealed record ConsignmentInvoiceListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientCode,
    string ClientName, DateOnly? IssueDate, DateOnly? DueDate, IReadOnlyList<string> OrderNumbers, decimal Consigned, decimal Returned, decimal Paid, decimal Balance,
    decimal InvoiceTotal, decimal InvoiceBalance, int PaymentCount, DateOnly? LastPaidDate);

/// <summary>Pago de consignación. LinkedToInvoicePayment = también quedó como pago de la factura (false = la factura ya estaba pagada: solo se informó lo vendido).</summary>
public sealed record ConsignmentPaymentDto(Guid PublicId, decimal Amount, DateOnly PaidDate, string Method, string MethodLabel, string? Reference, string? RecordedByName,
    DateTime RecordedAtUtc, bool LinkedToInvoicePayment, bool IsVoided, DateTime? VoidedAtUtc, string? VoidReason);

/// <summary>Línea en consignación de la factura: lo dejado (con IVU) y lo devuelto de ella.</summary>
public sealed record ConsignmentLineDto(int InvoiceLineId, string OrderNumber, string Description, decimal Quantity, decimal Amount, decimal Returned);

public sealed record ConsignmentInvoiceDto(ConsignmentInvoiceListItemDto Invoice, IReadOnlyList<ConsignmentLineDto> Lines, IReadOnlyList<ConsignmentPaymentDto> Payments);

/// <summary>
/// POST /billing/consignment/invoices/{id}/payments: monto (hasta 2 decimales, sin pasar del saldo en consignación ni del saldo de la factura), método (ACH, CHECK, CARD, CASH), fecha
/// del pago (hoy por omisión; no futura ni anterior a la emisión) y referencia (cheque, confirmación…; hasta 80).
/// </summary>
public sealed record ConsignmentPaymentRequest(decimal? Amount = null, string? Method = null, DateOnly? PaidDate = null, string? Reference = null);

public sealed record ConsignmentPaymentVoidRequest(string? Reason = null);

/// <summary>Reporte «Consignado por cliente»: qué se le dejó, qué devolvió, qué pagó y qué queda (facturas emitidas, vencidas o pagadas).</summary>
public sealed record ConsignmentClientRowDto(Guid ClientPublicId, string ClientCode, string ClientName, int InvoiceCount, int OpenInvoiceCount, decimal Consigned, decimal Returned,
    decimal Paid, decimal Balance, DateOnly? OldestOpenIssueDate);

public sealed record ConsignmentByClientDto(IReadOnlyList<ConsignmentClientRowDto> Items, decimal Consigned, decimal Returned, decimal Paid, decimal Balance, int OpenInvoiceCount);

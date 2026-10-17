namespace Teikem.Infrastructure.Contracts;

// Lote 79 (P6) — notas de crédito.

/// <summary>
/// Una línea de la nota. Con factura: InvoiceLineId (la línea que se acredita), y opcionalmente Quantity y UnitAmount (por omisión lo que se facturó de esa línea que aún no se ha acreditado: toda la
/// cantidad al monto de la línea); acreditar menos monto unitario es un ajuste de precio. Sin factura: Description, Quantity, UnitAmount y ChargeType (o ProductPublicId) son obligatorios.
/// </summary>
public sealed record CreditNoteLineRequest(int? InvoiceLineId = null, string? Description = null, decimal? Quantity = null, decimal? UnitAmount = null, string? ChargeType = null, Guid? ProductPublicId = null);

/// <summary>
/// POST /billing/credit-notes: la nota en borrador. Con InvoicePublicId el cliente sale de la factura; sin ella hace falta ClientPublicId (y, si se acredita una factura de otro sistema, ExternalInvoiceRef).
/// Reason: RETURN, PRICE_ADJUSTMENT, SERVICE_FAILURE o BILLING_ERROR. PUT cambia el motivo, la nota y las líneas (reemplaza todas) mientras esté en borrador.
/// </summary>
public sealed record CreditNoteRequest(Guid? ClientPublicId = null, Guid? InvoicePublicId = null, string? ExternalInvoiceRef = null, string? Reason = null, string? Note = null,
    IReadOnlyList<CreditNoteLineRequest>? Lines = null);

public sealed record CreditApplyRequest(Guid? InvoicePublicId = null, decimal? Amount = null);

public sealed record CreditVoidRequest(string? Reason = null);

public sealed record CreditNoteLineDto(int Id, int? InvoiceLineId, string Description, string? ChargeType, string? Sku, decimal Quantity, decimal UnitAmount, decimal LineSubtotal, decimal? TaxRatePct,
    decimal TaxAmount, bool IsTaxExempt);

public sealed record CreditApplicationDto(int Id, Guid InvoicePublicId, string InvoiceNumber, decimal Amount, DateOnly AppliedDate);

public sealed record CreditNoteDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, Guid? InvoicePublicId, string? InvoiceNumber,
    string? ExternalInvoiceRef, string Reason, string ReasonLabel, string Currency, decimal Subtotal, decimal TaxAmount, decimal Total, decimal Applied, decimal Balance, string? Note, DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc, IReadOnlyList<CreditNoteLineDto> Lines, IReadOnlyList<CreditApplicationDto> Applications);

public sealed record CreditNoteListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName, string? InvoiceNumber, string Reason,
    string ReasonLabel, decimal Total, decimal Applied, decimal Balance, DateTime CreatedAtUtc);

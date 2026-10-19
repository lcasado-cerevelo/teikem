using Teikem.Domain.Common;

namespace Teikem.Domain.Billing;

/// <summary>Constantes de la facturación (Lote 72, P5). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class BillingCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la factura (sembrado desde el seed original).</summary>
    public const string InvoiceEntityType = "INVOICE";
    /// <summary>EntityType de la corrida de facturación.</summary>
    public const string RunEntityType = "BILLING_RUN";
    public const string InvoiceStatusDomain = "InvoiceStatus";
    public const string RunStatusDomain = "BillingRunStatus";
    public const string ChargeTypeDomain = "ChargeType";
    /// <summary>Lote 126: tipo de cargo del cotejo de la factura del cliente (cuando el contrato lo cobra).</summary>
    public const string InvoiceCheck = "INVOICE_CHECK";
    public const string PaymentMethodDomain = "PaymentMethod";
    /// <summary>Tipo de cargo de la venta de producto (sembrado en el seed original).</summary>
    public const string ProductSale = "PRODUCT_SALE";
    /// <summary>Contador de números de factura (NumberSequence.Kind), uno por compañía.</summary>
    public const string NumberKind = "BILLINVOICE";
    public const string NumberPattern = "FAC-#####";
}

public static class InvoiceStatuses
{
    public const string Draft = "DRAFT";
    public const string Issued = "ISSUED";
    public const string Paid = "PAID";
    public const string Overdue = "OVERDUE";
    public const string Void = "VOID";
}

public static class BillingRunStatuses
{
    public const string Generated = "GENERATED";
    public const string Reviewed = "REVIEWED";
    public const string Approved = "APPROVED";
    public const string Exported = "EXPORTED";
}

/// <summary>Base de facturación del producto vendido (ajuste de la compañía): al despachar (recoger) o al entregar.</summary>
public static class SalesBillingTriggers
{
    public const string OnDispatch = "ON_DISPATCH";
    public const string OnDelivery = "ON_DELIVERY";
    public static readonly string[] All = { OnDispatch, OnDelivery };
}

/// <summary>
/// Corrida de facturación: generar → revisar → aprobar (AAL2) → exportar. Congela el cálculo al generar. ClientId = corrida de un solo cliente (NULL = todos con algo por facturar).
/// </summary>
[AuditEntity(BillingCodes.RunEntityType)]
public class BillingRun : ITenantScoped, IHasStatus
{
    public int BillingRunId { get; set; }
    public int TenantId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int InvoiceCount { get; set; }
    public decimal TotalAmount { get; set; }
    public int? ClientId { get; set; }
    public string? Note { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? ExportedAtUtc { get; set; }
    public int? ExportedBy { get; set; }
}

/// <summary>
/// Factura al cliente. Una por cliente y corrida. Total = Subtotal + Impuesto (columna calculada en la base). Nunca se borra: se anula (VOID) y libera lo que había facturado.
/// </summary>
[AuditEntity(BillingCodes.InvoiceEntityType)]
public class Invoice : ITenantScoped, IHasStatus
{
    public int InvoiceId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int? ContractId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int StatusCodeId { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int CurrencyLookupId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public int? BillingRunId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
}

/// <summary>
/// Línea de factura. PRODUCT_SALE = venta de producto de una línea de orden de venta (SalesOrderLineId) con la copia de su impuesto; el ajuste manual cambia UnitAmount y conserva
/// el calculado en OriginalUnitAmount.
/// </summary>
public class InvoiceLine
{
    public int InvoiceLineId { get; set; }
    public int InvoiceId { get; set; }
    public int? TransportOrderId { get; set; }
    public int? ProductId { get; set; }
    public int ChargeTypeLookupId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitAmount { get; set; }
    public int? RateComponentId { get; set; }
    public int? TaxRateId { get; set; }
    public decimal? TaxRatePct { get; set; }
    public decimal TaxAmount { get; set; }
    public bool IsTaxExempt { get; set; }
    public int? SalesOrderLineId { get; set; }
    /// <summary>El monto unitario como lo calculó el sistema al generar; NULL = nunca se ajustó.</summary>
    public decimal? OriginalUnitAmount { get; set; }
    public string? AdjustmentNote { get; set; }
    /// <summary>Lote 115: la línea es el cargo por devolución de la orden (se libera aparte del flete al anular).</summary>
    public bool IsReturnFee { get; set; }
}

/// <summary>Pago recibido de una factura. Una factura se salda (PAID) cuando sus pagos cubren el total.</summary>
[AuditEntity(BillingCodes.InvoiceEntityType)]
public class Payment : ITenantScoped
{
    public int PaymentId { get; set; }
    public int TenantId { get; set; }
    public int InvoiceId { get; set; }
    public int PaymentMethodLookupId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string? Reference { get; set; }
    /// <summary>Lote 75: quién lo registró; un pago no se borra: se anula (VoidedAtUtc) con motivo y deja de contar en el saldo.</summary>
    public int? RecordedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

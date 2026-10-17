using Teikem.Domain.Common;

namespace Teikem.Domain.Billing;

/// <summary>Constantes de las notas de crédito (Lote 79, P6). Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class CreditCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus de la nota de crédito (sembrado en la sección 2026-10-16 (v) de update.sql).</summary>
    public const string EntityType = "CREDIT_NOTE";
    public const string StatusDomain = "CreditNoteStatus";
    public const string ReasonDomain = "CreditReason";
    /// <summary>Contador de números de nota de crédito (NumberSequence.Kind), uno por compañía.</summary>
    public const string NumberKind = "CREDITNOTE";
    public const string NumberPattern = "NC-#####";
}

public static class CreditNoteStatuses
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    /// <summary>Todo su monto ya se aplicó a facturas (terminal).</summary>
    public const string Applied = "APPLIED";
    public const string Void = "VOID";
}

public static class CreditReasons
{
    public const string Return = "RETURN";
    public const string PriceAdjustment = "PRICE_ADJUSTMENT";
    public const string ServiceFailure = "SERVICE_FAILURE";
    public const string BillingError = "BILLING_ERROR";
    public static readonly string[] All = { Return, PriceAdjustment, ServiceFailure, BillingError };
}

/// <summary>
/// Nota de crédito al cliente: lo que se le acredita de una factura (devolución, ajuste de precio, falla del servicio, error de facturación) o, sin factura de Teikem, de una factura externa. DRAFT → APPROVED
/// (AAL2) → APPLIED (todo su monto aplicado a facturas) o VOID. Aprobarla NO mueve inventario (la devolución ya lo hizo al recibirse); lo que no se aplica a facturas queda como saldo a favor del cliente.
/// </summary>
[AuditEntity(CreditCodes.EntityType)]
public class CreditNote : ITenantScoped, IHasStatus, IAuditStamped
{
    public int CreditNoteId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string CreditNumber { get; set; } = string.Empty;
    public int ClientId { get; set; }
    /// <summary>Factura de Teikem que se acredita; NULL = factura externa (ExternalInvoiceRef) o ajuste sin factura.</summary>
    public int? InvoiceId { get; set; }
    public string? ExternalInvoiceRef { get; set; }
    public int ReasonLookupId { get; set; }
    public int CurrencyLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public string? Note { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<CreditNoteLine> Lines { get; set; } = new();
}

/// <summary>Línea de la nota: lo que se acredita de una línea de factura (InvoiceLineId) o un concepto libre. El impuesto es el de la línea original (copia), proporcional a lo acreditado.</summary>
public class CreditNoteLine
{
    public int CreditNoteLineId { get; set; }
    public int CreditNoteId { get; set; }
    public int? InvoiceLineId { get; set; }
    public int? ProductId { get; set; }
    public int? ChargeTypeLookupId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitAmount { get; set; }
    public int? TaxRateId { get; set; }
    public decimal? TaxRatePct { get; set; }
    public decimal TaxAmount { get; set; }
    public bool IsTaxExempt { get; set; }
}

/// <summary>Cuánto de una nota aprobada se aplicó a una factura abierta. Reduce el saldo de la factura igual que un pago.</summary>
[AuditEntity(CreditCodes.EntityType)]
public class CreditApplication : ITenantScoped
{
    public int CreditApplicationId { get; set; }
    public int TenantId { get; set; }
    public int CreditNoteId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public int? AppliedBy { get; set; }
}

/// <summary>Reglas puras de las notas de crédito (sin EF; CreditNoteService las traduce a 400/404/409/422). Los mensajes son públicos: el manual (capítulo 19) y la FAQ los citan.</summary>
public static class CreditNoteRules
{
    public const int MaxNoteLength = 500;
    public const int MaxRefLength = 80;
    public const int MaxDescriptionLength = 250;
    public const int MaxLines = 200;

    public const string ClientRequired = "Indique el cliente de la nota de crédito.";
    public const string ReasonRequired = "Indique el motivo de la nota de crédito.";
    public const string ReasonInvalid = "El motivo de la nota de crédito no existe.";
    public const string LinesRequired = "Agregue al menos una línea a la nota de crédito.";
    public const string TooManyLines = "La nota de crédito no puede tener más de 200 líneas.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string RefTooLong = "La referencia de la factura externa no puede pasar de 80 caracteres.";
    public const string InvoiceNotFound = "Factura no encontrada.";
    public const string InvoiceNotCreditable = "Solo se acredita una factura emitida, vencida o pagada.";
    public const string InvoiceClientMismatch = "La factura no es de ese cliente.";
    public const string InvoiceLineRequired = "Cada línea de una nota sobre una factura debe indicar la línea de la factura que acredita.";
    public const string InvoiceLineNotFound = "La línea no es de esa factura.";
    public const string InvoiceLineOnFree = "Una nota sin factura no acredita líneas de factura.";
    public const string DescriptionRequired = "Escriba la descripción de la línea.";
    public const string DescriptionTooLong = "La descripción no puede pasar de 250 caracteres.";
    public const string QuantityInvalid = "La cantidad debe ser mayor que cero (hasta 3 decimales).";
    public const string AmountInvalid = "El monto unitario debe ser mayor que cero con hasta 4 decimales.";
    public const string ChargeTypeRequired = "Indique el tipo de cargo de la línea.";
    public const string ChargeTypeInvalid = "El tipo de cargo no existe.";
    public const string NothingToCredit = "La nota de crédito no puede quedar en cero.";
    public const string NotFound = "Nota de crédito no encontrada.";
    public const string NeedsDraft = "Solo se cambia una nota de crédito en borrador.";
    public const string ApproveNeedsDraft = "Solo se aprueba una nota de crédito en borrador.";
    public const string VoidNeedsOpen = "Solo se anula una nota en borrador o aprobada.";
    public const string VoidHasApplications = "La nota ya se aplicó a una factura: no se puede anular.";
    public const string ApplyNeedsApproved = "Solo se aplica una nota de crédito aprobada con saldo.";
    public const string ApplyAmountRequired = "Indique el monto a aplicar.";
    public const string ApplyAmountInvalid = "El monto a aplicar debe ser mayor que cero y tener hasta 2 decimales.";
    public const string ApplyInvoiceNotOpen = "Solo se aplica a una factura emitida o vencida con saldo.";
    public const string ApplyOtherClient = "La nota y la factura deben ser del mismo cliente.";
    public const string VoidReasonRequired = "Escriba el motivo para anular la nota de crédito.";
    public const string VoidReasonTooLong = "El motivo no puede pasar de 250 caracteres.";
    public const string InvoiceHasCredits = "La factura tiene notas de crédito aprobadas o aplicadas: no se puede anular.";

    public static string ExceedsCreditable(string description, decimal creditable)
        => $"No se puede acreditar más de lo facturado: de «{description}» quedan {creditable:0.00} por acreditar.";

    public static string ExceedsNoteBalance(decimal amount, decimal balance) => $"El monto ({amount:0.00}) es mayor que el saldo de la nota ({balance:0.00}).";
    public static string ExceedsInvoiceBalance(decimal amount, decimal balance) => $"El monto ({amount:0.00}) es mayor que el saldo de la factura ({balance:0.00}).";

    /// <summary>Lo que todavía se puede acreditar de una línea de factura: su subtotal menos lo ya acreditado (nunca negativo).</summary>
    public static decimal Creditable(decimal lineSubtotal, decimal alreadyCredited) => Math.Max(0m, Math.Round(lineSubtotal - alreadyCredited, 4, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Impuesto de lo que se acredita de una línea: la tasa de la línea original (copia) sobre lo acreditado, en 2 decimales; una línea exenta no lleva. Nunca pasa del impuesto que le queda a la línea
    /// (el de la línea menos el ya acreditado), así la suma de las notas no devuelve más impuesto del que se cobró.
    /// </summary>
    public static decimal TaxFor(decimal creditedValue, decimal? taxRatePct, bool exempt, decimal lineTax, decimal taxAlreadyCredited)
    {
        if (exempt || taxRatePct is null or <= 0m || creditedValue <= 0m) return 0m;
        var tax = Math.Round(creditedValue * taxRatePct.Value / 100m, 2, MidpointRounding.AwayFromZero);
        return Math.Min(tax, Math.Max(0m, Math.Round(lineTax - taxAlreadyCredited, 2, MidpointRounding.AwayFromZero)));
    }

    public static decimal LineSubtotal(decimal quantity, decimal unitAmount) => Math.Round(quantity * unitAmount, 4, MidpointRounding.AwayFromZero);

    /// <summary>Saldo de la nota: su total menos lo aplicado a facturas (nunca negativo).</summary>
    public static decimal Balance(decimal total, decimal applied) => Math.Max(0m, Math.Round(total - applied, 4, MidpointRounding.AwayFromZero));
}

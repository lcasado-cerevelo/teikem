using Teikem.Domain.Common;

namespace Teikem.Domain.Billing;

/// <summary>
/// Lote 224 (P6 del pedido de vendedores) — pago de una factura en consignación: el cliente paga (parcial o total) lo que vendió de lo que se le dejó. Cada pago mueve lo pagado
/// a «vendido» en la fecha del pago (los reportes de ventas cuentan lo consignado solo así). Si la factura todavía tiene saldo por cobrar, el pago queda también como pago de la
/// factura (PaymentId); si ya estaba pagada por Pagos, solo se informa lo vendido (PaymentId NULL). No se borra: se anula con motivo (VoidedAtUtc) y deja de contar.
/// </summary>
[AuditEntity(BillingCodes.InvoiceEntityType)]
public class ConsignmentPayment : ITenantScoped
{
    public int ConsignmentPaymentId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int InvoiceId { get; set; }
    /// <summary>El pago de la factura que generó (NULL = la factura ya estaba pagada: solo se informa lo vendido).</summary>
    public int? PaymentId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Fecha del pago (día de la compañía): la fecha en que lo consignado cuenta como vendido.</summary>
    public DateOnly PaidDate { get; set; }
    /// <summary>Método (catálogo PaymentMethod: ACH, CHECK, CARD, CASH).</summary>
    public int PaymentMethodLookupId { get; set; }
    public string? Reference { get; set; }
    public int? RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VoidedAtUtc { get; set; }
    public int? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>Lote 224 — reglas puras de la consignación (cifras de una factura, saldo, mensajes exactos).</summary>
public static class ConsignmentRules
{
    /// <summary>Filtro del listado: OPEN = con saldo en consignación; PAID = pagadas por completo; ALL = todas.</summary>
    public const string FilterOpen = "OPEN";
    public const string FilterPaid = "PAID";
    public const string FilterAll = "ALL";
    public static readonly string[] Filters = { FilterOpen, FilterPaid, FilterAll };

    public const string NotConsignment = "La factura no tiene nada en consignación.";
    public const string NotPayable = "Solo se registran pagos de consignación de una factura emitida, vencida o pagada.";
    public const string AlreadySettled = "Lo consignado en esta factura ya está pagado por completo.";
    public const string FilterInvalid = "El filtro debe ser OPEN, PAID o ALL.";

    public static string ExceedsBalance(decimal amount, decimal balance)
        => $"El pago ({amount:0.00}) es mayor que el saldo en consignación de la factura ({balance:0.00}).";

    /// <summary>Monto de una línea de factura (o de nota de crédito) con su impuesto: subtotal a 4 decimales (como la factura) + impuesto.</summary>
    public static decimal LineTotal(decimal quantity, decimal unitAmount, decimal taxAmount)
        => Math.Round(quantity * unitAmount, 4, MidpointRounding.AwayFromZero) + taxAmount;

    /// <summary>Lo que se dejó en consignación (las líneas de órdenes en consignación con su impuesto), al centavo.</summary>
    public static decimal Consigned(IEnumerable<decimal> lineTotals) => Math.Round(lineTotals.Sum(), 2, MidpointRounding.AwayFromZero);

    /// <summary>Saldo en consignación: lo dejado menos lo devuelto (notas de crédito aprobadas de esas líneas) y lo pagado; nunca negativo.</summary>
    public static decimal Balance(decimal consigned, decimal returned, decimal paid)
        => Math.Max(0m, Math.Round(consigned - returned - paid, 2, MidpointRounding.AwayFromZero));
}

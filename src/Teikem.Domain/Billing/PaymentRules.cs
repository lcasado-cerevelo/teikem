namespace Teikem.Domain.Billing;

/// <summary>
/// Lote 75 (P5) — reglas puras de los pagos y del estado de cuenta (sin EF ni servicios; PaymentService las traduce a 400/404/422). Los mensajes son públicos porque el manual
/// (capítulo 19) y la FAQ los citan tal cual.
/// </summary>
public static class PaymentRules
{
    public const int MaxReferenceLength = 80;
    public const int MaxVoidReasonLength = 250;

    public const string AmountRequired = "Indique el monto del pago.";
    public const string AmountInvalid = "El monto del pago debe ser mayor que cero y tener hasta 2 decimales.";
    public const string MethodRequired = "Indique el método de pago.";
    public const string MethodInvalid = "El método de pago no existe.";
    public const string DateFuture = "La fecha del pago no puede ser futura.";
    public const string DateBeforeIssue = "La fecha del pago no puede ser anterior a la emisión de la factura.";
    public const string ReferenceTooLong = "La referencia no puede pasar de 80 caracteres.";
    public const string NotPayable = "Solo se registran pagos de una factura emitida o vencida.";
    public const string VoidReasonRequired = "Escriba el motivo para anular el pago.";
    public const string VoidReasonTooLong = "El motivo no puede pasar de 250 caracteres.";
    public const string PaymentNotFound = "Pago no encontrado.";
    public const string AlreadyVoided = "El pago ya está anulado.";
    public const string VoidOnPaidInvoice = "La factura ya está pagada por completo: un pago registrado por error se corrige con una nota de crédito o con soporte.";
    public const string StatementPeriodInvalid = "La fecha final no puede ser anterior a la inicial.";

    public static string ExceedsBalance(decimal amount, decimal balance)
        => $"El pago ({amount:0.00}) es mayor que el saldo de la factura ({balance:0.00}).";

    /// <summary>Un monto de dinero válido: mayor que cero y con hasta 2 decimales.</summary>
    public static bool IsValidAmount(decimal amount) => amount > 0m && Math.Round(amount, 2, MidpointRounding.AwayFromZero) == amount;

    /// <summary>Saldo de una factura: su total menos lo pagado (nunca negativo).</summary>
    public static decimal Balance(decimal total, decimal paid) => Math.Max(0m, Math.Round(total - paid, 4, MidpointRounding.AwayFromZero));

    /// <summary>Una factura queda saldada cuando lo pagado cubre su total.</summary>
    public static bool IsSettled(decimal total, decimal paid) => Balance(total, paid) <= 0m;

    /// <summary>Se vence cuando ya pasó su fecha de vencimiento y todavía tiene saldo.</summary>
    public static bool IsOverdue(DateOnly? dueDate, decimal balance, DateOnly today) => balance > 0m && dueDate is DateOnly d && d < today;

    /// <summary>
    /// Cubeta de antigüedad de un saldo: 0 = por vencer (aún no vence), 1 = vencido de 1 a 30 días, 2 = de 31 a 60, 3 = de 61 a 90, 4 = más de 90. Sin fecha de vencimiento cuenta como por vencer.
    /// </summary>
    public static int AgingBucket(DateOnly? dueDate, DateOnly asOf)
    {
        if (dueDate is not DateOnly d) return 0;
        var days = asOf.DayNumber - d.DayNumber;
        return days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4;
    }
}

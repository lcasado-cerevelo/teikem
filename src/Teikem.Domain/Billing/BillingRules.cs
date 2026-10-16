namespace Teikem.Domain.Billing;

/// <summary>
/// Lote 72 (P5) — reglas puras de la facturación (sin EF ni servicios; BillingService las traduce a 400/404/409/422). Los mensajes son públicos porque el manual (capítulo 19) y la
/// FAQ los citan tal cual.
/// </summary>
public static class BillingRules
{
    public const int MaxNoteLength = 500;
    public const int MaxDueDays = 365;
    public const int DefaultDueDays = 30;

    public const string PeriodRequired = "Indique el período de la corrida (fecha inicial y final).";
    public const string PeriodInvalid = "La fecha final no puede ser anterior a la inicial.";
    public const string NothingToBill = "No hay nada por facturar con esos datos.";
    public const string NoteTooLong = "La nota no puede pasar de 500 caracteres.";
    public const string ClientNotFound = "Cliente no encontrado.";
    public const string InvalidTrigger = "La base de facturación debe ser ON_DISPATCH (al despachar) u ON_DELIVERY (al entregar).";
    public const string DueDaysInvalid = "Los días de vencimiento deben estar entre 0 y 365.";

    public const string ReviewNeedsGenerated = "Solo se puede revisar una corrida generada.";
    public const string ApproveNeedsReviewed = "Revise la corrida antes de aprobarla.";
    public const string CancelNeedsOpen = "Solo se cancela una corrida generada o revisada; una aprobada se corrige anulando sus facturas.";
    public const string AdjustNeedsOpenRun = "Solo se ajusta una factura de una corrida generada o revisada.";
    public const string AdjustNeedsPrice = "Indique el nuevo monto unitario.";
    public const string AdjustPriceInvalid = "El monto unitario debe ser un número de 0 en adelante con hasta 4 decimales.";
    public const string AdjustNoteRequired = "Escriba el motivo del ajuste.";
    public const string AdjustNoteTooLong = "El motivo del ajuste no puede pasar de 250 caracteres.";
    public const string AdjustSameAmount = "El monto unitario es el mismo; no hay nada que ajustar.";
    public const string VoidNeedsIssued = "Solo se anula una factura emitida o vencida.";
    public const string VoidHasPayments = "La factura tiene pagos registrados: no se puede anular.";


    /// <summary>Lo que se puede facturar de una línea de orden de venta: despachado − devuelto − ya facturado (nunca negativo).</summary>
    public static decimal Billable(decimal shipped, decimal returned, decimal invoiced) => Math.Max(0m, shipped - returned - invoiced);

    /// <summary>Redondeo del dinero: 2 decimales, mitad lejos de cero (el mismo del impuesto).</summary>
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Impuesto de la parte que se factura de una línea: la copia del impuesto de la línea de la orden, en proporción a lo que se factura. Cuando lo que se factura es TODO lo que
    /// falta, se toma el resto del impuesto (el total de la línea menos lo ya facturado), así las partes suman exactamente el impuesto de la línea.
    /// </summary>
    public static decimal TaxPortion(decimal lineTax, decimal ordered, decimal qty, decimal remainingBeforeThis, decimal taxAlreadyInvoiced)
    {
        if (lineTax <= 0m || ordered <= 0m || qty <= 0m) return 0m;
        if (qty >= remainingBeforeThis) return Math.Max(0m, Money(lineTax - taxAlreadyInvoiced));
        return Money(lineTax * qty / ordered);
    }

    /// <summary>Fecha de vencimiento: la de emisión más los días de la compañía.</summary>
    public static DateOnly DueDate(DateOnly issued, int days) => issued.AddDays(Math.Clamp(days, 0, MaxDueDays));

    public static string Describe(string orderNumber, string sku, string name) => $"{orderNumber} · {sku} {name}".Trim();
}

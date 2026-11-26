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
    public const string InvalidTrigger = "La base de facturación debe ser ON_DISPATCH (al despachar), ON_DELIVERY (al entregar) u ON_REQUEST (cuando el usuario lo decida, ya solicitada la orden).";
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

    /// <summary>
    /// Lote 249: lo facturable de una línea con la base ON_REQUEST (se factura cuando el usuario lo decida, sin esperar el despacho): lo pedido − devuelto − ya facturado.
    /// En una línea cerrada con faltante (backorder cerrado) lo pedido que cuenta es lo despachado más lo reservado: el faltante cerrado ya no se va a entregar. Nunca negativo.
    /// </summary>
    public static decimal BillableOnRequest(decimal ordered, decimal reserved, decimal shipped, decimal returned, decimal invoiced, bool isClosed)
        => Math.Max(0m, (isClosed ? shipped + reserved : ordered) - returned - invoiced);

    /// <summary>Largo de InvoiceLine.Description (NVARCHAR(250)).</summary>
    public const int MaxDescriptionLength = 250;

    /// <summary>Largo máximo del MEMO de la factura en el IIF de QuickBooks (el mismo tope que el memo de las órdenes de compra).</summary>
    public const int MaxInvoiceMemoLength = 4000;

    /// <summary>
    /// Descripción de la línea de venta de producto: «OV-00040 · SKU Nombre». Lote 249: si la orden es una ADICIÓN (tiene madre), «OV-00045 (adición de OV-00040 · complementa FAC-00012,
    /// FAC-00015) · SKU Nombre» (sin facturas de la madre: «OV-00045 (adición de OV-00040) · SKU Nombre»). Se recorta a 250 caracteres: primero sobran los números de factura (los que no caben
    /// se cambian por «…»), después el nombre (y, si aún no cabe, el SKU). El número de la orden nunca se corta.
    /// </summary>
    public static string Describe(string orderNumber, string sku, string name, string? parentNumber = null, IReadOnlyList<string>? parentInvoiceNumbers = null)
    {
        sku = (sku ?? string.Empty).Trim();
        name = (name ?? string.Empty).Trim();
        var invoices = (parentInvoiceNumbers ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        string Head(int keep) => string.IsNullOrWhiteSpace(parentNumber) ? orderNumber : $"{orderNumber} {AdditionRef(parentNumber!, invoices, keep, "complementa ")}";
        string Join(string head, string s, string n) => $"{head} · {s} {n}".Trim();

        // 1) todas las facturas; 2) se van quitando de la última a la primera (con «…»)
        for (var keep = invoices.Count; keep >= 0; keep--)
        {
            var text = Join(Head(keep), sku, name);
            if (text.Length <= MaxDescriptionLength) return text;
        }
        // 3) sin facturas, se recorta el nombre; 4) y, si hace falta, el SKU
        var head = Head(0);
        var room = MaxDescriptionLength - Join(head, sku, string.Empty).Length - 1;   // −1: el espacio antes del nombre
        if (room > 1) return Join(head, sku, Cut(name, room));
        var skuRoom = MaxDescriptionLength - head.Length - 3;   // « · »
        if (skuRoom > 1) return $"{head} · {Cut(sku, skuRoom)}";
        return head.Length <= MaxDescriptionLength ? head : head[..MaxDescriptionLength];
    }

    /// <summary>
    /// Lote 249: la referencia de una orden de adición en el MEMO de la factura del IIF: «OV-00045 (adición de OV-00040 · FAC-00012)» (sin facturas de la madre: «OV-00045 (adición de
    /// OV-00040)»). Una orden normal va solo con su número.
    /// </summary>
    public static string MemoReference(string orderNumber, string? parentNumber, IReadOnlyList<string>? parentInvoiceNumbers)
        => string.IsNullOrWhiteSpace(parentNumber)
            ? orderNumber
            : $"{orderNumber} {AdditionRef(parentNumber!, (parentInvoiceNumbers ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(), int.MaxValue, string.Empty)}";

    /// <summary>El MEMO de la factura: las referencias de sus órdenes separadas por coma, recortado a <see cref="MaxInvoiceMemoLength"/> (las que no caben se cambian por «…»).</summary>
    public static string InvoiceMemo(IEnumerable<string> references)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in references)
        {
            var piece = sb.Length == 0 ? r : ", " + r;
            if (sb.Length + piece.Length > MaxInvoiceMemoLength)
            {
                // se quitan referencias del final hasta que quepa «, …»
                while (sb.Length > 0 && sb.Length + 3 > MaxInvoiceMemoLength)
                {
                    var cut = sb.ToString().LastIndexOf(", ", StringComparison.Ordinal);
                    sb.Length = Math.Max(0, cut);
                }
                sb.Append(sb.Length == 0 ? "…" : ", …");
                break;
            }
            sb.Append(piece);
        }
        return sb.ToString();
    }

    /// <summary>«(adición de OV-00040 · complementa FAC-00012, FAC-00015)» con las primeras `keep` facturas (si sobran, «…»; si no queda ninguna, sin la parte de las facturas).</summary>
    private static string AdditionRef(string parentNumber, IReadOnlyList<string> invoices, int keep, string verb)
    {
        if (invoices.Count == 0) return $"(adición de {parentNumber.Trim()})";
        var shown = invoices.Take(Math.Max(0, keep)).ToList();
        if (shown.Count == 0) return $"(adición de {parentNumber.Trim()})";
        var list = string.Join(", ", shown) + (shown.Count < invoices.Count ? ", …" : string.Empty);
        return $"(adición de {parentNumber.Trim()} · {verb}{list})";
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)].TrimEnd() + "…";
}

namespace Teikem.Domain.Sales;

/// <summary>Lote 222 — formas de pago de una orden de venta. Catálogo LookupCode «SalesPaymentMethod».</summary>
public static class SalesPaymentMethods
{
    public const string Domain = "SalesPaymentMethod";
    public const string CodCheck = "COD_CHECK";
    public const string CodCash = "COD_CASH";
    public const string CodAch = "COD_ACH";
    public const string Credit = "CREDIT";
    public const string Consignment = "CONSIGNMENT";
    public static readonly string[] All = { CodCheck, CodCash, CodAch, Credit, Consignment };
    public static bool IsCod(string? code) => code is CodCheck or CodCash or CodAch;
}

/// <summary>
/// Lote 222 — reglas puras de la forma de pago de la orden (plan §2: forma de pago prellenada con la del cliente y marca «cambió»; consignación solo con esa forma).
/// La «forma de pago» del cliente es su término de pago (catálogo PaymentTerm, ya migrado de QuickBooks): CHEQUE / CASH / ACH = contra entrega con cheque, efectivo o ACH;
/// NET15…NET60 y PK_BY_REP («cobra el representante») = crédito con ese término; CONSIGNMENT = consignación; COD = contra entrega sin decir cómo (el vendedor escoge).
/// La orden guarda la forma (SalesPaymentMethod) y, solo si es CRÉDITO, el término.
/// </summary>
public static class SalesPaymentRules
{
    public const string TermCod = "COD";
    public const string TermCheque = "CHEQUE";
    public const string TermCash = "CASH";
    public const string TermAch = "ACH";
    public const string TermConsignment = "CONSIGNMENT";
    public const string TermPaidByRep = "PK_BY_REP";

    public const string MethodUnknown = "La forma de pago debe ser COD_CHECK, COD_CASH, COD_ACH, CREDIT o CONSIGNMENT.";
    public const string CreditNeedsTerm = "Indique el término del crédito (15, 20, 30 o 45 días, o cobra el representante).";
    public const string TermOnlyWithCredit = "El término de pago solo se indica con la forma de pago Crédito.";
    public const string TermNotCredit = "El término de pago no es de crédito.";
    public const string ConsignmentNeedsMethod = "La venta en consignación exige la forma de pago Consignación.";

    /// <summary>¿El término es de crédito? (NET15, NET20, NET30, NET45, NET60… y «cobra el representante»).</summary>
    public static bool IsCreditTerm(string? term)
    {
        var t = (term ?? string.Empty).Trim().ToUpperInvariant();
        return t == TermPaidByRep || (t.StartsWith("NET", StringComparison.Ordinal) && t.Length > 3 && t[3..].All(char.IsDigit));
    }

    /// <summary>La forma de pago (y el término, si es crédito) que se prellena con el término de pago del cliente. (null, null) = el cliente no dice nada que sirva.</summary>
    public static (string? Method, string? Term) Prefill(string? clientTerm)
    {
        var t = (clientTerm ?? string.Empty).Trim().ToUpperInvariant();
        return t switch
        {
            TermCheque => (SalesPaymentMethods.CodCheck, null),
            TermCash => (SalesPaymentMethods.CodCash, null),
            TermAch => (SalesPaymentMethods.CodAch, null),
            TermConsignment => (SalesPaymentMethods.Consignment, null),
            _ when IsCreditTerm(t) => (SalesPaymentMethods.Credit, t),
            _ => (null, null),
        };
    }

    /// <summary>
    /// ¿La forma de pago de la orden cambió respecto a la del cliente? Nada escogido = no cambió; la prellenada = no cambió; un cliente «contra entrega» (COD) que paga
    /// contra entrega con cheque, efectivo o ACH = no cambió. Lo demás sí (la oficina lo ve marcado y decide si lo pasa al cliente).
    /// </summary>
    public static bool IsChange(string? clientTerm, string? method, string? term)
    {
        if (method is null && term is null) return false;
        var (pm, pt) = Prefill(clientTerm);
        if (string.Equals(method, pm, StringComparison.OrdinalIgnoreCase) && string.Equals(term, pt, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals((clientTerm ?? string.Empty).Trim(), TermCod, StringComparison.OrdinalIgnoreCase) && SalesPaymentMethods.IsCod(method) && term is null) return false;
        return true;
    }

    /// <summary>Forma y término juntos: devuelve el mensaje de error (o null). El término solo va con crédito, y el crédito lo exige.</summary>
    public static string? ValidateMethodAndTerm(string? method, string? term)
    {
        if (method is not null && !SalesPaymentMethods.All.Contains(method)) return MethodUnknown;
        if (method == SalesPaymentMethods.Credit && term is null) return CreditNeedsTerm;
        if (term is not null && method != SalesPaymentMethods.Credit) return TermOnlyWithCredit;
        if (term is not null && !IsCreditTerm(term)) return TermNotCredit;
        return null;
    }

    /// <summary>La consignación solo se marca con la forma de pago Consignación (devuelve el mensaje de error o null).</summary>
    public static string? ValidateConsignment(bool isConsignment, string? method)
        => isConsignment && method != SalesPaymentMethods.Consignment ? ConsignmentNeedsMethod : null;
}

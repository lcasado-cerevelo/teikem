namespace Teikem.Domain.Sales;

/// <summary>Las cantidades (y el importe) de una línea para revisar sus invariantes. En un kit, en kits.</summary>
public sealed record InvariantLine(
    int LineId,
    int LineNo,
    decimal QtyOrdered,
    decimal QtyReserved,
    decimal QtyBackordered,
    decimal QtyShipped,
    decimal QtyInvoiced,
    decimal QtyReturned,
    bool IsClosed,
    decimal UnitPrice = 0m,
    decimal TaxAmount = 0m)
{
    public static InvariantLine From(SalesOrderLine l)
        => new(l.SalesOrderLineId, l.LineNo, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped, l.QtyInvoiced, l.QtyReturned, l.IsClosed, l.UnitPrice, l.TaxAmount);
}

/// <summary>La cabecera (subtotal e IVU de producto) y sus líneas.</summary>
public sealed record InvariantOrder(decimal Subtotal, decimal TaxAmount, IReadOnlyList<InvariantLine> Lines)
{
    public static InvariantOrder From(SalesOrder o) => new(o.Subtotal, o.TaxAmount, o.Lines.Select(InvariantLine.From).ToList());
}

/// <summary>Una invariante rota. LineId/LineNo = 0 para las de la cabecera.</summary>
public sealed record SalesLineViolation(int LineId, int LineNo, string Rule, string Detail);

/// <summary>Las invariantes que revisa el verificador.</summary>
public static class SalesLineInvariantRules
{
    /// <summary>Ninguna cantidad negativa.</summary>
    public const string NonNegative = "NON_NEGATIVE";
    /// <summary>Abierta: reservado + backorder + despachado = pedido. Cerrada: sin backorder y reservado + despachado ≤ pedido (la quitada: todo en 0).</summary>
    public const string Balance = "BALANCE";
    /// <summary>Pedido 0 solo si la línea está cerrada (CK_SalesOrderLine_Qty).</summary>
    public const string ZeroOpen = "ZERO_OPEN";
    /// <summary>Despachado ≥ facturado + devuelto (solo cuando se factura lo despachado: ON_DISPATCH / ON_DELIVERY).</summary>
    public const string ShippedCoversInvoiced = "SHIPPED_COVERS_INVOICED";
    /// <summary>Subtotal de la cabecera = suma de cantidad × precio de las líneas.</summary>
    public const string HeaderSubtotal = "HEADER_SUBTOTAL";
    /// <summary>IVU de la cabecera = suma del IVU de las líneas.</summary>
    public const string HeaderTax = "HEADER_TAX";
}

/// <summary>
/// Lote 249 — verificador de invariantes de las líneas de una orden de venta. Corre DESPUÉS de cada modificación (en producción) y en las pruebas: una línea que
/// estaba coherente ANTES tiene que seguir coherente DESPUÉS, y una línea nueva tiene que nacer coherente. No falla por incoherencias heredadas que el cambio no
/// tocó (si la misma regla ya estaba rota antes en esa línea, no cuenta). Con una violación nueva, el motor responde 422 (IncoherentMessage) y revierte todo.
/// </summary>
public static class SalesLineInvariants
{
    /// <summary>Tolerancia de la cabecera (la cabecera guarda a 4 decimales; los totales se comparan a centavos).</summary>
    public const decimal HeaderTolerance = 0.01m;

    /// <summary>422 del motor cuando la modificación introduce una incoherencia.</summary>
    public static string IncoherentMessage(int lineNo) => $"La modificación dejaría la orden incoherente (línea {lineNo}): no se aplicó.";

    /// <summary>
    /// Las invariantes rotas de UNA línea. shippedCoversInvoiced = false con la facturación ON_REQUEST (se factura lo pedido sin esperar el despacho: lo facturado
    /// puede ser mayor que lo despachado).
    /// </summary>
    public static IReadOnlyList<SalesLineViolation> Violations(InvariantLine l, bool shippedCoversInvoiced = true)
    {
        var v = new List<SalesLineViolation>();
        if (l.QtyOrdered < 0m || l.QtyReserved < 0m || l.QtyBackordered < 0m || l.QtyShipped < 0m || l.QtyInvoiced < 0m || l.QtyReturned < 0m)
            v.Add(new(l.LineId, l.LineNo, SalesLineInvariantRules.NonNegative,
                $"pedido {T(l.QtyOrdered)}, reservado {T(l.QtyReserved)}, backorder {T(l.QtyBackordered)}, despachado {T(l.QtyShipped)}, facturado {T(l.QtyInvoiced)}, devuelto {T(l.QtyReturned)}"));
        if (l.QtyOrdered == 0m && !l.IsClosed)
            v.Add(new(l.LineId, l.LineNo, SalesLineInvariantRules.ZeroOpen, "pedido 0 sin cerrar"));
        var sum = l.QtyReserved + l.QtyBackordered + l.QtyShipped;
        var balanced = l.IsClosed ? l.QtyBackordered == 0m && sum <= l.QtyOrdered : sum == l.QtyOrdered;
        if (!balanced)
            v.Add(new(l.LineId, l.LineNo, SalesLineInvariantRules.Balance,
                $"reservado {T(l.QtyReserved)} + backorder {T(l.QtyBackordered)} + despachado {T(l.QtyShipped)} {(l.IsClosed ? "frente a" : "≠")} pedido {T(l.QtyOrdered)}{(l.IsClosed ? " (cerrada)" : string.Empty)}"));
        if (shippedCoversInvoiced && l.QtyShipped < l.QtyInvoiced + l.QtyReturned)
            v.Add(new(l.LineId, l.LineNo, SalesLineInvariantRules.ShippedCoversInvoiced,
                $"despachado {T(l.QtyShipped)} < facturado {T(l.QtyInvoiced)} + devuelto {T(l.QtyReturned)}"));
        return v;
    }

    /// <summary>Las invariantes rotas de la cabecera: subtotal = Σ cantidad × precio y IVU = Σ IVU de las líneas (a centavos).</summary>
    public static IReadOnlyList<SalesLineViolation> HeaderViolations(InvariantOrder o)
    {
        var v = new List<SalesLineViolation>();
        var subtotal = o.Lines.Sum(l => SalesRules.LineAmount(l.QtyOrdered, l.UnitPrice));
        if (Math.Abs(o.Subtotal - subtotal) > HeaderTolerance)
            v.Add(new(0, 0, SalesLineInvariantRules.HeaderSubtotal, $"subtotal {T(o.Subtotal)} ≠ suma de las líneas {T(subtotal)}"));
        var tax = o.Lines.Sum(l => l.TaxAmount);
        if (Math.Abs(o.TaxAmount - tax) > HeaderTolerance)
            v.Add(new(0, 0, SalesLineInvariantRules.HeaderTax, $"IVU {T(o.TaxAmount)} ≠ suma de las líneas {T(tax)}"));
        return v;
    }

    /// <summary>
    /// Las violaciones que el cambio INTRODUJO: por línea (emparejadas por id; las nuevas, contra nada) y de la cabecera. Una regla que ya estaba rota antes en la
    /// misma línea (o en la cabecera) no cuenta.
    /// </summary>
    public static IReadOnlyList<SalesLineViolation> Check(IReadOnlyList<InvariantLine> before, IReadOnlyList<InvariantLine> after, bool shippedCoversInvoiced = true)
    {
        var prev = before.Where(l => l.LineId > 0).GroupBy(l => l.LineId).ToDictionary(g => g.Key, g => g.First());
        var result = new List<SalesLineViolation>();
        foreach (var a in after)
        {
            var now = Violations(a, shippedCoversInvoiced);
            if (now.Count == 0) continue;
            var inherited = a.LineId > 0 && prev.TryGetValue(a.LineId, out var b)
                ? Violations(b, shippedCoversInvoiced).Select(x => x.Rule).ToHashSet()
                : new HashSet<string>();
            result.AddRange(now.Where(x => !inherited.Contains(x.Rule)));
        }
        return result;
    }

    /// <summary>Check de las líneas más las de la cabecera.</summary>
    public static IReadOnlyList<SalesLineViolation> Check(InvariantOrder before, InvariantOrder after, bool shippedCoversInvoiced = true)
    {
        var result = Check(before.Lines, after.Lines, shippedCoversInvoiced).ToList();
        var inherited = HeaderViolations(before).Select(x => x.Rule).ToHashSet();
        result.AddRange(HeaderViolations(after).Where(x => !inherited.Contains(x.Rule)));
        return result;
    }

    private static string T(decimal d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Teikem.Domain.Billing;

namespace Teikem.Domain.Sales;

/// <summary>Lote 249 — JSON estable (camelCase, sin sangría, con los nulos) de las fotos, la solicitud y los efectos de una modificación.</summary>
public static class SalesChangeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string? json) => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Una línea en la foto de la modificación (cantidades en kits para las líneas de kit).</summary>
public sealed record SalesChangeSnapshotLine(
    int LineId,
    int LineNo,
    int ProductId,
    string Sku,
    string Name,
    decimal QtyOrdered,
    decimal QtyReserved,
    decimal QtyBackordered,
    decimal QtyShipped,
    decimal QtyInvoiced,
    decimal QtyReturned,
    decimal UnitPrice,
    decimal? ResolvedUnitPrice,
    string? PriceSource,
    decimal TaxRatePct,
    decimal TaxAmount,
    string? TaxReason,
    decimal? TaxComputedRatePct,
    decimal? TaxComputedAmount,
    bool IsClosed,
    bool IsRemoved);

/// <summary>Los términos en la foto: forma de pago y término (códigos), entrega efectiva, consignatario, consignación y notas.</summary>
public sealed record SalesChangeSnapshotTerms(
    string? PaymentMethod,
    string? PaymentTerm,
    string? DeliveryMode,
    int? ConsigneeId,
    string? ConsigneeName,
    bool IsConsignment,
    string? Notes);

/// <summary>Un campo que cambió, con su valor de antes y de después (texto invariante: decimales sin ceros de sobra, «true»/«false»).</summary>
public sealed record SalesChangeFieldDiff(string Field, string? Before, string? After);

/// <summary>Qué le pasó a una línea: ADDED (nueva o reactivada), REMOVED (quitada) o CHANGED (con sus campos).</summary>
public sealed record SalesChangeLineDiff(int LineId, int LineNo, int ProductId, string Sku, string Name, string Change, IReadOnlyList<SalesChangeFieldDiff> Fields);

/// <summary>La diferencia entre la foto de antes y la de después.</summary>
public sealed record SalesChangeDiff(
    IReadOnlyList<SalesChangeLineDiff> Added,
    IReadOnlyList<SalesChangeLineDiff> Removed,
    IReadOnlyList<SalesChangeLineDiff> Changed,
    IReadOnlyList<SalesChangeFieldDiff> Terms,
    decimal TotalBefore,
    decimal TotalAfter)
{
    public decimal Delta => TotalAfter - TotalBefore;
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0 && Terms.Count == 0 && TotalBefore == TotalAfter;
}

/// <summary>Valores de SalesChangeLineDiff.Change.</summary>
public static class SalesChangeDiffKinds
{
    public const string Added = "ADDED";
    public const string Removed = "REMOVED";
    public const string Changed = "CHANGED";
}

/// <summary>
/// Lote 249 — la FOTO (v1) de una orden de venta antes o después de una modificación: líneas (SKU, nombre, cantidades, precio, precio resuelto, origen, % e
/// importe de IVU, razón, IVU calculado y si está quitada), términos y totales (subtotal, IVU, envío y su IVU, total). Se guarda como JSON estable en camelCase
/// (SalesOrderChange.BeforeJson / AfterJson). Aquí viven también la diferencia, el resumen (≤500) y la huella SHA-256 que se compara al aprobar.
/// </summary>
public sealed record SalesChangeSnapshot(
    int V,
    IReadOnlyList<SalesChangeSnapshotLine> Lines,
    SalesChangeSnapshotTerms Terms,
    decimal Subtotal,
    decimal TaxAmount,
    decimal ShippingCharge,
    decimal ShippingTaxAmount,
    decimal Total)
{
    /// <summary>Arma la foto con el total calculado: Money(subtotal + IVU + envío + IVU del envío). Las líneas quedan en orden de número de línea.</summary>
    public static SalesChangeSnapshot Create(IEnumerable<SalesChangeSnapshotLine> lines, SalesChangeSnapshotTerms terms, decimal subtotal, decimal taxAmount,
        decimal shippingCharge, decimal shippingTaxAmount)
        => new(SalesChangeCodes.SnapshotVersion, lines.OrderBy(l => l.LineNo).ThenBy(l => l.LineId).ToList(), terms, subtotal, taxAmount, shippingCharge, shippingTaxAmount,
            OrderTotal(subtotal, taxAmount, shippingCharge, shippingTaxAmount));

    /// <summary>La foto de una orden tal como está (con sus líneas cargadas). products = SKU y nombre de cada producto de las líneas.</summary>
    public static SalesChangeSnapshot FromOrder(SalesOrder order, IReadOnlyDictionary<int, (string Sku, string Name)> products, SalesChangeSnapshotTerms terms)
        => Create(order.Lines.Select(l =>
        {
            var (sku, name) = products.TryGetValue(l.ProductId, out var p) ? p : (string.Empty, string.Empty);
            return new SalesChangeSnapshotLine(l.SalesOrderLineId, l.LineNo, l.ProductId, sku, name, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped,
                l.QtyInvoiced, l.QtyReturned, l.UnitPrice, l.ResolvedUnitPrice, l.PriceSource, l.TaxRatePct, l.TaxAmount, l.TaxReason, l.TaxComputedRatePct,
                l.TaxComputedAmount, l.IsClosed, l.QtyOrdered == 0m && l.IsClosed);
        }), terms, order.Subtotal, order.TaxAmount, order.ShippingCharge, order.ShippingTaxAmount);

    /// <summary>Total de la orden con IVU y envío, redondeado a centavos (la misma suma que compara la segunda aprobación).</summary>
    public static decimal OrderTotal(decimal subtotal, decimal taxAmount, decimal shippingCharge, decimal shippingTaxAmount)
        => BillingRules.Money(subtotal + taxAmount + shippingCharge + shippingTaxAmount);

    public string ToJson() => SalesChangeJson.Serialize(this);

    public static SalesChangeSnapshot? FromJson(string? json) => SalesChangeJson.Deserialize<SalesChangeSnapshot>(json);

    // ------------------------------------------------------------------------------------------------ diferencia

    /// <summary>
    /// Agregadas (nuevas o reactivadas), quitadas y cambiadas por campo (cantidad, precio, % e importe de IVU, razón), y los términos que cambiaron. Las líneas se
    /// emparejan por id; una línea nueva de la foto proyectada (id 0) se empareja por producto con una línea de antes.
    /// </summary>
    public static SalesChangeDiff Diff(SalesChangeSnapshot before, SalesChangeSnapshot after)
    {
        var added = new List<SalesChangeLineDiff>();
        var removed = new List<SalesChangeLineDiff>();
        var changed = new List<SalesChangeLineDiff>();
        var unmatched = before.Lines.ToList();
        var pairs = new List<(SalesChangeSnapshotLine? B, SalesChangeSnapshotLine A)>();

        foreach (var a in after.Lines.Where(l => l.LineId > 0))
        {
            var b = unmatched.FirstOrDefault(x => x.LineId == a.LineId);
            if (b is not null) unmatched.Remove(b);
            pairs.Add((b, a));
        }
        foreach (var a in after.Lines.Where(l => l.LineId <= 0))
        {
            var b = unmatched.FirstOrDefault(x => x.ProductId == a.ProductId);
            if (b is not null) unmatched.Remove(b);
            pairs.Add((b, a));
        }

        foreach (var (b, a) in pairs)
        {
            var wasActive = b is not null && !b.IsRemoved;
            if (!wasActive && a.IsRemoved) continue;   // seguía quitada
            if (!wasActive)
            {
                added.Add(LineDiff(a, SalesChangeDiffKinds.Added, new[]
                {
                    new SalesChangeFieldDiff("qtyOrdered", null, Txt(a.QtyOrdered)),
                    new SalesChangeFieldDiff("unitPrice", null, Txt(a.UnitPrice)),
                }));
                continue;
            }
            if (a.IsRemoved)
            {
                removed.Add(LineDiff(b!, SalesChangeDiffKinds.Removed, new[] { new SalesChangeFieldDiff("qtyOrdered", Txt(b!.QtyOrdered), Txt(0m)) }));
                continue;
            }
            var fields = new List<SalesChangeFieldDiff>();
            Compare(fields, "qtyOrdered", b!.QtyOrdered, a.QtyOrdered);
            Compare(fields, "unitPrice", b.UnitPrice, a.UnitPrice);
            Compare(fields, "taxRatePct", b.TaxRatePct, a.TaxRatePct);
            Compare(fields, "taxAmount", b.TaxAmount, a.TaxAmount);
            if (b.TaxReason != a.TaxReason) fields.Add(new SalesChangeFieldDiff("taxReason", b.TaxReason, a.TaxReason));
            if (fields.Count > 0) changed.Add(LineDiff(a, SalesChangeDiffKinds.Changed, fields));
        }
        foreach (var b in unmatched.Where(x => !x.IsRemoved))
            removed.Add(LineDiff(b, SalesChangeDiffKinds.Removed, new[] { new SalesChangeFieldDiff("qtyOrdered", Txt(b.QtyOrdered), Txt(0m)) }));

        var terms = new List<SalesChangeFieldDiff>();
        var tb = before.Terms;
        var ta = after.Terms;
        if (tb.PaymentMethod != ta.PaymentMethod) terms.Add(new("paymentMethod", tb.PaymentMethod, ta.PaymentMethod));
        if (tb.PaymentTerm != ta.PaymentTerm) terms.Add(new("paymentTerm", tb.PaymentTerm, ta.PaymentTerm));
        if (tb.DeliveryMode != ta.DeliveryMode) terms.Add(new("deliveryMode", tb.DeliveryMode, ta.DeliveryMode));
        if (tb.ConsigneeId != ta.ConsigneeId) terms.Add(new("consignee", tb.ConsigneeName, ta.ConsigneeName));
        if (tb.IsConsignment != ta.IsConsignment) terms.Add(new("isConsignment", Txt(tb.IsConsignment), Txt(ta.IsConsignment)));
        if ((tb.Notes ?? string.Empty) != (ta.Notes ?? string.Empty)) terms.Add(new("notes", tb.Notes, ta.Notes));

        return new SalesChangeDiff(
            added.OrderBy(x => x.LineNo).ToList(), removed.OrderBy(x => x.LineNo).ToList(), changed.OrderBy(x => x.LineNo).ToList(), terms, before.Total, after.Total);
    }

    private static SalesChangeLineDiff LineDiff(SalesChangeSnapshotLine l, string change, IReadOnlyList<SalesChangeFieldDiff> fields)
        => new(l.LineId, l.LineNo, l.ProductId, l.Sku, l.Name, change, fields);

    private static void Compare(List<SalesChangeFieldDiff> fields, string name, decimal before, decimal after)
    {
        if (before != after) fields.Add(new SalesChangeFieldDiff(name, Txt(before), Txt(after)));
    }

    /// <summary>Texto invariante de un decimal sin ceros de sobra (8.00 → «8», 7.50 → «7.5»).</summary>
    public static string Txt(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);
    private static string Txt(bool b) => b ? "true" : "false";

    // ------------------------------------------------------------------------------------------------ resumen

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>
    {
        ["qtyOrdered"] = "cantidad",
        ["unitPrice"] = "precio",
        ["taxRatePct"] = "% IVU",
        ["taxReason"] = "razón del IVU",
        ["paymentMethod"] = "forma de pago",
        ["paymentTerm"] = "término",
        ["deliveryMode"] = "entrega",
        ["consignee"] = "consignatario",
        ["isConsignment"] = "consignación",
        ["notes"] = "notas",
    };

    /// <summary>
    /// Resumen corto (≤500) para listas y avisos, p. ej. «Agregó: SKU-1 ×3. Quitó: SKU-2. Cambió: SKU-3 (cantidad 5 → 3; precio 8 → 7.5). Términos: entrega. Total
    /// 100.00 → 95.00.». Si no cabe, se recorta con «…».
    /// </summary>
    public static string Summary(SalesChangeDiff diff)
    {
        var parts = new List<string>();
        if (diff.Added.Count > 0)
            parts.Add("Agregó: " + string.Join(", ", diff.Added.Select(a => $"{a.Sku} ×{a.Fields.FirstOrDefault(f => f.Field == "qtyOrdered")?.After}")));
        if (diff.Removed.Count > 0)
            parts.Add("Quitó: " + string.Join(", ", diff.Removed.Select(r => r.Sku)));
        if (diff.Changed.Count > 0)
            parts.Add("Cambió: " + string.Join(", ", diff.Changed.Select(c =>
            {
                var fs = c.Fields.Where(f => FieldLabels.ContainsKey(f.Field)).Select(f => $"{FieldLabels[f.Field]} {f.Before ?? "—"} → {f.After ?? "—"}").ToList();
                return fs.Count == 0 ? $"{c.Sku} (IVU)" : $"{c.Sku} ({string.Join("; ", fs)})";
            })));
        if (diff.Terms.Count > 0)
            parts.Add("Términos: " + string.Join(", ", diff.Terms.Select(t => FieldLabels.TryGetValue(t.Field, out var lbl) ? lbl : t.Field)));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"Total {diff.TotalBefore:0.00} → {diff.TotalAfter:0.00}"));
        var text = string.Join(". ", parts) + ".";
        return text.Length <= SalesChangeRules.MaxSummary ? text : text[..(SalesChangeRules.MaxSummary - 1)] + "…";
    }

    // ------------------------------------------------------------------------------------------------ huella

    /// <summary>
    /// Huella SHA-256 (64 hex en minúsculas) del estado que una modificación pendiente da por bueno: estatus de la orden; líneas (id, pedida, despachada, facturada,
    /// devuelta, precio, %, razón, cerrada); términos; recolecciones activas (id, estatus, envío). NO incluye lo reservado ni el backorder (el almacén no se
    /// bloquea por reservar). No depende del orden en que vengan las líneas ni las recolecciones.
    /// </summary>
    public static string Fingerprint(string orderStatusCode, SalesChangeSnapshot snapshot, IEnumerable<(int BatchId, string StatusCode, int? ShipmentId)> activeBatches)
    {
        var sb = new StringBuilder();
        sb.Append("v1|").Append(orderStatusCode).Append('|');
        foreach (var l in snapshot.Lines.OrderBy(l => l.LineId).ThenBy(l => l.LineNo))
            sb.Append("L:").Append(l.LineId).Append(',').Append(Txt(l.QtyOrdered)).Append(',').Append(Txt(l.QtyShipped)).Append(',').Append(Txt(l.QtyInvoiced))
              .Append(',').Append(Txt(l.QtyReturned)).Append(',').Append(Txt(l.UnitPrice)).Append(',').Append(Txt(l.TaxRatePct)).Append(',').Append(l.TaxReason)
              .Append(',').Append(l.IsClosed ? '1' : '0').Append(';');
        var t = snapshot.Terms;
        sb.Append("|T:").Append(t.PaymentMethod).Append(',').Append(t.PaymentTerm).Append(',').Append(t.DeliveryMode).Append(',').Append(t.ConsigneeId)
          .Append(',').Append(t.IsConsignment ? '1' : '0').Append(',').Append(t.Notes).Append('|');
        foreach (var b in activeBatches.OrderBy(b => b.BatchId))
            sb.Append("B:").Append(b.BatchId).Append(',').Append(b.StatusCode).Append(',').Append(b.ShipmentId).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>La huella con el estado de la orden cargado para la modificación (sus recolecciones activas).</summary>
    public static string Fingerprint(ChangeOrderState state, SalesChangeSnapshot snapshot)
        => Fingerprint(state.StatusCode, snapshot, state.Batches.Where(b => b.StatusCode != Constants.PickBatchStatuses.Cancelled).Select(b => (b.BatchId, b.StatusCode, b.ShipmentId)));
}

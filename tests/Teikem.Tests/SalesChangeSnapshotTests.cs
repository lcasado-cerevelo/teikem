using Teikem.Domain.Sales;
using Xunit;
using Snap = Teikem.Domain.Sales.SalesChangeSnapshot;

namespace Teikem.Tests;

/// <summary>Lote 249 (P2) — foto v1 de la modificación: JSON estable, diferencia por campo, resumen (≤500) y huella SHA-256.</summary>
public sealed class SalesChangeSnapshotTests
{
    private static SalesChangeSnapshotLine Line(int id, decimal ordered, decimal price = 8m, decimal reserved = 0m, decimal back = 0m, decimal shipped = 0m,
        decimal invoiced = 0m, decimal pct = 11.5m, string? reason = "TAXABLE", bool closed = false, int? product = null, string? sku = null)
        => new(id, id, product ?? 100 + id, sku ?? $"SKU-{id}", $"Producto {id}", ordered, reserved, back, shipped, invoiced, 0m, price, price, "GENERAL_LIST", pct,
            Teikem.Domain.Billing.BillingRules.Money(SalesRules.LineAmount(ordered, price) * pct / 100m), reason, null, null, closed, ordered == 0m && closed);

    private static readonly SalesChangeSnapshotTerms Terms = new("CREDIT", "NET30", "DELIVERY", 9, "Tienda Norte", false, "Llamar antes");

    private static Snap Make(params SalesChangeSnapshotLine[] lines)
        => Snap.Create(lines, Terms, lines.Sum(l => SalesRules.LineAmount(l.QtyOrdered, l.UnitPrice)), lines.Sum(l => l.TaxAmount), 5m, 0.58m);

    private static readonly (int, string, int?)[] NoBatches = Array.Empty<(int, string, int?)>();

    [Fact]
    public void El_JSON_va_y_vuelve_sin_cambios_y_en_camelCase()
    {
        var s = Make(Line(1, 3m), Line(2, 0m, closed: true));
        var json = s.ToJson();
        Assert.Contains("\"lines\":[", json);
        Assert.Contains("\"qtyOrdered\":3", json);
        Assert.Contains("\"v\":1", json);
        var back = Snap.FromJson(json)!;
        Assert.Equal(json, back.ToJson());
        Assert.Equal(s.Total, back.Total);
        Assert.Equal(s.Lines.Count, back.Lines.Count);
        Assert.Equal(s.Lines[1], back.Lines[1]);
        Assert.Equal(s.Terms, back.Terms);
        Assert.Null(Snap.FromJson(null));
    }

    [Fact]
    public void El_total_lleva_IVU_y_envio()
    {
        var s = Make(Line(1, 3m));   // 24.00 + 2.76 + 5.00 + 0.58
        Assert.Equal(32.34m, s.Total);
        Assert.Equal(1, s.V);
    }

    [Fact]
    public void Desde_la_orden()
    {
        var o = new SalesOrder { Subtotal = 24m, TaxAmount = 2.76m, ShippingCharge = 5m, ShippingTaxAmount = 0.58m };
        o.Lines.Add(new SalesOrderLine { SalesOrderLineId = 11, LineNo = 2, ProductId = 5, QtyOrdered = 3m, QtyReserved = 3m, UnitPrice = 8m, TaxRatePct = 11.5m, TaxAmount = 2.76m });
        o.Lines.Add(new SalesOrderLine { SalesOrderLineId = 10, LineNo = 1, ProductId = 6, QtyOrdered = 0m, IsClosed = true, UnitPrice = 1m });
        var s = Snap.FromOrder(o, new Dictionary<int, (string, string)> { [5] = ("A-5", "Arroz"), [6] = ("B-6", "Batata") }, Terms);
        Assert.Equal(new[] { 1, 2 }, s.Lines.Select(l => l.LineNo));
        Assert.True(s.Lines[0].IsRemoved);
        Assert.Equal(("A-5", "Arroz"), (s.Lines[1].Sku, s.Lines[1].Name));
        Assert.Equal(32.34m, s.Total);
    }

    [Fact]
    public void El_diff_marca_agregadas_quitadas_y_cambiadas_por_campo()
    {
        var before = Make(Line(1, 3m), Line(2, 5m), Line(3, 0m, closed: true, sku: "RE"), Line(4, 2m));
        var after = Make(
            Line(1, 3m),                                // igual
            Line(2, 4m, price: 7.5m),                   // cambia cantidad y precio
            Line(3, 1m, sku: "RE"),                     // reactivada
            Line(4, 0m, closed: true),                  // quitada
            Line(0, 2m, product: 900, sku: "NEW"));     // nueva (aún sin id)
        var d = Snap.Diff(before, after);

        Assert.Equal(new[] { "RE", "NEW" }, d.Added.Select(a => a.Sku).OrderBy(x => x == "NEW"));
        Assert.All(d.Added, a => Assert.Equal(SalesChangeDiffKinds.Added, a.Change));
        var removed = Assert.Single(d.Removed);
        Assert.Equal(("SKU-4", "2", "0"), (removed.Sku, removed.Fields[0].Before, removed.Fields[0].After));
        var changed = Assert.Single(d.Changed);
        Assert.Equal("SKU-2", changed.Sku);
        Assert.Contains(changed.Fields, f => f == new SalesChangeFieldDiff("qtyOrdered", "5", "4"));
        Assert.Contains(changed.Fields, f => f == new SalesChangeFieldDiff("unitPrice", "8", "7.5"));
        Assert.Contains(changed.Fields, f => f.Field == "taxAmount");
        Assert.Empty(d.Terms);
        Assert.Equal(after.Total - before.Total, d.Delta);
        Assert.False(d.IsEmpty);
    }

    [Fact]
    public void El_diff_de_los_terminos_y_la_linea_que_falta()
    {
        var before = Make(Line(1, 3m), Line(2, 1m));
        var after = before with { Lines = new[] { before.Lines[0] }, Terms = Terms with { DeliveryMode = "PICKUP", ConsigneeId = null, ConsigneeName = null, Notes = "" } };
        var d = Snap.Diff(before, after);
        Assert.Equal("SKU-2", Assert.Single(d.Removed).Sku);
        Assert.Equal(new[] { "deliveryMode", "consignee", "notes" }, d.Terms.Select(t => t.Field));
        Assert.Equal(new SalesChangeFieldDiff("consignee", "Tienda Norte", null), d.Terms[1]);
        Assert.True(Snap.Diff(before, before).IsEmpty);
    }

    [Fact]
    public void El_resumen_es_legible_y_no_pasa_de_500()
    {
        var before = Make(Line(1, 3m), Line(2, 5m));
        var after = Make(Line(1, 3m), Line(2, 4m), Line(0, 2m, product: 900, sku: "NEW"));
        var text = Snap.Summary(Snap.Diff(before, after));
        Assert.StartsWith("Agregó: NEW ×2. Cambió: SKU-2 (cantidad 5 → 4", text);
        Assert.EndsWith(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Total {before.Total:0.00} → {after.Total:0.00}."), text);

        var many = Make(Enumerable.Range(1, 80).Select(i => Line(i, 1m, sku: $"PRODUCTO-LARGO-{i:000}")).ToArray());
        var none = Make(Line(999, 1m));
        var big = Snap.Summary(Snap.Diff(none, many));
        Assert.True(big.Length <= 500);
        Assert.EndsWith("…", big);
    }

    [Fact]
    public void La_huella_es_estable_con_el_orden_de_las_lineas()
    {
        var a = Make(Line(1, 3m), Line(2, 5m));
        var b = a with { Lines = a.Lines.Reverse().ToList() };
        var batches = new (int, string, int?)[] { (7, "COLLECTED", null), (3, "PACKED", 40) };
        var fa = Snap.Fingerprint("PARTIAL", a, batches);
        Assert.Equal(64, fa.Length);
        Assert.Matches("^[0-9a-f]{64}$", fa);
        Assert.Equal(fa, Snap.Fingerprint("PARTIAL", b, batches.Reverse()));
    }

    [Fact]
    public void La_huella_cambia_con_lo_despachado_lo_facturado_una_recoleccion_o_el_precio()
    {
        var a = Make(Line(1, 3m, shipped: 1, reserved: 2), Line(2, 5m, reserved: 5));
        var batches = new (int, string, int?)[] { (7, "COLLECTED", null) };
        var fa = Snap.Fingerprint("PARTIAL", a, batches);

        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", Make(Line(1, 3m, shipped: 2, reserved: 1), Line(2, 5m, reserved: 5)), batches));
        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", Make(Line(1, 3m, shipped: 1, reserved: 2, invoiced: 1), Line(2, 5m, reserved: 5)), batches));
        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", Make(Line(1, 3m, shipped: 1, reserved: 2, price: 9m), Line(2, 5m, reserved: 5)), batches));
        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", a, new (int, string, int?)[] { (7, "PACKED", 55) }));
        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", a, NoBatches));
        Assert.NotEqual(fa, Snap.Fingerprint("SHIPPED", a, batches));
        Assert.NotEqual(fa, Snap.Fingerprint("PARTIAL", a with { Terms = Terms with { PaymentMethod = "CASH" } }, batches));
    }

    [Fact]
    public void La_huella_no_cambia_con_lo_reservado_ni_el_backorder()
    {
        var a = Make(Line(1, 3m, shipped: 1, reserved: 2), Line(2, 5m, reserved: 5));
        var b = Make(Line(1, 3m, shipped: 1, back: 2), Line(2, 5m, reserved: 1, back: 4));
        var batches = new (int, string, int?)[] { (7, "COLLECTED", null) };
        Assert.Equal(Snap.Fingerprint("PARTIAL", a, batches), Snap.Fingerprint("PARTIAL", b, batches));
    }

    [Fact]
    public void La_huella_desde_el_estado_ignora_las_recolecciones_canceladas()
    {
        var snap = Make(Line(1, 3m, reserved: 3));
        var lines = new[] { ChangeStateBuilder.L(1, 3m, reserved: 3m) };
        var live = ChangeStateBuilder.S(lines, new[] { ChangeStateBuilder.Collected(7, (1, 1m)) });
        var withCancelled = ChangeStateBuilder.S(lines, new[] { ChangeStateBuilder.Collected(7, (1, 1m)), ChangeStateBuilder.Collected(8, (1, 1m)) with { StatusCode = "CANCELLED" } });
        Assert.Equal(Snap.Fingerprint(live, snap), Snap.Fingerprint(withCancelled, snap));
        Assert.Equal(Snap.Fingerprint("REQUESTED", snap, new (int, string, int?)[] { (7, "COLLECTED", null) }), Snap.Fingerprint(live, snap));
    }
}

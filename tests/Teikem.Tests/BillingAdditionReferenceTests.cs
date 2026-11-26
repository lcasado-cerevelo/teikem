using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P6): la factura complementaria de la orden de ADICIÓN — la referencia «(adición de OV-… · complementa FAC-…)» en la descripción de la línea (recortada a 250), sin cambiar
// montos, cantidades ni IVU; InvoiceDto.Complements; y la base de facturación ON_REQUEST (lo pedido y aún no facturado, sin esperar el despacho).
public sealed class BillingAdditionReferenceTests
{
    // ------------------------------------------------------------------ reglas puras

    [Fact]
    public void Describe_with_parent_and_invoices_gives_the_exact_reference()
    {
        Assert.Equal("OV-00045 (adición de OV-00040 · complementa FAC-00012) · SKU Nombre", BillingRules.Describe("OV-00045", "SKU", "Nombre", "OV-00040", new[] { "FAC-00012" }));
        Assert.Equal("OV-00045 (adición de OV-00040 · complementa FAC-00012, FAC-00015) · SKU Nombre",
            BillingRules.Describe("OV-00045", "SKU", "Nombre", "OV-00040", new[] { "FAC-00012", "FAC-00015" }));
        Assert.Equal("OV-00045 (adición de OV-00040) · SKU Nombre", BillingRules.Describe("OV-00045", "SKU", "Nombre", "OV-00040", Array.Empty<string>()));
        Assert.Equal("OV-00045 (adición de OV-00040) · SKU Nombre", BillingRules.Describe("OV-00045", "SKU", "Nombre", "OV-00040"));
        // sin madre: el formato de siempre
        Assert.Equal("OV-00040 · SKU Nombre", BillingRules.Describe("OV-00040", "SKU", "Nombre"));
        Assert.Equal(BillingRules.Describe("OV-00040", "SKU", "Nombre"), BillingRules.Describe("OV-00040", "SKU", "Nombre", null, new[] { "FAC-1" }));
    }

    [Fact]
    public void Describe_is_cut_to_250_dropping_invoice_numbers_first_then_the_name_and_never_the_order_number()
    {
        // muchas facturas: sobran primero los números de factura (los que no caben → «…») y el nombre queda entero
        var invoices = Enumerable.Range(1, 40).Select(i => $"FAC-{i:00000}").ToArray();
        var text = BillingRules.Describe("OV-00045", "SKU-1", "Nombre del producto", "OV-00040", invoices);
        Assert.True(text.Length <= BillingRules.MaxDescriptionLength);
        Assert.StartsWith("OV-00045 (adición de OV-00040 · complementa FAC-00001, ", text);
        Assert.Contains(", …) · SKU-1 Nombre del producto", text);
        Assert.EndsWith("Nombre del producto", text);

        // nombre larguísimo: ya sin facturas, se recorta el nombre con «…»
        var longName = new string('x', 400);
        var cut = BillingRules.Describe("OV-00045", "SKU-1", longName, "OV-00040", new[] { "FAC-00012" });
        Assert.Equal(BillingRules.MaxDescriptionLength, cut.Length);
        Assert.StartsWith("OV-00045 (adición de OV-00040) · SKU-1 xxx", cut);
        Assert.EndsWith("…", cut);

        // sin madre también se recorta (antes podía pasar del largo de la columna)
        var plain = BillingRules.Describe("OV-00045", "SKU-1", longName);
        Assert.Equal(BillingRules.MaxDescriptionLength, plain.Length);
        Assert.StartsWith("OV-00045 · SKU-1 x", plain);

        // ni con un SKU enorme se corta el número de la orden ni la referencia a la madre
        var hugeSku = BillingRules.Describe("OV-00045", new string('S', 400), "Nombre", "OV-00040", invoices);
        Assert.True(hugeSku.Length <= BillingRules.MaxDescriptionLength);
        Assert.StartsWith("OV-00045 (adición de OV-00040) · SSS", hugeSku);
    }

    [Fact]
    public void Memo_reference_and_memo_cap()
    {
        Assert.Equal("OV-00045 (adición de OV-00040 · FAC-00012)", BillingRules.MemoReference("OV-00045", "OV-00040", new[] { "FAC-00012" }));
        Assert.Equal("OV-00045 (adición de OV-00040 · FAC-00012, FAC-00015)", BillingRules.MemoReference("OV-00045", "OV-00040", new[] { "FAC-00012", "FAC-00015" }));
        Assert.Equal("OV-00045 (adición de OV-00040)", BillingRules.MemoReference("OV-00045", "OV-00040", Array.Empty<string>()));
        Assert.Equal("OV-00040", BillingRules.MemoReference("OV-00040", null, new[] { "FAC-1" }));
        Assert.Equal("OV-1, OV-2", BillingRules.InvoiceMemo(new[] { "OV-1", "OV-2" }));
        var many = BillingRules.InvoiceMemo(Enumerable.Range(1, 1000).Select(i => $"OV-{i:00000}"));
        Assert.True(many.Length <= BillingRules.MaxInvoiceMemoLength);
        Assert.EndsWith(", …", many);
    }

    [Fact]
    public void Billable_on_request_is_what_was_ordered_minus_returned_and_invoiced()
    {
        Assert.Equal(10m, BillingRules.BillableOnRequest(10m, 10m, 0m, 0m, 0m, false));   // nada despachado: se factura lo pedido
        Assert.Equal(6m, BillingRules.BillableOnRequest(10m, 2m, 8m, 0m, 4m, false));
        Assert.Equal(0m, BillingRules.BillableOnRequest(10m, 0m, 10m, 0m, 10m, false));
        Assert.Equal(0m, BillingRules.BillableOnRequest(10m, 0m, 10m, 2m, 10m, false));   // nunca negativo
        Assert.Equal(5m, BillingRules.BillableOnRequest(10m, 1m, 4m, 0m, 0m, true));   // backorder cerrado: cuenta lo despachado + lo reservado
        Assert.Equal(0m, BillingRules.BillableOnRequest(0m, 0m, 0m, 0m, 0m, true));   // línea quitada por una modificación
        Assert.Contains(SalesBillingTriggers.OnRequest, SalesBillingTriggers.All);
    }

    // ------------------------------------------------------------------ servicio

    private sealed record World(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client C, SalesOrderService Sales, BillingService Billing, Product P);

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<BillingService>();
        });
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Billing);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = 100m, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w, c, f.Get<SalesOrderService>(), f.Get<BillingService>(), p);
    }

    private static async Task<SalesOrderDto> OrderAsync(World x, decimal qty, decimal shipped)
    {
        var d = await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P.PublicId, qty) }), default);
        var o = await x.Sales.RequestAsync(d.PublicId, null, default);
        if (shipped > 0m) o = (await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, shipped) }), default)).Order;
        return o;
    }

    /// <summary>La línea de la orden con su IVU (copia) y, si se pide, la orden marcada como adición de `parent`.</summary>
    private static async Task PrepareAsync(World x, SalesOrderDto o, decimal tax, SalesOrderDto? parent = null)
    {
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync(l => l.SalesOrderLineId == o.Lines[0].Id);
        (line.TaxAmount, line.TaxRatePct) = (tax, 11.5m);
        if (parent is not null)
        {
            var parentId = await x.F.Db.Set<SalesOrder>().AsNoTracking().Where(s => s.PublicId == parent.PublicId).Select(s => s.SalesOrderId).SingleAsync();
            var so = await x.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(s => s.PublicId == o.PublicId);
            so.ParentSalesOrderId = parentId;
        }
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static BillingRunRequest Period() => new(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    [Fact]
    public async Task The_addition_line_says_whose_addition_it_is_and_which_invoices_it_complements()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var mother = await OrderAsync(x, 10m, 4m);
        await PrepareAsync(x, mother, 11.50m);
        var first = await x.Billing.GenerateAsync(Period(), default);
        var motherInvoice = Assert.Single(first.Invoices);
        Assert.Equal("FAC-00001", motherInvoice.Number);

        var addition = await OrderAsync(x, 3m, 3m);
        await PrepareAsync(x, addition, 3.45m, mother);
        var second = await x.Billing.GenerateAsync(Period(), default);
        var inv = Assert.Single(second.Invoices);
        var detail = await x.Billing.GetInvoiceAsync(inv.PublicId, default);
        var line = Assert.Single(detail.Lines);
        Assert.Equal($"{addition.Number} (adición de {mother.Number} · complementa FAC-00001) · MED Producto MED", line.Description);
        Assert.Equal((3m, 10m, 3.45m, addition.Number), (line.Quantity, line.UnitAmount, line.TaxAmount, line.SalesOrderNumber));
        Assert.Equal(new[] { "FAC-00001" }, detail.Complements);
        Assert.Empty((await x.Billing.GetInvoiceAsync(motherInvoice.PublicId, default)).Complements!);
    }

    [Fact]
    public async Task The_reference_changes_no_amount_quantity_or_tax()
    {
        async Task<(decimal Total, decimal Subtotal, decimal Tax, decimal Invoiced)> RunAsync(bool asAddition)
        {
            var x = await NewWorldAsync();
            await using var _ = x.F;
            var mother = await OrderAsync(x, 10m, 4m);
            await PrepareAsync(x, mother, 11.50m);
            await x.Billing.GenerateAsync(Period(), default);
            var other = await OrderAsync(x, 3m, 3m);
            await PrepareAsync(x, other, 3.45m, asAddition ? mother : null);
            var run = await x.Billing.GenerateAsync(Period(), default);
            var line = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderLineId == other.Lines[0].Id);
            return (run.TotalAmount, run.Invoices[0].Subtotal, run.Invoices[0].TaxAmount, line.QtyInvoiced);
        }
        var plain = await RunAsync(false);
        var withRef = await RunAsync(true);
        Assert.Equal(plain, withRef);
        Assert.Equal((33.45m, 30m, 3.45m, 3m), withRef);
    }

    [Fact]
    public async Task An_addition_in_the_same_run_as_its_mother_goes_in_the_same_invoice()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var mother = await OrderAsync(x, 10m, 4m);
        await PrepareAsync(x, mother, 11.50m);
        var addition = await OrderAsync(x, 3m, 3m);
        await PrepareAsync(x, addition, 3.45m, mother);
        var run = await x.Billing.GenerateAsync(Period(), default);
        var inv = Assert.Single(run.Invoices);   // mismo cliente y moneda: una sola factura
        var detail = await x.Billing.GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Contains(detail.Lines, l => l.Description == $"{mother.Number} · MED Producto MED");
        Assert.Contains(detail.Lines, l => l.Description == $"{addition.Number} (adición de {mother.Number}) · MED Producto MED");   // la madre aún no tenía factura
        Assert.Empty(detail.Complements!);
        Assert.Equal((70m, 8.05m), (detail.Subtotal, detail.TaxAmount));   // 4.60 (4 de 10 de la madre) + 3.45

        // cancelar la corrida libera todo (cabeceras → líneas)
        await x.Billing.CancelAsync(run.Id, "prueba", default);
        Assert.All(await x.F.Db.Set<SalesOrderLine>().AsNoTracking().ToListAsync(), l => Assert.Equal(0m, l.QtyInvoiced));
    }

    [Fact]
    public async Task A_voided_mother_invoice_is_not_complemented()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var mother = await OrderAsync(x, 10m, 4m);
        await PrepareAsync(x, mother, 0m);
        var first = await x.Billing.GenerateAsync(Period(), default);
        await x.Billing.CancelAsync(first.Id, "se anula", default);   // FAC-00001 queda anulada
        var again = await x.Billing.GenerateAsync(Period(), default);   // FAC-00002 de la madre
        Assert.Equal("FAC-00002", again.Invoices[0].Number);
        var addition = await OrderAsync(x, 2m, 2m);
        await PrepareAsync(x, addition, 0m, mother);
        var run = await x.Billing.GenerateAsync(Period(), default);
        var detail = await x.Billing.GetInvoiceAsync(run.Invoices[0].PublicId, default);
        Assert.Equal($"{addition.Number} (adición de {mother.Number} · complementa FAC-00002) · MED Producto MED", Assert.Single(detail.Lines).Description);
        Assert.Equal(new[] { "FAC-00002" }, detail.Complements);
    }

    [Fact]
    public async Task With_the_on_request_basis_what_was_ordered_is_billed_without_waiting_for_the_dispatch()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(new BillingSettingsDto(SalesBillingTriggers.OnRequest, 30), await x.Billing.UpdateSettingsAsync(new BillingSettingsRequest("on_request", null), default));
        await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P.PublicId, 7m) }), default);   // borrador: no
        var o = await OrderAsync(x, 10m, 0m);   // solicitada, sin despachar
        await PrepareAsync(x, o, 11.50m);

        var run = await x.Billing.GenerateAsync(Period(), default);
        var inv = Assert.Single(run.Invoices);
        Assert.Equal((100m, 11.50m), (inv.Subtotal, inv.TaxAmount));
        var line = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderLineId == o.Lines[0].Id);
        Assert.Equal((10m, 0m), (line.QtyInvoiced, line.QtyShipped));   // facturado más que lo despachado: vale con ON_REQUEST
        var nothing = await Assert.ThrowsAnyAsync<TeikemException>(() => x.Billing.GenerateAsync(Period(), default));
        Assert.Equal(BillingRules.NothingToBill, nothing.Message);
        // despachar después no vuelve a facturar
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 4m) }), default);
        Assert.Equal(BillingRules.NothingToBill, (await Assert.ThrowsAnyAsync<TeikemException>(() => x.Billing.GenerateAsync(Period(), default))).Message);

        // de vuelta a ON_DISPATCH: lo pedido sin despachar ya no entra
        await x.Billing.UpdateSettingsAsync(new BillingSettingsRequest(SalesBillingTriggers.OnDispatch, null), default);
        await OrderAsync(x, 5m, 0m);
        Assert.Equal(BillingRules.NothingToBill, (await Assert.ThrowsAnyAsync<TeikemException>(() => x.Billing.GenerateAsync(Period(), default))).Message);
    }
}

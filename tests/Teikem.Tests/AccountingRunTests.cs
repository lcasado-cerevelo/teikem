using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Accounting;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 181 (H1): contabilización de compras y de despachos — solo lo propio, con el costo ya congelado, sin repetir una línea en dos corridas vivas.
public sealed class AccountingRunTests
{
    private static DateOnly Today => TenantClock.Default.Today;
    private static AccountingRunRequest Period(string kind, DateOnly? from = null, DateOnly? to = null) => new(kind, from ?? Today.AddDays(-1), to ?? Today);

    private sealed class World(WmsFixture f, AccountingRunService svc, Product own, Product other)
    {
        public WmsFixture F { get; } = f;
        public AccountingRunService Svc { get; } = svc;
        public Product Own { get; } = own;
        public Product Other { get; } = other;
    }

    /// <summary>Un recibo de una compra (orden → aviso → recibo) con 3 líneas: 10 de lo propio a 5.00, 4 de lo propio sin costo y 7 de un producto de cliente; más un recibo sin confirmar y una devolución de venta.</summary>
    private static async Task<World> NewWorldAsync(bool withDispatch = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<ExportTemplateService>();
            s.AddSingleton<AccountingRunService>();
        });
        f.SetModules(ModuleKeys.Purchasing, ModuleKeys.WmsLotSerial, ModuleKeys.Billing);
        var w = await f.AddWarehouseAsync("W1");
        var client = await f.AddClientAsync("CLI");
        var own = await f.AddProductAsync("PROPIO", purchaseCost: 5m);
        var other = await f.AddProductAsync("AJENO", ownerClientId: client.ClientId, purchaseCost: 9m);
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Proveedor Uno", IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        var po = new PurchaseOrder { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = "PO-00001", OrderDate = Today, StatusCodeId = 1, IsActive = true };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        var asn = new Asn { TenantId = WmsFixture.TenantId, Number = "LLE-00001", WarehouseId = w.WarehouseId, PurchaseOrderId = po.PurchaseOrderId, StatusCodeId = 1, IsActive = true };
        f.Db.Set<Asn>().Add(asn);
        await f.Db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var confirmed = await f.AddReceiptAsync(w, ReceiptStatuses.Received);
        confirmed.AsnId = asn.AsnId;
        confirmed.ReceivedAtUtc = now;
        f.Db.Update(confirmed);
        f.Db.Set<ReceiptLine>().AddRange(
            new ReceiptLine { ReceiptHeaderId = confirmed.ReceiptHeaderId, ProductId = own.ProductId, ReceivedQty = 10m, UnitCost = 5m, CostSource = CostSources.PurchaseOrder },
            new ReceiptLine { ReceiptHeaderId = confirmed.ReceiptHeaderId, ProductId = own.ProductId, ReceivedQty = 4m },
            new ReceiptLine { ReceiptHeaderId = confirmed.ReceiptHeaderId, ProductId = other.ProductId, ReceivedQty = 7m, UnitCost = 9m, CostSource = CostSources.Product });
        var open = await f.AddReceiptAsync(w, ReceiptStatuses.Expected);
        f.Db.Set<ReceiptLine>().Add(new ReceiptLine { ReceiptHeaderId = open.ReceiptHeaderId, ProductId = own.ProductId, ReceivedQty = 99m, UnitCost = 5m });
        var sales = await f.AddReceiptAsync(w, ReceiptStatuses.Received, ReceiptTypes.Return);
        sales.ReceivedAtUtc = now;
        sales.SalesReturnId = 1;
        f.Db.Update(sales);
        f.Db.Set<ReceiptLine>().Add(new ReceiptLine { ReceiptHeaderId = sales.ReceiptHeaderId, ProductId = own.ProductId, ReceivedQty = 2m, UnitCost = 5m });

        if (withDispatch)
        {
            var batch = new PickBatch { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = "EMP-00001", StatusCodeId = 1, CollectedAtUtc = now, IsActive = true };
            batch.Lines.Add(new PickBatchLine { ProductId = own.ProductId, FromBinId = 1, Quantity = 3m, UnitCost = 5m, IssueTxnId = 1 });
            batch.Lines.Add(new PickBatchLine { ProductId = other.ProductId, FromBinId = 1, Quantity = 2m, UnitCost = 9m, IssueTxnId = 2 });
            batch.Lines.Add(new PickBatchLine { ProductId = own.ProductId, FromBinId = 1, Quantity = 1m, UnitCost = 5m, IssueTxnId = 3, ReversalTxnId = 4 });
            var deleted = new PickBatch { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = "EMP-00002", StatusCodeId = 1, CollectedAtUtc = now, IsActive = false };
            deleted.Lines.Add(new PickBatchLine { ProductId = own.ProductId, FromBinId = 1, Quantity = 8m, UnitCost = 5m, IssueTxnId = 5, ReversalTxnId = 6 });
            f.Db.Set<PickBatch>().AddRange(batch, deleted);
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, f.Get<AccountingRunService>(), own, other);
    }

    [Fact]
    public void Only_own_inventory_is_accountable_and_the_total_is_quantity_times_the_frozen_cost()
    {
        Assert.True(AccountingRules.IsAccountable(null));
        Assert.False(AccountingRules.IsAccountable(7));
        Assert.Equal(50m, AccountingRules.LineTotal(10m, 5m));
        Assert.Equal(1.6667m, AccountingRules.LineTotal(5m, 0.33333m));
        Assert.Null(AccountingRules.LineTotal(4m, null));
        Assert.True(AccountingRules.IsKnownKind("PURCHASES"));
        Assert.False(AccountingRules.IsKnownKind("purchases"));
        // el servicio real de contadores rechaza los tipos que no conoce (las pruebas con contador de mentira no lo ven)
        Assert.True(Teikem.Domain.Orders.NumberingRules.IsKnownKind(AccountingCodes.NumberKindPurchases));
        Assert.True(Teikem.Domain.Orders.NumberingRules.IsKnownKind(AccountingCodes.NumberKindDispatches));
        Assert.Equal("CMP-00007", Teikem.Domain.Clients.NumberFormat.Resolve(AccountingCodes.NumberPatternPurchases, 7));
        Assert.Equal("DSP-00007", Teikem.Domain.Clients.NumberFormat.Resolve(AccountingCodes.NumberPatternDispatches, 7));
    }

    [Fact]
    public async Task Purchases_take_only_confirmed_own_receipt_lines_with_their_frozen_cost()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var preview = await x.Svc.PreviewAsync(AccountingCodes.KindPurchases, Period("X"), default);
        Assert.Equal((2, 50m, 1), (preview.LineCount, preview.TotalAmount, preview.UnpricedLineCount));   // lo del cliente, el recibo sin confirmar y la devolución de venta quedan fuera

        var run = await x.Svc.GenerateAsync(AccountingCodes.KindPurchases, Period("X"), default);
        Assert.Equal(("CMP-00001", "PURCHASES", "GENERATED", 2, 50m, 1), (run.Number, run.Kind, run.StatusCode, run.LineCount, run.TotalAmount, run.UnpricedLineCount));
        var priced = run.Lines.Single(l => l.UnitCost != null);
        Assert.Equal(("PROPIO", 10m, 5m, 50m, "PO", "PO-00001", "Proveedor Uno", "W1"), (priced.Sku, priced.Quantity, priced.UnitCost, priced.TotalCost, priced.CostSource, priced.ReferenceNumber, priced.PartyName, priced.WarehouseCode));
        var unpriced = run.Lines.Single(l => l.UnitCost == null);
        Assert.Equal((4m, null), (unpriced.Quantity, unpriced.TotalCost));

        // el costo del producto cambia después: la corrida no se recalcula
        var p = await x.F.Db.Set<Product>().AsTracking().SingleAsync(p => p.ProductId == x.Own.ProductId);
        p.PurchaseCost = 99m;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(50m, (await x.Svc.GetAsync(AccountingCodes.KindPurchases, run.PublicId, default)).TotalAmount);
    }

    [Fact]
    public async Task A_line_is_never_in_two_live_runs_and_cancelling_frees_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var first = await x.Svc.GenerateAsync(AccountingCodes.KindPurchases, Period("X"), default);
        Assert.Equal(AccountingRules.NothingToAccount, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.GenerateAsync(AccountingCodes.KindPurchases, Period("X"), default))).Message);
        Assert.Equal(0, (await x.Svc.PreviewAsync(AccountingCodes.KindPurchases, Period("X"), default)).LineCount);

        var cancelled = await x.Svc.CancelAsync(AccountingCodes.KindPurchases, first.PublicId, "me equivoqué", default);
        Assert.Equal("CANCELLED", cancelled.StatusCode);
        var again = await x.Svc.GenerateAsync(AccountingCodes.KindPurchases, Period("X"), default);
        Assert.Equal(("CMP-00002", 2), (again.Number, again.LineCount));
        Assert.Equal(AccountingRules.CancelNeedsOpen, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CancelAsync(AccountingCodes.KindPurchases, first.PublicId, null, default))).Message);
    }

    [Fact]
    public async Task Accepting_then_exporting_builds_the_file_with_the_template_and_marks_it_exported()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var run = await x.Svc.GenerateAsync(AccountingCodes.KindPurchases, Period("X"), default);
        Assert.Equal(AccountingRules.ExportNeedsAccepted, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.ExportAsync(AccountingCodes.KindPurchases, run.PublicId, null, default))).Message);

        var accepted = await x.Svc.AcceptAsync(AccountingCodes.KindPurchases, run.PublicId, default);
        Assert.Equal("ACCEPTED", accepted.StatusCode);
        Assert.NotNull(accepted.AcceptedAtUtc);
        Assert.Equal(AccountingRules.AcceptNeedsGenerated, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.AcceptAsync(AccountingCodes.KindPurchases, run.PublicId, default))).Message);

        var preview = await x.Svc.PreviewExportAsync(AccountingCodes.KindPurchases, run.PublicId, null, default);
        Assert.Equal("ACCEPTED", (await x.Svc.GetAsync(AccountingCodes.KindPurchases, run.PublicId, default)).StatusCode);   // la vista previa no cambia nada
        var file = await x.Svc.ExportAsync(AccountingCodes.KindPurchases, run.PublicId, null, default);
        Assert.Equal(preview.Text, file.Text);
        Assert.Equal(2, file.RowCount);
        Assert.StartsWith("RunNo,PeriodStart,PeriodEnd,LineNo,ReceiptDate,ReceiptNo,PoNo,Supplier,Warehouse,Item,Description,Lot,Qty,UnitCost,Amount,CostSource,Currency,RunTotal", file.Text);
        Assert.Contains("CMP-00001", file.Text);
        Assert.Contains("PO-00001,Proveedor Uno,W1,PROPIO,Producto PROPIO,,10,5,50.00,PO,USD,50.00", file.Text);
        Assert.Contains("PROPIO,Producto PROPIO,,4,,,,USD,50.00", file.Text);   // sin costo: en blanco
        var done = await x.Svc.GetAsync(AccountingCodes.KindPurchases, run.PublicId, default);
        Assert.Equal(("EXPORTED", true), (done.StatusCode, done.ExportedAtUtc != null));
        Assert.Equal(2, (await x.Svc.ExportAsync(AccountingCodes.KindPurchases, run.PublicId, null, default)).RowCount);   // una exportada se puede volver a exportar
        Assert.Equal(AccountingRules.CancelNeedsOpen, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CancelAsync(AccountingCodes.KindPurchases, run.PublicId, null, default))).Message);
    }

    [Fact]
    public async Task Dispatches_take_only_live_own_pick_lines_and_the_kinds_do_not_mix()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var run = await x.Svc.GenerateAsync(AccountingCodes.KindDispatches, Period("X"), default);
        Assert.Equal(("DSP-00001", "DISPATCHES", 1, 15m), (run.Number, run.Kind, run.LineCount, run.TotalAmount));   // sin lo del cliente, sin lo revertido y sin la recolección eliminada
        var line = Assert.Single(run.Lines);
        Assert.Equal(("EMP-00001", "PROPIO", 3m, 5m, 15m), (line.DocumentNumber, line.Sku, line.Quantity, line.UnitCost, line.TotalCost));

        // una corrida de compras y una de despachos son cosas distintas: cada una solo se ve desde su tipo
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetAsync(AccountingCodes.KindPurchases, run.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.AcceptAsync(AccountingCodes.KindPurchases, run.PublicId, default));
        Assert.Empty((await x.Svc.ListAsync(AccountingCodes.KindPurchases, null, null, 0, 50, default)).Items);
        Assert.Single((await x.Svc.ListAsync(AccountingCodes.KindDispatches, "GENERATED", "DSP", 0, 50, default)).Items);

        await x.Svc.AcceptAsync(AccountingCodes.KindDispatches, run.PublicId, default);
        var file = await x.Svc.ExportAsync(AccountingCodes.KindDispatches, run.PublicId, null, default);
        Assert.StartsWith("RunNo,PeriodStart,PeriodEnd,LineNo,DispatchDate,DispatchNo,OrderNo,Customer,Warehouse,Item", file.Text);
        Assert.Contains("EMP-00001", file.Text);
    }

    [Fact]
    public async Task The_period_limits_what_enters_and_the_request_is_validated()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var old = Period("X", Today.AddDays(-30), Today.AddDays(-20));
        Assert.Equal(0, (await x.Svc.PreviewAsync(AccountingCodes.KindPurchases, old, default)).LineCount);
        Assert.Equal(AccountingRules.NothingToAccount, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.GenerateAsync(AccountingCodes.KindDispatches, old, default))).Message);
        Assert.Equal(AccountingRules.PeriodRequired, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", null, null), default))).Message);
        Assert.Equal(AccountingRules.PeriodInvalid, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", Today, Today.AddDays(-1)), default))).Message);
        Assert.Equal(AccountingRules.NoteTooLong, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.PreviewAsync(AccountingCodes.KindPurchases, new AccountingRunRequest("X", Today, Today, new string('n', 501)), default))).Message);
        Assert.Equal(AccountingRules.KindInvalid, (await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ListAsync("OTRO", null, null, 0, 50, default))).Message);
    }
}

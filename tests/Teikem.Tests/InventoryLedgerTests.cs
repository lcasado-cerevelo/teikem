using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0 — InventoryLedger (la ÚNICA vía de escritura del inventario) sobre InMemory con WmsFixture:
/// - signo del ledger (D3): RECEIPT +, ISSUE −, TRANSFER + con From y To, ADJUSTMENT de salida −; Ref en el INSERT (D48);
/// - guardas: disponible insuficiente → 409 insufficient_stock sin cambios; motivo obligatorio; producto/posición inactivos;
/// - series: nacimiento AVAILABLE, salida desde otra posición → 409, reversa SHIPPED → AVAILABLE con historial;
/// - reservas: lo reservado no se despacha salvo FromReserved; liberar de más → 409;
/// - conciliación ledger ↔ saldo e ids en el orden de entrada.
/// </summary>
public class InventoryLedgerTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B1, WarehouseBin B2, WarehouseBin Inactive, Product PN, Product PS);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("ALM-01");
        var pck = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var b1 = await f.AddBinAsync(pck, "A01");
        var b2 = await f.AddBinAsync(pck, "A02");
        var inactive = await f.AddBinAsync(pck, "A99", isActive: false);
        var pn = await f.AddProductAsync("PN");
        var ps = await f.AddProductAsync("PS", TrackingTypes.Serial);
        return new World(f, w, b1, b2, inactive, pn, ps);
    }

    private static InventoryPosting Receipt(World w, Product p, WarehouseBin b, decimal qty, string? serial = null)
        => new(InventoryTxnTypes.Receipt, p.ProductId, qty, SerialNumber: serial, ToWarehouseId: w.W.WarehouseId, ToBinId: b.WarehouseBinId,
            RefEntityType: EntityTypes.Receipt, RefId: 77);

    private static InventoryPosting Issue(World w, Product p, WarehouseBin b, decimal qty, string? serial = null, bool fromReserved = false)
        => new(InventoryTxnTypes.Issue, p.ProductId, qty, SerialNumber: serial, FromWarehouseId: w.W.WarehouseId, FromBinId: b.WarehouseBinId,
            RefEntityType: EntityTypes.PickBatch, RefId: 55, FromReserved: fromReserved);

    [Fact]
    public async Task Receipt_is_positive_and_issue_negative_with_ref_in_the_insert()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 10m));
        var ids = await w.F.PostAsync(Issue(w, w.PN, w.B1, 3m));

        var txns = await w.F.TransactionsAsync();
        Assert.Equal(new[] { 10m, -3m }, txns.Select(t => t.Quantity));
        var issue = txns.Single(t => t.InventoryTransactionId == ids[0]);
        Assert.Equal(w.F.LookupId(LookupDomains.EntityType, EntityTypes.PickBatch), issue.RefEntityLookupId);
        Assert.Equal(55, issue.RefId);
        Assert.Equal(w.W.WarehouseId, issue.FromWarehouseId);
        Assert.Null(issue.ToWarehouseId);
        Assert.Equal(1, issue.CreatedBy);
        Assert.Equal(7m, await w.F.OnHandAsync(w.PN.ProductId, w.B1.WarehouseBinId));
    }

    [Fact]
    public async Task Issue_above_available_is_409_insufficient_stock_without_changes()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 2m));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => w.F.PostAsync(Issue(w, w.PN, w.B1, 3m)));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("insufficient_stock", ex.Code);
        Assert.Equal("Inventario insuficiente de PN en A01: disponible 2, solicitado 3.", ex.Message);
        Assert.Contains("postings[0]", ex.Errors!.Keys);
        Assert.Single(await w.F.TransactionsAsync());
        Assert.Equal(2m, await w.F.OnHandAsync(w.PN.ProductId, w.B1.WarehouseBinId));
    }

    [Fact]
    public async Task Transfer_is_one_positive_row_with_from_and_to()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 5m));
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.PN.ProductId, 2m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B2.WarehouseBinId));

        var t = (await w.F.TransactionsAsync()).Last();
        Assert.Equal(2m, t.Quantity);
        Assert.Equal(w.B1.WarehouseBinId, t.FromBinId);
        Assert.Equal(w.B2.WarehouseBinId, t.ToBinId);
        Assert.Equal(3m, await w.F.OnHandAsync(w.PN.ProductId, w.B1.WarehouseBinId));
        Assert.Equal(2m, await w.F.OnHandAsync(w.PN.ProductId, w.B2.WarehouseBinId));
    }

    [Fact]
    public async Task Adjustment_requires_a_reason_and_stores_the_sign_of_its_direction()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 5m));

        var noReason = await Assert.ThrowsAsync<ValidationException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment,
            w.PN.ProductId, 1m, FromWarehouseId: w.W.WarehouseId, FromBinId: w.B1.WarehouseBinId)));
        Assert.Contains(InventoryRules.ReasonRequired, noReason.Errors!["postings[0]"]);

        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.PN.ProductId, 1m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, ReasonCode: AdjustmentReasons.Damage));
        var adj = (await w.F.TransactionsAsync()).Last();
        Assert.Equal(-1m, adj.Quantity);
        Assert.Equal(w.F.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.Damage), adj.ReasonLookupId);
        Assert.Equal(4m, await w.F.OnHandAsync(w.PN.ProductId, w.B1.WarehouseBinId));
    }

    [Fact]
    public async Task Inactive_product_or_bin_rejects_entries_with_422()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var inactiveProduct = await w.F.AddProductAsync("PX", isActive: false);

        var p = await Assert.ThrowsAsync<StatusRuleException>(() => w.F.PostAsync(Receipt(w, inactiveProduct, w.B1, 1m)));
        Assert.Equal(InventoryRules.ProductInactiveMessage("PX"), p.Message);
        var b = await Assert.ThrowsAsync<StatusRuleException>(() => w.F.PostAsync(Receipt(w, w.PN, w.Inactive, 1m)));
        Assert.Equal(InventoryRules.BinInactiveMessage("A99"), b.Message);
        Assert.Empty(await w.F.TransactionsAsync());
    }

    [Fact]
    public async Task Bin_of_another_warehouse_is_404()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var w2 = await w.F.AddWarehouseAsync("ALM-02");
        var z2 = await w.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking);
        var c1 = await w.F.AddBinAsync(z2, "C01");
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, w.PN.ProductId, 1m,
            ToWarehouseId: w.W.WarehouseId, ToBinId: c1.WarehouseBinId)));
        Assert.Equal("Posición no encontrada.", ex.Message);
    }

    [Fact]
    public async Task Serial_is_born_available_cannot_leave_from_another_bin_and_returns_from_shipped()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PS, w.B1, 1m, "S-1"));
        var born = await w.F.SerialAsync(w.PS.ProductId, "S-1");
        Assert.Equal(SerialStatuses.Available, w.F.StatusCodeOf(born.StatusCodeId!.Value));
        Assert.Equal(w.B1.WarehouseBinId, born.CurrentBinId);

        // Recibir otra vez la misma serie en inventario → 409.
        var again = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(Receipt(w, w.PS, w.B2, 1m, "S-1")));
        Assert.Equal(SerialRules.AlreadyInStock("S-1"), again.Message);

        // Salida desde otra posición → 409.
        var elsewhere = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(Issue(w, w.PS, w.B2, 1m, "S-1")));
        Assert.Equal(SerialRules.NotAvailable("S-1", "A02"), elsewhere.Message);

        // Despacho → SHIPPED sin ubicación; reversa (ADJUSTMENT de entrada) → AVAILABLE con historial.
        await w.F.PostAsync(Issue(w, w.PS, w.B1, 1m, "S-1"));
        var shipped = await w.F.SerialAsync(w.PS.ProductId, "S-1");
        Assert.Equal(SerialStatuses.Shipped, w.F.StatusCodeOf(shipped.StatusCodeId!.Value));
        Assert.Null(shipped.CurrentBinId);
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.PS.ProductId, 1m, SerialNumber: "S-1",
            ToWarehouseId: w.W.WarehouseId, ToBinId: w.B1.WarehouseBinId, ReasonCode: AdjustmentReasons.PickBatchReversal));
        var back = await w.F.SerialAsync(w.PS.ProductId, "S-1");
        Assert.Equal(SerialStatuses.Available, w.F.StatusCodeOf(back.StatusCodeId!.Value));
        Assert.Equal(w.B1.WarehouseBinId, back.CurrentBinId);

        var serialType = w.F.LookupId(LookupDomains.EntityType, EntityTypes.InventorySerial);
        var history = await w.F.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == serialType && h.EntityId == back.SerialId).OrderBy(h => h.EntityStatusHistoryId)
            .Select(h => h.ToStatusCodeId).ToListAsync();
        Assert.Equal(new[] { SerialStatuses.Available, SerialStatuses.Shipped, SerialStatuses.Available }, history.Select(w.F.StatusCodeOf));
        Assert.All(await w.F.TransactionsAsync(), t => Assert.Equal(back.SerialId, t.SerialId));
    }

    [Fact]
    public async Task Scrapped_serial_never_returns()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PS, w.B1, 1m, "S-9"));
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.PS.ProductId, 1m, SerialNumber: "S-9",
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.B1.WarehouseBinId, ReasonCode: AdjustmentReasons.Damage));
        Assert.Equal(SerialStatuses.Scrapped, w.F.StatusCodeOf((await w.F.SerialAsync(w.PS.ProductId, "S-9")).StatusCodeId!.Value));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(Receipt(w, w.PS, w.B1, 1m, "S-9")));
        Assert.Equal(SerialRules.Scrapped("S-9"), ex.Message);
    }

    [Fact]
    public async Task Reserved_stock_only_leaves_with_from_reserved()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 5m));

        var over = await Assert.ThrowsAsync<InsufficientStockException>(() =>
            w.F.ReserveAsync(new StockReservation(w.PN.ProductId, w.W.WarehouseId, w.B1.WarehouseBinId, null, 6m)));
        Assert.Equal("insufficient_stock", over.Code);

        await w.F.ReserveAsync(new StockReservation(w.PN.ProductId, w.W.WarehouseId, w.B1.WarehouseBinId, null, 3m));
        var b = await w.F.BalanceAsync(w.PN.ProductId, w.B1.WarehouseBinId);
        Assert.Equal(2m, InventoryRules.Available(b!.QtyOnHand, b.QtyReserved));

        await Assert.ThrowsAsync<InsufficientStockException>(() => w.F.PostAsync(Issue(w, w.PN, w.B1, 3m)));
        await w.F.PostAsync(Issue(w, w.PN, w.B1, 3m, fromReserved: true));
        b = await w.F.BalanceAsync(w.PN.ProductId, w.B1.WarehouseBinId);
        Assert.Equal((2m, 0m), (b!.QtyOnHand, b.QtyReserved));

        var release = await Assert.ThrowsAsync<ConflictException>(() =>
            w.F.ReleaseAsync(new StockReservation(w.PN.ProductId, w.W.WarehouseId, w.B1.WarehouseBinId, null, 1m)));
        Assert.Equal(InventoryRules.ReleaseExceedsReserved, release.Message);
    }

    [Fact]
    public async Task Serial_reservation_marks_reserved_and_only_leaves_with_from_reserved()
    {
        // Maestro L328: las series tienen estatus disponible/reservado/despachado.
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PS, w.B1, 1m, "R-1"), Receipt(w, w.PS, w.B1, 1m, "R-2"));
        StockReservation Res(decimal qty, params string[]? serials)
            => new(w.PS.ProductId, w.W.WarehouseId, w.B1.WarehouseBinId, null, qty, serials);
        string StatusOf(InventorySerial s) => w.F.StatusCodeOf(s.StatusCodeId!.Value);

        // Producto con serie: la reserva nombra tantas series como la cantidad (400).
        var noSerials = await Assert.ThrowsAsync<ValidationException>(() => w.F.ReserveAsync(Res(1m, null)));
        Assert.Contains(InventoryRules.SerialCount, noSerials.Errors!.Values.SelectMany(v => v));
        // Una serie que no está en la posición → 409.
        var missing = await Assert.ThrowsAsync<ConflictException>(() => w.F.ReserveAsync(Res(1m, "R-9")));
        Assert.Equal(SerialRules.NotAvailable("R-9", "A01"), missing.Message);

        await w.F.ReserveAsync(Res(1m, "R-1"));
        Assert.Equal(SerialStatuses.Reserved, StatusOf(await w.F.SerialAsync(w.PS.ProductId, "R-1")));
        Assert.Equal(SerialStatuses.Available, StatusOf(await w.F.SerialAsync(w.PS.ProductId, "R-2")));
        var bal = await w.F.BalanceAsync(w.PS.ProductId, w.B1.WarehouseBinId);
        Assert.Equal((2m, 1m), (bal!.QtyOnHand, bal.QtyReserved));

        // La serie reservada no sale sin FromReserved (ni se reserva dos veces); la disponible no sale con FromReserved.
        var plain = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(Issue(w, w.PS, w.B1, 1m, "R-1")));
        Assert.Equal(SerialRules.NotAvailable("R-1", "A01"), plain.Message);
        await Assert.ThrowsAsync<ConflictException>(() => w.F.ReserveAsync(Res(1m, "R-1")));
        var wrong = await Assert.ThrowsAsync<ConflictException>(() => w.F.PostAsync(Issue(w, w.PS, w.B1, 1m, "R-2", fromReserved: true)));
        Assert.Equal(InventoryLedger.SerialNotReserved("R-2", "A01"), wrong.Message);

        // Liberar la devuelve a AVAILABLE; reservarla otra vez y sacarla con FromReserved la deja SHIPPED.
        await w.F.ReleaseAsync(Res(1m, "R-1"));
        Assert.Equal(SerialStatuses.Available, StatusOf(await w.F.SerialAsync(w.PS.ProductId, "R-1")));
        await w.F.ReserveAsync(Res(1m, "R-1"));
        await w.F.PostAsync(Issue(w, w.PS, w.B1, 1m, "R-1", fromReserved: true));
        var shipped = await w.F.SerialAsync(w.PS.ProductId, "R-1");
        Assert.Equal(SerialStatuses.Shipped, StatusOf(shipped));
        Assert.Null(shipped.CurrentBinId);
        bal = await w.F.BalanceAsync(w.PS.ProductId, w.B1.WarehouseBinId);
        Assert.Equal((1m, 0m), (bal!.QtyOnHand, bal.QtyReserved));

        // Producto sin serie: nombrar series → 400.
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 2m));
        var notAllowed = await Assert.ThrowsAsync<ValidationException>(() =>
            w.F.ReserveAsync(new StockReservation(w.PN.ProductId, w.W.WarehouseId, w.B1.WarehouseBinId, null, 1m, new[] { "X" })));
        Assert.Contains(InventoryRules.SerialNotAllowed, notAllowed.Errors!.Values.SelectMany(v => v));
    }

    [Fact]
    public async Task Reconcile_finds_no_mismatch_and_then_a_forced_one()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, w.PN, w.B1, 10m));
        await w.F.PostAsync(Issue(w, w.PN, w.B1, 3m));
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.PN.ProductId, 2m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B2.WarehouseBinId));
        Assert.Empty(await w.F.Ledger.ReconcileAsync(null, default));

        // Descuadre forzado (escritura directa en la prueba, nunca en src/).
        var b = await w.F.Db.StockBalances.SingleAsync(x => x.ProductId == w.PN.ProductId && x.WarehouseBinId == w.B2.WarehouseBinId);
        b.QtyOnHand = 5m;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var rows = await w.F.Ledger.ReconcileAsync(w.PN.ProductId, default);
        Assert.Contains(rows, r => !r.ProductTotal && r.Key.BinId == w.B2.WarehouseBinId && r.LedgerQty == 2m && r.BalanceQty == 5m);
        Assert.Contains(rows, r => r.ProductTotal && r.LedgerQty == 7m && r.BalanceQty == 10m);
    }

    [Fact]
    public async Task Ids_come_back_in_input_order()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var ids = await w.F.PostAsync(Receipt(w, w.PN, w.B2, 1m), Receipt(w, w.PN, w.B1, 2m), Receipt(w, w.PN, w.B2, 3m));
        var txns = await w.F.TransactionsAsync();
        Assert.Equal(new[] { 1m, 2m, 3m }, ids.Select(id => txns.Single(t => t.InventoryTransactionId == id).Quantity));
        Assert.Equal(4m, await w.F.OnHandAsync(w.PN.ProductId, w.B2.WarehouseBinId));
    }

    [Fact]
    public async Task Invalid_postings_are_400_before_touching_anything()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.F.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, w.PN.ProductId, 0m, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B1.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Issue, w.PN.ProductId, 1m, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B1.WarehouseBinId)));
        Assert.Contains(InventoryRules.QuantityMustBePositive, ex.Errors!["postings[0]"]);
        Assert.Contains(InventoryRules.DirectionInvalid(InventoryTxnTypes.Issue), ex.Errors!["postings[1]"]);
        Assert.Empty(await w.F.TransactionsAsync());
        Assert.Empty(await w.F.Db.StockBalances.AsNoTracking().ToListAsync());
    }
}

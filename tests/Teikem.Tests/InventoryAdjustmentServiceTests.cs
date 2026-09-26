using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P3 — ajuste manual y transferencias en InMemory con el InventoryLedger REAL (única vía de escritura): el ajuste
/// ± queda en el Kárdex con la cantidad CON signo (D3) y su motivo; una salida por encima del disponible es 409
/// insufficient_stock sin cambios; una entrada con lote nuevo lo asegura (EnsureLot; mismas fechas o 409); la transferencia
/// entre almacenes es UNA fila TRANSFER (D40) y no toma lo reservado.
/// </summary>
public class InventoryAdjustmentServiceTests
{
    private sealed record World(InventoryServiceFixture F, Warehouse W1, WarehouseBin B1, WarehouseBin B2, Warehouse W2, WarehouseBin C1,
        Product PN, Product PL);

    private static async Task<World> SeedAsync()
    {
        var f = await InventoryServiceFixture.CreateAsync();
        var w1 = await f.AddWarehouseAsync("ALM-01");
        var z1 = await f.AddZoneAsync(w1, "PCK", "PICKING");
        var b1 = await f.AddBinAsync(z1, "A01-R01-N1-P01");
        var b2 = await f.AddBinAsync(z1, "A01-R01-N1-P02");
        var w2 = await f.AddWarehouseAsync("ALM-02");
        var z2 = await f.AddZoneAsync(w2, "RSV", "RESERVE");
        var c1 = await f.AddBinAsync(z2, "B01-R01-N1-P01");
        var pn = await f.AddProductAsync("PN", "NONE", cost: 12.3456m);
        var pl = await f.AddProductAsync("PL", "LOT");
        return new World(f, w1, b1, b2, w2, c1, pn, pl);
    }

    private static AdjustmentRequest Adjust(World w, decimal qty, string reason, WarehouseBin? bin = null, Product? product = null,
        LotInput? lot = null, int? lotId = null)
        => new(product?.PublicId ?? w.PN.PublicId, (bin ?? w.B1).WarehouseId == w.W2.WarehouseId ? w.W2.PublicId : w.W1.PublicId,
            (bin ?? w.B1).WarehouseBinId, qty, reason, "prueba", lotId, lot);

    private static async Task<decimal> OnHandAsync(World w, Product p, WarehouseBin b, int? lotId = null)
    {
        w.F.Db.ChangeTracker.Clear();
        return await w.F.Db.Set<StockBalance>().AsNoTracking()
            .Where(x => x.ProductId == p.ProductId && x.WarehouseBinId == b.WarehouseBinId && x.LotId == lotId)
            .SumAsync(x => x.QtyOnHand);
    }

    private static Task<int> TxnCountAsync(World w) => w.F.Db.Set<InventoryTransaction>().AsNoTracking().CountAsync();

    [Fact]
    public async Task Adjust_plus_and_minus_are_signed_in_the_ledger()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();

        var plus = await svc.AdjustAsync(Adjust(w, 3m, "found"), default);
        var t1 = Assert.Single(plus.Transactions);
        Assert.Equal("ADJUSTMENT", t1.TypeCode);
        Assert.Equal(3m, t1.Quantity);
        Assert.Equal("FOUND", t1.ReasonCode);
        Assert.Equal("ALM-01", t1.ToWarehouseCode);
        Assert.Equal("prueba", t1.Notes);
        Assert.Equal(3m, Assert.Single(plus.Balances).QtyOnHand);
        Assert.Equal(3m, await OnHandAsync(w, w.PN, w.B1));

        var minus = await svc.AdjustAsync(Adjust(w, -2m, "DAMAGE"), default);
        var t2 = Assert.Single(minus.Transactions);
        Assert.Equal(-2m, t2.Quantity);
        Assert.Equal("DAMAGE", t2.ReasonCode);
        Assert.Equal("ALM-01", t2.FromWarehouseCode);
        Assert.Null(t2.ToWarehouseCode);
        Assert.Equal(1m, await OnHandAsync(w, w.PN, w.B1));
        Assert.Equal(1m, Assert.Single(minus.Balances).QtyOnHand);

        // Kárdex: la cantidad del ledger con signo, sin reinterpretar.
        var kardex = await w.F.Get<InventoryReadService>().KardexAsync(new KardexQuery(), InventoryScope.Any, default);
        Assert.Equal(new[] { -2m, 3m }, kardex.Items.Select(r => r.Quantity));
    }

    [Fact]
    public async Task Adjust_exit_above_available_is_409_without_changes()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        await svc.AdjustAsync(Adjust(w, 1m, "FOUND"), default);
        var before = await TxnCountAsync(w);

        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.AdjustAsync(Adjust(w, -1000m, "LOSS"), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("insufficient_stock", ex.Code);

        Assert.Equal(before, await TxnCountAsync(w));
        Assert.Equal(1m, await OnHandAsync(w, w.PN, w.B1));
    }

    [Fact]
    public async Task Adjust_validations_zero_reason_and_system_reason()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();

        var zero = await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(Adjust(w, 0m, "FOUND"), default));
        Assert.Contains("La cantidad del ajuste no puede ser cero.", zero.Errors!["quantity"]);
        var noReason = await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(Adjust(w, 1m, " "), default));
        Assert.Contains("Indique el motivo del ajuste.", noReason.Errors!["reason"]);
        var system = await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(Adjust(w, 1m, "RECEIPT_VARIANCE"), default));
        Assert.Contains("El motivo RECEIPT_VARIANCE lo asigna el sistema.", system.Errors!["reason"]);
        Assert.Equal(0, await TxnCountAsync(w));
    }

    [Fact]
    public async Task Adjust_bin_of_other_warehouse_is_404()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        // Posición de ALM-02 con el almacén ALM-01: se resuelve dentro del almacén → 404 sin oráculo.
        var req = new AdjustmentRequest(w.PN.PublicId, w.W1.PublicId, w.C1.WarehouseBinId, 1m, "FOUND");
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => svc.AdjustAsync(req, default));
        Assert.Equal("Posición no encontrada.", ex.Message);
    }

    [Fact]
    public async Task Adjust_entry_with_new_lot_ensures_the_lot()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        var expiry = new DateOnly(2027, 1, 31);

        var result = await svc.AdjustAsync(Adjust(w, 5m, "OTHER", product: w.PL, lot: new LotInput("L-NEW", null, expiry)), default);
        var lot = await w.F.Db.Set<InventoryLot>().AsNoTracking().SingleAsync(l => l.ProductId == w.PL.ProductId);
        Assert.Equal("L-NEW", lot.LotNumber);
        Assert.Equal(expiry, lot.ExpiryDate);
        Assert.Equal("L-NEW", Assert.Single(result.Transactions).LotNumber);
        Assert.Equal(5m, await OnHandAsync(w, w.PL, w.B1, lot.LotId));

        // Mismo número y mismas fechas: se reutiliza.
        await svc.AdjustAsync(Adjust(w, 1m, "FOUND", product: w.PL, lot: new LotInput("L-NEW", null, expiry)), default);
        Assert.Equal(1, await w.F.Db.Set<InventoryLot>().AsNoTracking().CountAsync(l => l.ProductId == w.PL.ProductId));
        Assert.Equal(6m, await OnHandAsync(w, w.PL, w.B1, lot.LotId));

        // Mismo número con otra fecha: 409 (D34).
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            svc.AdjustAsync(Adjust(w, 1m, "FOUND", product: w.PL, lot: new LotInput("L-NEW", null, expiry.AddDays(1))), default));
        Assert.Equal("El lote L-NEW ya existe con otras fechas; corrija las fechas o use otro número de lote.", ex.Message);

        // Producto con lote sin lote: 400.
        var noLot = await Assert.ThrowsAsync<ValidationException>(() => svc.AdjustAsync(Adjust(w, 1m, "FOUND", product: w.PL), default));
        Assert.Equal("El producto PL se controla por lote; indique el lote.", noLot.Message);
    }

    [Fact]
    public async Task Transfer_between_warehouses_is_one_transfer_row()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        await svc.AdjustAsync(Adjust(w, 4m, "FOUND"), default);
        var before = await TxnCountAsync(w);

        var result = await svc.TransferAsync(new TransferRequest(w.PN.PublicId, w.B1.WarehouseBinId, w.C1.WarehouseBinId, 1m,
            w.W1.PublicId, w.W2.PublicId, Notes: "a reserva"), default);

        Assert.Equal(before + 1, await TxnCountAsync(w));
        var t = Assert.Single(result.Transactions);
        Assert.Equal("TRANSFER", t.TypeCode);
        Assert.Equal(1m, t.Quantity);
        Assert.Equal("ALM-01", t.FromWarehouseCode);
        Assert.Equal("ALM-02", t.ToWarehouseCode);
        Assert.Equal(0m, t.SignedQuantity);   // sin filtro, una transferencia no cambia la existencia total
        Assert.Equal(3m, await OnHandAsync(w, w.PN, w.B1));
        Assert.Equal(1m, await OnHandAsync(w, w.PN, w.C1));
        Assert.Equal(2, result.Balances.Count);

        // El ledger cuadra con los saldos.
        Assert.Empty((await w.F.Get<TraceabilityService>().ReconcileAsync(null, default)).Mismatches);
    }

    [Fact]
    public async Task Transfer_does_not_take_reserved_stock()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        await svc.AdjustAsync(Adjust(w, 2m, "FOUND"), default);

        // Reserva de cross-dock sembrada directamente: en mano 2, reservado 2 → disponible 0.
        w.F.Db.ChangeTracker.Clear();
        var balance = await w.F.Db.Set<StockBalance>().SingleAsync(b => b.ProductId == w.PN.ProductId && b.WarehouseBinId == w.B1.WarehouseBinId);
        balance.QtyReserved = 2m;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var before = await TxnCountAsync(w);

        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.TransferAsync(
            new TransferRequest(w.PN.PublicId, w.B1.WarehouseBinId, w.B2.WarehouseBinId, 1m, w.W1.PublicId), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(before, await TxnCountAsync(w));
        Assert.Equal(2m, await OnHandAsync(w, w.PN, w.B1));
    }

    [Fact]
    public async Task Transfer_to_same_bin_is_400()
    {
        var w = await SeedAsync();
        var svc = w.F.Get<InventoryAdjustmentService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.TransferAsync(
            new TransferRequest(w.PN.PublicId, w.B1.WarehouseBinId, w.B1.WarehouseBinId, 1m, w.W1.PublicId), default));
        Assert.Contains("La posición de origen y la de destino son la misma.", ex.Errors!["toBinId"]);
    }
}

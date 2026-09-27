using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 — las fuentes de análisis WMS se EJECUTAN (LoadAsync sobre InMemory con el ledger real de WmsFixture), no solo se
/// validan sus campos: valorización de 'Valor de inventario a costo/venta' (maestro L889), IsBelowMin detrás de 'Inventario bajo
/// mínimo' y 'Productos bajo mínimo' (L874), y la cantidad CON signo que suma la vista agrupada 'Movimientos por tipo y
/// producto' (L887) y lista 'Ajustes de inventario' (L327). Todas bajo el filtro de tenant.
/// </summary>
public sealed class WmsDataSourceTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Bin, WarehouseBin Bin2, Product Pn, Product Inactive, Product Zero);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var bin = await f.AddBinAsync(zone, "P-01");
        var bin2 = await f.AddBinAsync(zone, "P-02");
        var pn = await f.AddProductAsync("PN", purchaseCost: 2.5m);
        var inactive = await f.AddProductAsync("PX", isActive: false);
        var zero = await f.AddProductAsync("PZ", purchaseCost: 1m);
        foreach (var (id, min, price) in new[] { (pn.ProductId, 50m, (decimal?)4m), (inactive.ProductId, 50m, null) })
        {
            var p = await f.Db.Products.SingleAsync(x => x.ProductId == id);
            p.MinQty = min;
            p.SalePrice = price;
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        // PN: +10, −4, TRANSFER 1 de P-01 a P-02, ajuste −1 DAMAGE → 5 en mano (4 + 1). PZ: +1 y −1 → saldo en cero.
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, pn.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, pn.ProductId, 4m, FromWarehouseId: w.WarehouseId, FromBinId: bin.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, pn.ProductId, 1m, FromWarehouseId: w.WarehouseId, FromBinId: bin.WarehouseBinId,
            ToWarehouseId: w.WarehouseId, ToBinId: bin2.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, pn.ProductId, 1m, FromWarehouseId: w.WarehouseId, FromBinId: bin.WarehouseBinId,
            ReasonCode: AdjustmentReasons.Damage));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, zero.ProductId, 1m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, zero.ProductId, 1m, FromWarehouseId: w.WarehouseId, FromBinId: bin.WarehouseBinId));

        // Otro tenant con inventario propio: ninguna fuente lo ve.
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            var w2 = await f.AddWarehouseAsync("W1");
            var b2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-01");
            var other = await f.AddProductAsync("PN", purchaseCost: 100m);
            await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, other.ProductId, 7m, ToWarehouseId: w2.WarehouseId, ToBinId: b2.WarehouseBinId));
        }
        return new World(f, w, bin, bin2, pn, inactive, zero);
    }

    [Fact]
    public async Task Stock_balance_source_values_on_hand_at_cost_and_sale_and_skips_zero_and_other_tenants()
    {
        var x = await SeedAsync();
        await using var f = x.F;
        var rows = await new StockBalanceDataSource(f.Db, new InventoryReadService(f.Db, f.Tenant, f.Lookups)).LoadAsync(new DataQuery(), default);

        Assert.All(rows, r => Assert.Equal(x.Pn.ProductId, r["ProductId"]));   // sin PZ (en cero) ni el otro tenant
        Assert.Equal(2, rows.Count);
        var p01 = rows.Single(r => (string?)r["BinCode"] == "P-01");
        Assert.Equal(4m, p01["QtyOnHand"]);
        Assert.Equal(10m, p01["CostValue"]);        // 4 × 2.5
        Assert.Equal(16m, p01["SaleValue"]);        // 4 × 4
        // 'Valor de inventario a costo' = Σ CostValue de los saldos = 5 × 2.5.
        Assert.Equal(12.5m, rows.Sum(r => (decimal)r["CostValue"]!));
        Assert.Equal(20m, rows.Sum(r => (decimal)r["SaleValue"]!));
    }

    [Fact]
    public async Task Product_source_flags_below_min_only_for_active_products()
    {
        var x = await SeedAsync();
        await using var f = x.F;
        var rows = (await new ProductDataSource(f.Db, f.Lookups).LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);

        Assert.Equal(3, rows.Count);                            // el producto del otro tenant no aparece
        Assert.Equal(true, rows[x.Pn.ProductId]["IsBelowMin"]); // disponible 5 < mínimo 50
        Assert.Equal(5m, rows[x.Pn.ProductId]["QtyAvailable"]);
        Assert.Equal(12.5m, rows[x.Pn.ProductId]["CostValue"]);
        Assert.Equal(false, rows[x.Inactive.ProductId]["IsBelowMin"]); // inactivo: fuera de 'bajo mínimo'
        Assert.Equal(false, rows[x.Zero.ProductId]["IsBelowMin"]);     // sin mínimo
    }

    [Fact]
    public async Task Transaction_source_keeps_the_ledger_sign_and_transfer_is_neutral()
    {
        var x = await SeedAsync();
        await using var f = x.F;
        var rows = await new InventoryTransactionDataSource(f.Db, new InventoryReadService(f.Db, f.Tenant, f.Lookups)).LoadAsync(new DataQuery(), default);
        var pn = rows.Where(r => (int)r["ProductId"]! == x.Pn.ProductId).ToList();

        Assert.Equal(6, rows.Count);   // 4 de PN + 2 de PZ; nada del otro tenant
        Assert.Equal(10m, pn.Single(r => (string?)r["TxnTypeCode"] == InventoryTxnTypes.Receipt)["Quantity"]);
        Assert.Equal(-4m, pn.Single(r => (string?)r["TxnTypeCode"] == InventoryTxnTypes.Issue)["Quantity"]);
        var transfer = pn.Single(r => (string?)r["TxnTypeCode"] == InventoryTxnTypes.Transfer);
        Assert.Equal(1m, transfer["Quantity"]);
        Assert.Equal(0m, transfer["SignedQuantity"]);
        var adjustment = pn.Single(r => (string?)r["TxnTypeCode"] == InventoryTxnTypes.Adjustment);
        Assert.Equal(-1m, adjustment["Quantity"]);
        Assert.Equal(AdjustmentReasons.Damage, adjustment["ReasonCode"]);
        // La suma CON signo sin TRANSFER (lo que agrega 'Movimientos por tipo y producto') cuadra con el saldo en mano.
        Assert.Equal(5m, pn.Where(r => (string?)r["TxnTypeCode"] != InventoryTxnTypes.Transfer).Sum(r => (decimal)r["Quantity"]!));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Ajuste del dueño del producto (2026-09-30) al Lote 12 — KPI "Unidades totales": GET /products?onlyOnHand=true deja solo
/// los productos con existencia en mano &gt; 0 (Σ QtyOnHand de todas sus posiciones, incluidas cuarentena y lo reservado),
/// acotada a los almacenes indicados si los hay. Con activeOnly = "activos con existencia en mano". A diferencia de
/// onlyAvailable, no excluye zonas ni resta lo reservado. InMemory con WmsFixture; traducción a SQL Server con ToQueryString.
/// </summary>
public sealed class ProductOnHandFilterTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<ProductService>());

    private static InventoryPosting In(int productId, WarehouseBin bin, decimal qty)
        => new(InventoryTxnTypes.Receipt, productId, qty, ToWarehouseId: bin.WarehouseId, ToBinId: bin.WarehouseBinId);

    private static async Task ChangeBalanceAsync(WmsFixture f, Product p, WarehouseBin bin, Action<StockBalance> change)
    {
        f.Db.ChangeTracker.Clear();
        var balance = await f.Db.Set<StockBalance>().SingleAsync(b => b.ProductId == p.ProductId && b.WarehouseBinId == bin.WarehouseBinId);
        change(balance);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Only_on_hand_keeps_products_with_physical_stock_in_the_filtered_warehouses()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var pick = await f.AddBinAsync(await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking), "P-01");
        var quarantine = await f.AddBinAsync(await f.AddZoneAsync(w1, "CUA", ZoneTypes.Quarantine), "Q-01");
        var reserve2 = await f.AddBinAsync(await f.AddZoneAsync(w2, "RSV", ZoneTypes.Reserve), "R-01");

        var a = await f.AddProductAsync("A-PICK");        // 5 en picking de W1
        var b = await f.AddProductAsync("B-QUAR");        // 3 solo en cuarentena de W1 (sin disponible recolectable)
        var c = await f.AddProductAsync("C-RESV");        // 2 en W2, todo reservado
        await f.AddProductAsync("D-NONE");                // sin saldo
        var e = await f.AddProductAsync("E-INAC");        // 4 en W1, producto dado de baja después
        var z = await f.AddProductAsync("Z-ZERO");        // saldo que quedó en 0
        await f.PostAsync(In(a.ProductId, pick, 5m), In(b.ProductId, quarantine, 3m), In(c.ProductId, reserve2, 2m),
            In(e.ProductId, pick, 4m), In(z.ProductId, pick, 2m));
        await ChangeBalanceAsync(f, c, reserve2, bal => bal.QtyReserved = 2m);
        await ChangeBalanceAsync(f, z, pick, bal => bal.QtyOnHand = 0m);
        var tracked = await f.Db.Products.AsTracking().SingleAsync(p => p.ProductId == e.ProductId);
        tracked.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var products = f.Get<ProductService>();
        async Task<string[]> Skus(ProductListQuery q) => (await products.ListAsync(q, InventoryScope.Any, default)).Items.Select(i => i.Sku).ToArray();

        Assert.Equal(new[] { "A-PICK", "B-QUAR", "C-RESV", "E-INAC" }, await Skus(new ProductListQuery(OnlyOnHand: true)));
        // El indicador: activos con existencia en mano, contados con take=1 y total.
        Assert.Equal(new[] { "A-PICK", "B-QUAR", "C-RESV" }, await Skus(new ProductListQuery(ActiveOnly: true, OnlyOnHand: true)));
        var kpi = await products.ListAsync(new ProductListQuery(ActiveOnly: true, OnlyOnHand: true, Take: 1), InventoryScope.Any, default);
        Assert.Equal(3, kpi.Total);
        Assert.Single(kpi.Items);

        // Acotado a los almacenes (singular, lista o ambos combinados).
        Assert.Equal(new[] { "C-RESV" }, await Skus(new ProductListQuery(OnlyOnHand: true, WarehousePublicIds: new[] { w2.PublicId })));
        Assert.Equal(new[] { "A-PICK", "B-QUAR", "E-INAC" }, await Skus(new ProductListQuery(OnlyOnHand: true, WarehousePublicId: w1.PublicId)));
        Assert.Equal(new[] { "A-PICK", "B-QUAR", "C-RESV", "E-INAC" },
            await Skus(new ProductListQuery(OnlyOnHand: true, WarehousePublicId: w1.PublicId, WarehousePublicIds: new[] { w2.PublicId })));

        // Diferencia con onlyAvailable: ese excluye cuarentena y lo reservado.
        Assert.Equal(new[] { "A-PICK", "E-INAC" }, await Skus(new ProductListQuery(OnlyAvailable: true)));
        // Sin el filtro, la lista no cambia.
        Assert.Equal(6, (await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default)).Total);
    }

    [Fact]
    public void Only_on_hand_predicate_translates_to_sql_server()
    {
        // Misma forma que ProductService.ListAsync con onlyOnHand y almacenes.
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var whIds = new List<int> { 1, 2 };
        var onHand = db.Set<StockBalance>().AsNoTracking().Where(b => whIds.Contains(b.WarehouseId));
        var sql = db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive)
            .Where(p => onHand.Where(b => b.ProductId == p.ProductId).Sum(b => b.QtyOnHand) > 0)
            .OrderBy(p => p.Sku).Take(1)
            .ToQueryString();
        Assert.Contains("[StockBalance]", sql);
        Assert.Contains("SUM(", sql);
        Assert.Contains("[QtyOnHand]", sql);
    }
}

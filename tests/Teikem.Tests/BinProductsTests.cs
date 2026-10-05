using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Informe "Productos por posición": productos agregados por producto (sin repetir por lote), solo con existencia en mano, por SKU;
/// los mismos filtros del listado de posiciones, paginación con tope de 200, 404 de otro tenant y traducción a SQL Server sin BD.
/// InMemory con el ledger real (WmsFixture).
/// </summary>
public sealed class BinProductsTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone Z, WarehouseBin A, WarehouseBin B, WarehouseBin C, Product X, Product Y,
        Product L) : IAsyncDisposable
    {
        public BinProductsService Service => F.Get<BinProductsService>();
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    /// <summary>Almacén W1, zona PCK con posiciones A-01, A-02 (pasillo A) y A-03 (pasillo B); productos X, Y (sin seguimiento) y L (por lote).</summary>
    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<WarehouseLayoutService>();
            s.AddSingleton<BinProductsService>();
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<InventoryAdjustmentService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var a = await f.AddBinAsync(z, "A-01", aisle: "A", rack: "01");
        var b = await f.AddBinAsync(z, "A-02", aisle: "A", rack: "02");
        var c = await f.AddBinAsync(z, "A-03", aisle: "B", rack: "03");
        var x = await f.AddProductAsync("SKU-X");
        var y = await f.AddProductAsync("SKU-Y");
        var l = await f.AddProductAsync("SKU-L", TrackingTypes.Lot);
        return new World(f, w, z, a, b, c, x, y, l);
    }

    private static async Task RunAsync(Func<World, Task> body)
    {
        await using var x = await WorldAsync();
        await body(x);
    }

    private static InventoryPosting In(World x, Product p, WarehouseBin b, decimal q, int? lotId = null)
        => new(InventoryTxnTypes.Receipt, p.ProductId, q, ToWarehouseId: x.W.WarehouseId, ToBinId: b.WarehouseBinId, LotId: lotId);

    private static InventoryPosting Out(World x, Product p, WarehouseBin b, decimal q, int? lotId = null)
        => new(InventoryTxnTypes.Issue, p.ProductId, q, FromWarehouseId: x.W.WarehouseId, FromBinId: b.WarehouseBinId, LotId: lotId);

    [Fact]
    public void Exact_message_of_the_report()
        => Assert.Equal("Se pueden pedir como máximo 200 posiciones por consulta; use skip para pedir las siguientes.", BinProductsRules.TakeTooLarge);

    [Fact]
    public Task Report_aggregates_products_without_repeating_lots_only_on_hand_ordered_by_sku()
        => RunAsync(async x =>
        {
            var l1 = await x.F.AddLotAsync(x.L, "L1");
            var l2 = await x.F.AddLotAsync(x.L, "L2");
            var z = await x.F.AddProductAsync("SKU-Z");
            await x.F.PostAsync(In(x, z, x.A, 1m), In(x, x.L, x.A, 2m, l1.LotId), In(x, x.L, x.A, 3m, l2.LotId), In(x, x.X, x.A, 1m),
                In(x, x.Y, x.A, 1m));
            await x.F.PostAsync(Out(x, x.Y, x.A, 1m));   // Y salió: no va en el informe
            var bc = await x.F.Db.Products.AsTracking().SingleAsync(p => p.ProductId == x.X.ProductId);
            bc.Barcode = "7501234567890";
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();

            var page = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(BinIds: new[] { x.A.WarehouseBinId })), default);
            var bin = Assert.Single(page.Items);
            Assert.Equal(("A-01", "PCK", "A", "01"), (bin.Code, bin.ZoneCode, bin.Aisle, bin.Rack));
            Assert.Equal(new[] { "SKU-L", "SKU-X", "SKU-Z" }, bin.Products.Select(p => p.Sku));
            var px = bin.Products.Single(p => p.Sku == "SKU-X");
            Assert.Equal((x.X.PublicId, "Producto SKU-X", "7501234567890"), (px.ProductPublicId, px.Name, px.Barcode));
            Assert.Equal(1, page.Total);
            Assert.True(page.GeneratedAtUtc <= DateTime.UtcNow);
        });

    [Fact]
    public Task Empty_bin_has_no_products_and_a_product_that_moved_leaves_the_origin()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 2m));
            await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, x.X.ProductId, 2m, FromWarehouseId: x.W.WarehouseId,
                FromBinId: x.A.WarehouseBinId, ToWarehouseId: x.W.WarehouseId, ToBinId: x.B.WarehouseBinId));
            var page = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(BinIds: new[] { x.A.WarehouseBinId, x.B.WarehouseBinId })), default);
            Assert.Empty(page.Items.Single(i => i.Code == "A-01").Products);
            Assert.Equal("SKU-X", Assert.Single(page.Items.Single(i => i.Code == "A-02").Products).Sku);
        });

    [Fact]
    public Task Report_uses_the_bin_list_filters_paginates_and_rejects_more_than_200()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m), In(x, x.Y, x.C, 1m));

            var all = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery()), default);
            Assert.Equal(new[] { "A-01", "A-02", "A-03" }, all.Items.Select(s => s.Code));
            Assert.Equal((3, 0, 50), (all.Total, all.Skip, all.Take));

            var paged = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(), Skip: 1, Take: 1), default);
            Assert.Equal((3, 1, 1), (paged.Total, paged.Skip, paged.Take));
            Assert.Equal(new[] { "A-02" }, paged.Items.Select(s => s.Code));

            var aisle = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(Aisle: "b")), default);
            Assert.Equal("A-03", Assert.Single(aisle.Items).Code);

            var withStock = await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(OnlyWithStock: true)), default);
            Assert.Equal(new[] { "A-01", "A-03" }, withStock.Items.Select(s => s.Code));

            var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(), Take: 201), default));
            Assert.Equal(BinProductsRules.TakeTooLarge, Assert.Single(tooMany.Errors!["take"]));
            Assert.Equal(200, (await x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(), Take: 200), default)).Take);

            var unknown = await Assert.ThrowsAsync<ValidationException>(() =>
                x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery(Occupancy: new[] { "X" })), default));
            Assert.Contains("occupancy", unknown.Errors!.Keys);
        });

    [Fact]
    public Task Unknown_warehouse_is_404_and_another_tenant_cannot_read_the_report()
        => RunAsync(async x =>
        {
            await x.F.PostAsync(In(x, x.X, x.A, 1m));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Service.ListAsync(Guid.NewGuid(), new BinProductsQuery(new WarehouseBinQuery()), default));
            using (x.F.AsTenant(WmsFixture.OtherTenantId))
            {
                await Assert.ThrowsAsync<NotFoundException>(() => x.Service.ListAsync(x.W.PublicId, new BinProductsQuery(new WarehouseBinQuery()), default));
            }
        });

    [Fact]
    public void Product_pairs_query_translates_to_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var pairs = BinProductsService.ProductPairsQuery(db, 1, new List<int> { 1, 2 }).ToQueryString();
        Assert.Contains("GROUP BY", pairs);
        Assert.Contains("HAVING", pairs);
    }

    [Fact]
    public void Bin_sheet_columns_no_longer_exist_in_the_model_or_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(typeof(WarehouseBin))!;
        Assert.Null(entity.FindProperty("SheetPrintedAtUtc"));
        Assert.Null(entity.FindProperty("SheetContentChangedAtUtc"));
        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.DoesNotContain("SheetPrintedAtUtc", sql);
        Assert.DoesNotContain("SheetContentChangedAtUtc", sql);
    }
}

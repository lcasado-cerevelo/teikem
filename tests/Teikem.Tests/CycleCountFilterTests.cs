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
/// Lote 6 (P6; maestro L553) — los filtros de Inventario se replican en Conteo cíclico: almacén (selección múltiple),
/// posición, producto y categoría (con subcategorías), más Desde/Hasta (Lote 14: días locales de Puerto Rico) en la lista, y en la ficha los de línea
/// (posición, producto, categoría, solo con diferencia y solo pendientes). InMemory con WmsFixture y el ledger real.
/// </summary>
public sealed class CycleCountFilterTests
{
    private sealed record World(WmsFixture F, Warehouse W1, Warehouse W2, WarehouseBin Bin1, WarehouseBin Bin2, Product Pa, Product Pb,
        ProductCategory Root, CycleCountDetailDto Cc1, CycleCountDetailDto Cc2);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<Teikem.Infrastructure.Abstractions.ITenantClock>(Teikem.Infrastructure.Abstractions.TenantClock.Default);   // Lote 14
            s.AddSingleton<CycleCountService>();
        });
        var root = new ProductCategory { TenantId = WmsFixture.TenantId, Name = "Medidores", IsActive = true };
        f.Db.Set<ProductCategory>().Add(root);
        await f.Db.SaveChangesAsync();
        var child = new ProductCategory { TenantId = WmsFixture.TenantId, Name = "Glucosa", ParentId = root.ProductCategoryId, IsActive = true };
        f.Db.Set<ProductCategory>().Add(child);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var w1 = await f.AddWarehouseAsync("W1");
        var z1 = await f.AddZoneAsync(w1, "PCK", ZoneTypes.Picking);
        var bin1 = await f.AddBinAsync(z1, "P-01");
        var bin2 = await f.AddBinAsync(z1, "P-02");
        var w2 = await f.AddWarehouseAsync("W2");
        var bin3 = await f.AddBinAsync(await f.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "P-01");
        var pa = await f.AddProductAsync("SKU-ALFA");
        var pb = await f.AddProductAsync("SKU-BETA");
        (await f.Db.Products.SingleAsync(p => p.ProductId == pa.ProductId)).ProductCategoryId = child.ProductCategoryId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, pa.ProductId, 5m, ToWarehouseId: w1.WarehouseId, ToBinId: bin1.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, pb.ProductId, 3m, ToWarehouseId: w1.WarehouseId, ToBinId: bin2.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, pb.ProductId, 2m, ToWarehouseId: w2.WarehouseId, ToBinId: bin3.WarehouseBinId));

        var svc = f.Get<CycleCountService>();
        var cc1 = await svc.CreateAsync(new CycleCountCreateRequest(w1.PublicId), default);
        var cc2 = await svc.CreateAsync(new CycleCountCreateRequest(w2.PublicId), default);
        // CC2 se dio de alta el 2 de septiembre a las 02:00 UTC = 1 de septiembre a las 22:00 en Puerto Rico (Lote 14: días locales).
        (await f.Db.Set<CycleCount>().SingleAsync(c => c.CycleCountId == cc2.Count.Id)).CreatedAtUtc = new DateTime(2026, 9, 2, 2, 0, 0, DateTimeKind.Utc);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w1, w2, bin1, bin2, pa, pb, root, cc1, cc2);
    }

    private static async Task<string[]> ListAsync(World w, CycleCountQuery q)
        => (await w.F.Get<CycleCountService>().ListAsync(q, default)).Select(c => c.Number).OrderBy(n => n).ToArray();

    [Fact]
    public async Task List_filters_by_warehouses_bins_products_categories_and_dates()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var (n1, n2) = (w.Cc1.Count.Number, w.Cc2.Count.Number);

        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(WarehousePublicIds: new[] { w.W1.PublicId })));
        Assert.Equal(new[] { n1, n2 }, await ListAsync(w, new CycleCountQuery(WarehousePublicIds: new[] { w.W1.PublicId, w.W2.PublicId })));
        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(BinIds: new[] { w.Bin2.WarehouseBinId })));
        Assert.Equal(new[] { n1, n2 }, await ListAsync(w, new CycleCountQuery(ProductPublicIds: new[] { w.Pb.PublicId })));
        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(ProductPublicIds: new[] { w.Pa.PublicId })));
        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(CategoryIds: new[] { w.Root.ProductCategoryId })));   // subcategoría

        var sep1 = new DateOnly(2026, 9, 1);
        Assert.Equal(new[] { n2 }, await ListAsync(w, new CycleCountQuery(From: sep1, To: sep1)));
        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(From: sep1.AddDays(1))));
        Assert.Equal(new[] { n1 }, await ListAsync(w, new CycleCountQuery(Status: new[] { "open" }, Search: "ALFA")));
    }

    [Fact]
    public async Task Detail_filters_lines()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var svc = w.F.Get<CycleCountService>();
        var id = w.Cc1.Count.Id;
        Assert.Equal(2, w.Cc1.Lines.Count);

        Assert.Equal("SKU-ALFA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(BinIds: new[] { w.Bin1.WarehouseBinId }), default)).Lines).Sku);
        Assert.Equal("SKU-BETA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(ProductPublicIds: new[] { w.Pb.PublicId }), default)).Lines).Sku);
        Assert.Equal("SKU-ALFA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(CategoryIds: new[] { w.Root.ProductCategoryId }), default)).Lines).Sku);

        // Se cuenta ALFA con diferencia (4 de 5); BETA queda pendiente.
        var alfa = w.Cc1.Lines.Single(l => l.Sku == "SKU-ALFA");
        await svc.CaptureAsync(id, new CountCaptureRequest(new[] { new CountCaptureItem(alfa.Id, 4m) }), default);
        Assert.Equal("SKU-ALFA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(OnlyVariance: true), default)).Lines).Sku);
        Assert.Equal("SKU-BETA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(OnlyPending: true), default)).Lines).Sku);

        // A ciegas, onlyVariance se ignora: filtrar por diferencia revelaría qué líneas difieren de lo esperado.
        var ciego = await svc.GetAsync(id, new CycleCountLinesQuery(OnlyVariance: true), blind: true, default);
        Assert.True(ciego.IsBlind);
        Assert.Equal(2, ciego.Lines.Count);
        Assert.All(ciego.Lines, l => Assert.Null(l.SystemQty));
        Assert.All(ciego.Lines, l => Assert.Null(l.VarianceQty));
        // Los demás filtros sí aplican a ciegas.
        Assert.Equal("SKU-BETA", Assert.Single((await svc.GetAsync(id, new CycleCountLinesQuery(OnlyPending: true), blind: true, default)).Lines).Sku);
    }
}

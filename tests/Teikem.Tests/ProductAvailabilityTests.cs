using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P2/P3; R37, bitácora L781) — el filtro OnlyAvailable del selector de recolección y de los saldos descuenta lo
/// reservado y no cuenta las zonas QUARANTINE ni CROSSDOCK (InMemory, WmsFixture). La traducción a SQL la cubre el smoke.
/// </summary>
public sealed class ProductAvailabilityTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ProductService>();
        s.AddSingleton<Teikem.Infrastructure.Abstractions.ITenantClock>(Teikem.Infrastructure.Abstractions.TenantClock.Default);   // Lote 14
        s.AddSingleton<InventoryReconciler>();   // Lote 14: TraceabilityService delega la conciliación
        s.AddSingleton<InventoryReadService>();
    });

    [Fact]
    public async Task Only_available_discounts_reserved_and_ignores_quarantine_and_cross_dock()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var pck = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var qua = await f.AddBinAsync(await f.AddZoneAsync(w, "QUA", ZoneTypes.Quarantine), "Q-01");
        var xd = await f.AddBinAsync(await f.AddZoneAsync(w, "XD", ZoneTypes.CrossDock), "XD-01");
        var reserved = await f.AddProductAsync("PR");
        var quarantined = await f.AddProductAsync("PQ");
        var free = await f.AddProductAsync("PF");
        InventoryPosting In(int productId, int binId, decimal qty)
            => new(InventoryTxnTypes.Receipt, productId, qty, ToWarehouseId: w.WarehouseId, ToBinId: binId);
        await f.PostAsync(In(reserved.ProductId, pck.WarehouseBinId, 3m), In(quarantined.ProductId, qua.WarehouseBinId, 5m),
            In(quarantined.ProductId, xd.WarehouseBinId, 2m), In(free.ProductId, pck.WarehouseBinId, 1m));
        await f.ReserveAsync(new StockReservation(reserved.ProductId, w.WarehouseId, pck.WarehouseBinId, null, 3m));

        var products = f.Get<ProductService>();
        var available = await products.ListAsync(new ProductListQuery(WarehousePublicId: w.PublicId, OnlyAvailable: true), InventoryScope.Any, default);
        Assert.Equal(new[] { "PF" }, available.Items.Select(i => i.Sku).ToArray());
        var all = await products.ListAsync(new ProductListQuery(WarehousePublicId: w.PublicId), InventoryScope.Any, default);
        Assert.Equal(3, all.Total);

        // «Excluir no disponibles» (HasAvailable): disponible = en mano − reservado de TODAS las posiciones (la columna de la tabla): cuarentena cuenta, lo reservado no
        var hasAvailable = await products.ListAsync(new ProductListQuery(WarehousePublicId: w.PublicId, HasAvailable: true), InventoryScope.Any, default);
        Assert.Equal(new[] { "PF", "PQ" }, hasAvailable.Items.Select(i => i.Sku).OrderBy(x => x).ToArray());

        // filtro «Posición»: el código de la posición CONTIENE el texto (y la posición tiene existencia de ese producto)
        var inA = await products.ListAsync(new ProductListQuery(BinSearch: "P-0"), InventoryScope.Any, default);
        Assert.Equal(new[] { "PF", "PR" }, inA.Items.Select(i => i.Sku).OrderBy(x => x).ToArray());
        var inQ = await products.ListAsync(new ProductListQuery(BinSearch: " q-0 "), InventoryScope.Any, default);
        Assert.Equal(new[] { "PQ" }, inQ.Items.Select(i => i.Sku).ToArray());
        // las cantidades de la lista son las de ESAS posiciones: PQ tiene 5 en Q-01 y 2 en XD-01
        Assert.Equal(5m, inQ.Items.Single().QtyOnHand);
        var inXd = await products.ListAsync(new ProductListQuery(BinSearch: "xd"), InventoryScope.Any, default);
        Assert.Equal(2m, inXd.Items.Single().QtyOnHand);
        Assert.Empty((await products.ListAsync(new ProductListQuery(BinSearch: "ZZ"), InventoryScope.Any, default)).Items);

        var balances = await f.Get<InventoryReadService>().BalancesAsync(
            new BalanceQuery(ProductPublicIds: new[] { reserved.PublicId }, OnlyAvailable: true), InventoryScope.Any, default);
        Assert.Empty(balances.Items);

        // Al liberar 1, vuelve a aparecer con disponible 1.
        await f.ReleaseAsync(new StockReservation(reserved.ProductId, w.WarehouseId, pck.WarehouseBinId, null, 1m));
        var again = await products.ListAsync(new ProductListQuery(WarehousePublicId: w.PublicId, OnlyAvailable: true), InventoryScope.Any, default);
        Assert.Contains(again.Items, i => i.Sku == "PR" && i.QtyAvailable == 1m);
    }
}

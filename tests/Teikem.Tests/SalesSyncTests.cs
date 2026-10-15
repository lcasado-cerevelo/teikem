using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 64 (P4, S2): las órdenes de venta por despachar para el aparato (GET /sync/sales-orders) con de qué posición y lote reservó el sistema cada parte.
public sealed class SalesSyncTests
{
    private static async Task<(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client C, SalesOrderService Svc, WarehouseBin B1, WarehouseBin B2, Product P)> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<SyncService>();
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var zone = await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking);
        var b1 = await f.AddBinAsync(zone, "P-01");
        var b2 = await f.AddBinAsync(zone, "P-02");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        foreach (var (bin, qty) in new[] { (b1, 4m), (b2, 10m) })
            f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = qty, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, w, c, f.Get<SalesOrderService>(), b1, b2, p);
    }

    [Fact]
    public async Task Open_orders_come_with_where_each_part_is_reserved_and_closed_ones_come_as_inactive()
    {
        var (f, w, c, svc, b1, b2, p) = await WorldAsync();
        await using var _ = f;
        var draft = await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 6m) }), default);
        var requested = await svc.RequestAsync(draft.PublicId, null, default);   // 4 en P-01 y 2 en P-02
        var notRequested = await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 1m) }), default);
        var sync = f.Get<SyncService>();

        var page = await sync.SalesOrdersAsync(new SyncQuery(WarehousePublicId: w.PublicId), default);
        var order = Assert.Single(page.Items);   // el borrador no sale
        Assert.Equal((requested.Number, "REQUESTED", true), (order.Number, order.StatusCode, order.IsActive));
        Assert.Equal(w.PublicId, order.WarehousePublicId);
        Assert.Equal("Cliente C1", order.ClientName);
        var line = Assert.Single(order.Lines);
        Assert.Equal(("MED", 6m, 6m, 0m), (line.Sku, line.QtyOrdered, line.QtyReserved, line.QtyShipped));
        Assert.Equal(new[] { ("P-01", 4m), ("P-02", 2m) }, line.Reservations.Select(r => (r.BinCode, r.Quantity)).ToArray());
        Assert.Equal(new[] { b1.WarehouseBinId, b2.WarehouseBinId }, line.Reservations.Select(r => r.BinId).ToArray());

        // despachada: con since llega como no activa (el aparato la borra); las abiertas siguen llegando siempre
        await svc.DispatchAsync(requested.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(requested.Lines[0].Id, 6m) }), default);
        var req2 = await svc.RequestAsync(notRequested.PublicId, null, default);
        var delta = await sync.SalesOrdersAsync(new SyncQuery(Since: DateTime.UtcNow.AddMinutes(-5), WarehousePublicId: w.PublicId), default);
        Assert.Equal((false, "SHIPPED"), (delta.Items.Single(o => o.Number == requested.Number).IsActive, delta.Items.Single(o => o.Number == requested.Number).StatusCode));
        Assert.True(delta.Items.Single(o => o.Number == req2.Number).IsActive);
        // la carga completa (sin since) solo trae las abiertas
        Assert.Equal(new[] { req2.Number }, (await sync.SalesOrdersAsync(new SyncQuery(WarehousePublicId: w.PublicId), default)).Items.Select(o => o.Number).ToArray());
    }

    [Fact]
    public async Task Another_company_gets_nothing_and_the_endpoint_is_protected_by_module_and_permission()
    {
        var (f, w, c, svc, _, _, p) = await WorldAsync();
        await using var _f = f;
        var d = await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 1m) }), default);
        await svc.RequestAsync(d.PublicId, null, default);
        using (f.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty((await f.Get<SyncService>().SalesOrdersAsync(new SyncQuery(), default)).Items);

        var m = typeof(SyncController).GetMethod(nameof(SyncController.SalesOrders))!;
        Assert.Contains(ModuleKeys.SalesOrders, m.GetCustomAttributes<RequireModuleAttribute>().Select(a => a.ModuleKey));
        Assert.Contains(RequirePermissionAttribute.Prefix + PermissionCatalog.SalesView, m.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy));
    }
}

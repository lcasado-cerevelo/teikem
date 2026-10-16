using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 67 (P4, S4): análisis de órdenes de venta — reglas puras, las dos fuentes de datos, el contenido de sistema (los nombres de campo existen) y el aviso del Pulso.
public sealed class SalesAnalyticsTests
{
    [Theory]
    [InlineData(10, 10, 100)]
    [InlineData(10, 7, 70)]
    [InlineData(3, 1, 33.33)]
    [InlineData(10, 12, 100)]   // no pasa de 100
    [InlineData(0, 0, 0)]
    public void Fill_rate_is_shipped_over_ordered_capped_at_100(double ordered, double shipped, double expected)
        => Assert.Equal((decimal)expected, SalesAnalyticsRules.FillRatePct((decimal)ordered, (decimal)shipped));

    [Fact]
    public void Days_to_ship_needs_both_dates_and_waiting_days_only_count_for_a_pending_backorder()
    {
        var a = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1.5m, SalesAnalyticsRules.DaysToShip(a, a.AddHours(36)));
        Assert.Null(SalesAnalyticsRules.DaysToShip(null, a));
        Assert.Null(SalesAnalyticsRules.DaysToShip(a, null));
        Assert.Null(SalesAnalyticsRules.DaysToShip(a, a.AddDays(-1)));
        Assert.Equal(3, SalesAnalyticsRules.DaysWaiting(2m, false, a, a.AddDays(3).AddHours(5)));
        Assert.Equal(0, SalesAnalyticsRules.DaysWaiting(0m, false, a, a.AddDays(3)));
        Assert.Equal(0, SalesAnalyticsRules.DaysWaiting(2m, true, a, a.AddDays(3)));
        Assert.True(SalesAnalyticsRules.IsLate(SalesOrderStatuses.Requested, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)));
        Assert.False(SalesAnalyticsRules.IsLate(SalesOrderStatuses.Shipped, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)));
        Assert.False(SalesAnalyticsRules.IsLate(SalesOrderStatuses.Requested, null, new DateOnly(2026, 10, 2)));
    }

    private static async Task<(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client C, SalesOrderService Svc, WarehouseBin B1, Product P)> WorldAsync(decimal onHand)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var b1 = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = b1.WarehouseBinId, QtyOnHand = onHand, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, w, c, f.Get<SalesOrderService>(), b1, p);
    }

    private static SalesOrderDataSource OrderSource(WmsFixture f) => new(f.Db, f.Lookups, f.Tenant);
    private static SalesOrderLineDataSource LineSource(WmsFixture f) => new(f.Db, f.Tenant);

    [Fact]
    public async Task Order_rows_carry_status_units_backorder_fill_rate_and_days_to_ship()
    {
        var (f, w, c, svc, _, p) = await WorldAsync(onHand: 10m);
        await using var _f = f;
        // A: pide 4, queda reservada y se despacha completa → Despachada, cumplimiento 100
        var a = await svc.RequestAsync((await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 4m) }), default)).PublicId, null, default);
        await svc.DispatchAsync(a.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(a.Lines[0].Id, 4m) }), default);
        // B: pide 10 con 6 libres → reserva 6 y 4 en backorder (abierta)
        var b = await svc.RequestAsync((await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 10m) }), default)).PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        // C: borrador
        await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 1m) }), default);

        var rows = await OrderSource(f).LoadAsync(new DataQuery(), default);
        Assert.Equal(3, rows.Count);
        var ra = rows.Single(r => (string?)r["Number"] == a.Number);
        Assert.Equal(("SHIPPED", false, 100m, 4m, "Cliente C1", "W1"), ((string?)ra["StatusCode"], (bool)ra["IsOpen"]!, (decimal?)ra["FillRatePct"], (decimal)ra["UnitsShipped"]!, (string?)ra["ClientName"], (string?)ra["WarehouseCode"]));
        Assert.NotNull(ra["ShippedAtUtc"]);
        Assert.NotNull(ra["DaysToShip"]);
        var rb = rows.Single(r => (string?)r["Number"] == b.Number);
        Assert.Equal(("REQUESTED", true, true, 6m, 4m, 10m), ((string?)rb["StatusCode"], (bool)rb["IsOpen"]!, (bool)rb["HasBackorder"]!, (decimal)rb["UnitsReserved"]!, (decimal)rb["UnitsBackordered"]!, (decimal)rb["UnitsOrdered"]!));
        Assert.Null(rb["FillRatePct"]);   // todavía no terminó: sin cumplimiento ni días a despachar
        Assert.Null(rb["DaysToShip"]);
        Assert.Equal(100m, (decimal)rb["Total"]!);
        var draft = rows.Single(r => (string?)r["StatusCode"] == "DRAFT");
        Assert.Equal((false, false), ((bool)draft["IsOpen"]!, (bool)draft["HasBackorder"]!));
    }

    [Fact]
    public async Task Line_rows_show_pending_backorder_with_days_waiting_and_respect_ids_range_and_tenant()
    {
        var (f, w, c, svc, _, p) = await WorldAsync(onHand: 2m);
        await using var _f = f;
        var o = await svc.RequestAsync((await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 5m) }), default)).PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);

        var rows = await LineSource(f).LoadAsync(new DataQuery(), default);
        var r = Assert.Single(rows);
        Assert.Equal((o.Number, "MED", 5m, 2m, 3m, true, false), ((string?)r["OrderNumber"], (string?)r["Sku"], (decimal)r["QtyOrdered"]!, (decimal)r["QtyReserved"]!, (decimal)r["QtyBackordered"]!, (bool)r["IsOpen"]!, (bool)r["IsClosed"]!));
        Assert.Equal(0, (int)r["DaysWaiting"]!);
        Assert.Equal(50m, (decimal)r["LineTotal"]!);

        // el interceptor de auditoría pone la fecha de creación; el fixture no lo tiene
        foreach (var ord in await f.Db.Set<SalesOrder>().AsTracking().ToListAsync()) ord.CreatedAtUtc = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var id = (int)r["Id"]!;
        Assert.Single(await LineSource(f).LoadAsync(new DataQuery { Ids = new[] { id } }, default));
        Assert.Empty(await LineSource(f).LoadAsync(new DataQuery { Ids = new[] { id + 99 } }, default));
        Assert.Empty(await LineSource(f).LoadAsync(new DataQuery { FromUtc = DateTime.UtcNow.AddDays(1) }, default));
        Assert.Single(await OrderSource(f).LoadAsync(new DataQuery { FromUtc = DateTime.UtcNow.AddDays(-1), ToUtc = DateTime.UtcNow.AddDays(1) }, default));
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Empty(await LineSource(f).LoadAsync(new DataQuery(), default));
            Assert.Empty(await OrderSource(f).LoadAsync(new DataQuery(), default));
        }
    }

    private static IEnumerable<string> FieldsIn(string? json)
    {
        if (json is null) yield break;
        using var doc = JsonDocument.Parse(json);
        var stack = new Stack<JsonElement>();
        stack.Push(doc.RootElement);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Name == "field" && prop.Value.ValueKind == JsonValueKind.String) yield return prop.Value.GetString()!;
                    if (prop.Name == "by" && prop.Value.ValueKind == JsonValueKind.Array) foreach (var b in prop.Value.EnumerateArray()) yield return b.GetString()!;
                    stack.Push(prop.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) stack.Push(x);
        }
    }

    [Fact]
    public void The_system_filters_and_groups_only_use_fields_that_exist_in_each_source()
    {
        var orderFields = new SalesOrderDataSource(null!, null!, null!).Fields.Select(x => x.Key).ToHashSet();
        var lineFields = new SalesOrderLineDataSource(null!, null!).Fields.Select(x => x.Key).ToHashSet();
        foreach (var json in new[] { SalesAnalyticsRules.OpenFilter, SalesAnalyticsRules.ShippedFilter, SalesAnalyticsRules.OrdersByClientGroup })
            Assert.All(FieldsIn(json), fld => Assert.Contains(fld, orderFields));
        Assert.All(FieldsIn(SalesAnalyticsRules.PendingBackorderFilter), fld => Assert.Contains(fld, lineFields));
        // los campos que usan los indicadores y las columnas de las vistas sembradas
        foreach (var fld in new[] { "UnitsBackordered", "FillRatePct", "DaysToShip", "Status", "Number", "ClientName", "WarehouseCode", "RequiredDate", "IsLate", "LineCount", "UnitsOrdered", "UnitsShipped", "Total", "CreatedAtUtc" })
            Assert.Contains(fld, orderFields);
        foreach (var fld in new[] { "OrderNumber", "ClientName", "WarehouseCode", "Sku", "ProductName", "QtyOrdered", "QtyReserved", "QtyBackordered", "DaysWaiting" })
            Assert.Contains(fld, lineFields);
    }

    [Fact]
    public void The_sources_need_sales_view_and_the_sales_module()
    {
        var order = new SalesOrderDataSource(null!, null!, null!);
        var line = new SalesOrderLineDataSource(null!, null!);
        Assert.Equal((SalesCodes.EntityType, "CreatedAtUtc", ModuleKeys.SalesOrders), (order.Key, order.DateField, order.TenantModule));
        Assert.Equal((EntityTypes.SalesOrderLine, "CreatedAtUtc", ModuleKeys.SalesOrders), (line.Key, line.DateField, line.TenantModule));
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceReadPermission(order.EntityTypeCode ?? order.Key));
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceReadPermission(line.EntityTypeCode ?? line.Key));
    }

    [Fact]
    public async Task The_pulse_notice_lists_each_order_with_backorder_that_can_be_fulfilled_now()
    {
        var (f, w, c, svc, b1, p) = await WorldAsync(onHand: 2m);
        await using var _f = f;
        var o = await svc.RequestAsync((await svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 5m) }), default)).PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        var provider = new SalesBackorderReadyAttentionProvider(svc);
        var scope = new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.SalesOrders });

        Assert.Equal((ModuleKeys.SalesOrders, PermissionCatalog.SalesBackorder, "SALES_BACKORDER_READY"), (provider.TenantModule, provider.RequiredPermission, provider.Code));
        Assert.Equal(0, (await provider.ReadAsync(scope, 5, default)).Total);   // sin inventario nuevo no hay nada que surtir

        var bal = await f.Db.StockBalances.AsTracking().SingleAsync(x => x.WarehouseBinId == b1.WarehouseBinId);
        bal.QtyOnHand += 2m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await provider.ReadAsync(scope, 5, default);
        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal(("warn", "/sales-orders"), (item.Tone, item.Route));
        Assert.Equal((o.Number, "Cliente C1", "1", "2", o.PublicId.ToString()), (item.Params["number"], item.Params["client"], item.Params["lines"], item.Params["units"], item.Query!["order"]));
        Assert.Equal(1, result.Group!.Total);
        Assert.Empty((await provider.ReadAsync(scope, 0, default)).Items);   // take 0: solo el total
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 66 (P4, S3): avisos de las órdenes de venta — solicitada, despachada, cancelada (a mano y sola), crédito por autorizar y backorder que ya se puede surtir.
public sealed class SalesNotificationTests
{
    private static async Task<(WmsFixture F, Warehouse W, Teikem.Domain.Clients.Client C, SalesOrderService Svc, WarehouseBin B1, Product P)> WorldAsync(decimal onHand = 10m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<INotificationEventProvider, SalesEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
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

    private static Task<List<NotificationOutbox>> OutboxAsync(WmsFixture f, string? code = null)
        => f.Db.NotificationOutbox.AsNoTracking().Where(o => code == null || o.EventCode == code).OrderBy(o => o.NotificationOutboxId).ToListAsync();

    private static async Task AddStockAsync(WmsFixture f, Warehouse w, WarehouseBin bin, Product p, decimal qty)
    {
        var bal = await f.Db.StockBalances.AsTracking().SingleAsync(b => b.ProductId == p.ProductId && b.WarehouseBinId == bin.WarehouseBinId);
        bal.QtyOnHand += qty;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<SalesOrderDto> DraftAsync(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, Product p, decimal qty)
        => svc.CreateAsync(new SalesOrderRequest(w.PublicId, c.PublicId, Lines: new[] { new SalesOrderLineRequest(p.PublicId, qty) }), default);

    [Fact]
    public async Task Requesting_publishes_the_requested_event_with_its_variables()
    {
        var (f, w, c, svc, _, p) = await WorldAsync();
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 4m);
        Assert.Empty(await OutboxAsync(f));   // un borrador no avisa
        var r = await svc.RequestAsync(d.PublicId, null, default);

        var row = Assert.Single(await OutboxAsync(f));
        Assert.Equal(SalesEventCodes.OrderRequested, row.EventCode);
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal((r.Number, "Cliente C1", "1", "Quedó todo reservado."), (values["orderNumber"], values["client"], values["lineCount"], values["detail"]));
        Assert.Equal(w.Name, values["warehouse"]);
    }

    [Fact]
    public async Task A_request_with_backorder_says_so_and_a_failed_request_publishes_nothing()
    {
        var (f, w, c, svc, _, p) = await WorldAsync(onHand: 3m);
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 5m);
        await Assert.ThrowsAnyAsync<TeikemException>(() => svc.RequestAsync(d.PublicId, null, default));   // faltante sin backorder: 409
        Assert.Empty(await OutboxAsync(f));
        await svc.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        var values = NotificationRules.ParseValues((await OutboxAsync(f)).Single().PayloadJson);
        Assert.Equal("1 línea(s) con faltante en backorder.", values["detail"]);
    }

    [Fact]
    public async Task Cancelling_publishes_the_cancelled_event_with_the_reason()
    {
        var (f, w, c, svc, _, p) = await WorldAsync();
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 2m);
        await svc.RequestAsync(d.PublicId, null, default);
        await svc.CancelAsync(d.PublicId, "El cliente la anuló.", default);
        var row = (await OutboxAsync(f, SalesEventCodes.OrderCancelled)).Single();
        Assert.Equal("El cliente la anuló.", NotificationRules.ParseValues(row.PayloadJson)["reason"]);
    }

    [Fact]
    public async Task Shipping_everything_publishes_shipped_once_and_a_partial_dispatch_does_not()
    {
        var (f, w, c, svc, _, p) = await WorldAsync();
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 4m);
        var r = await svc.RequestAsync(d.PublicId, null, default);
        await svc.DispatchAsync(r.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(r.Lines[0].Id, 1m) }), default);
        Assert.Empty(await OutboxAsync(f, SalesEventCodes.OrderShipped));
        await svc.DispatchAsync(r.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(r.Lines[0].Id, 3m) }), default);
        var row = (await OutboxAsync(f, SalesEventCodes.OrderShipped)).Single();
        Assert.Equal("4", NotificationRules.ParseValues(row.PayloadJson)["shipped"]);
    }

    [Fact]
    public async Task Closing_every_line_with_shortage_cancels_the_order_and_publishes_cancelled()
    {
        var (f, w, c, svc, _, p) = await WorldAsync(onHand: 0m);
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 3m);
        var r = await svc.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        await svc.CloseLineAsync(r.PublicId, r.Lines[0].Id, null, default);
        var row = (await OutboxAsync(f, SalesEventCodes.OrderCancelled)).Single();
        Assert.Contains("cerraron con faltante", NotificationRules.ParseValues(row.PayloadJson)["reason"]);
    }

    [Fact]
    public async Task Credit_over_the_limit_publishes_a_credit_notice_once_per_situation_and_still_rejects()
    {
        var (f, w, c, svc, _, p) = await WorldAsync(onHand: 100m);
        await using var _f = f;
        var client = await f.Db.Clients.AsTracking().SingleAsync(x => x.ClientId == c.ClientId);
        client.CreditLimit = 100m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Get<FakeBalance>().Pending = 60m;
        var d = await DraftAsync(f, w, c, svc, p, 5m);

        await Assert.ThrowsAsync<CreditExceededException>(() => svc.RequestAsync(d.PublicId, null, default));
        await Assert.ThrowsAsync<CreditExceededException>(() => svc.RequestAsync(d.PublicId, null, default));   // el mismo intento no avisa otra vez
        var row = Assert.Single(await OutboxAsync(f, SalesEventCodes.CreditOverrideNeeded));
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal((d.Number, "100.00", "60.00", "50.00"), (values["orderNumber"], values["limit"], values["exposure"], values["amount"]));
        Assert.Empty(await OutboxAsync(f, SalesEventCodes.OrderRequested));

        f.Get<FakeBalance>().Pending = 70m;   // otra situación: avisa de nuevo
        await Assert.ThrowsAsync<CreditExceededException>(() => svc.RequestAsync(d.PublicId, null, default));
        Assert.Equal(2, (await OutboxAsync(f, SalesEventCodes.CreditOverrideNeeded)).Count);
    }

    [Fact]
    public async Task The_backorder_scan_notifies_when_stock_arrives_and_does_not_repeat_until_it_changes()
    {
        var (f, w, c, svc, b1, p) = await WorldAsync(onHand: 2m);
        await using var _f = f;
        var d = await DraftAsync(f, w, c, svc, p, 6m);
        await svc.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);   // reserva 2, backorder 4
        f.Db.NotificationOutbox.RemoveRange(await f.Db.NotificationOutbox.AsTracking().ToListAsync());
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        Assert.Equal(0, await svc.NotifyFulfillableBackordersAsync(default));   // sin inventario nuevo, nada
        await AddStockAsync(f, w, b1, p, 3m);                                  // entran 3
        Assert.Equal(1, await svc.NotifyFulfillableBackordersAsync(default));
        Assert.Equal(0, await svc.NotifyFulfillableBackordersAsync(default));   // misma situación: no repite
        var row = Assert.Single(await OutboxAsync(f, SalesEventCodes.BackorderAvailable));
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal(("MED × 3", "1"), (values["lines"], values["lineCount"]));
        await AddStockAsync(f, w, b1, p, 1m);                                  // entra más: cambia lo surtible, vuelve a avisar
        Assert.Equal(1, await svc.NotifyFulfillableBackordersAsync(default));
    }

    [Fact]
    public async Task Without_a_publisher_nothing_happens_and_the_operations_work_as_before()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
        });
        await using var _f = f;
        Assert.Equal(0, await f.Get<SalesOrderService>().NotifyFulfillableBackordersAsync(default));
    }

    [Fact]
    public void The_five_events_are_in_the_catalog_with_consistent_variables_and_codes()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new SalesEventProvider() });
        Assert.Equal(SalesEventCodes.All.OrderBy(x => x), catalog.All.Select(e => e.Code).OrderBy(x => x));
        foreach (var e in catalog.All)
        {
            Assert.InRange(e.Code.Length, 1, 60);
            Assert.Equal(ModuleKeys.SalesOrders, e.ModuleKey);
            var known = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), v => Assert.Contains(v, known));
        }
        Assert.False(catalog.Find(SalesEventCodes.CreditOverrideNeeded)!.IsOptional);   // es una petición de autorización: no se silencia
    }

    [Fact]
    public void The_scan_job_is_registered_every_ten_minutes_and_on_by_default()
    {
        var job = new SalesBackorderScanJob();
        Assert.Equal((JobKeys.SalesBackorderScan, JobScheduleKinds.Interval, 600, true), (job.Key, job.DefaultKind, job.DefaultIntervalSeconds, job.DefaultEnabled));
    }
}

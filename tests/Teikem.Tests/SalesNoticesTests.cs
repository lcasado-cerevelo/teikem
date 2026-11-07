using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): avisos — «Cliente nuevo pendiente de aprobación» (campana: evento; «Necesita tu atención»: sales.approve_client) y
// «Pedido urgente» (campana: evento; «Necesita tu atención»: warehouse.pick).
public sealed class SalesNoticesTests
{
    private static Task<List<NotificationOutbox>> OutboxAsync(SalesVendorWorld x, string code)
        => x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == code).OrderBy(o => o.NotificationOutboxId).ToListAsync();

    private static AttentionScope Scope => new(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.SalesOrders });

    [Fact]
    public void Both_events_are_in_the_sales_catalog_with_their_variables_and_links()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new SalesEventProvider() });
        var pending = catalog.Find(SalesEventCodes.ClientPendingApproval)!;
        Assert.Equal(("/clients", false), (pending.LinkTemplate, pending.IsOptional));   // no se puede silenciar: es una petición de aprobación
        Assert.Equal(new[] { "client", "clientCode", "seller" }, pending.Variables.Select(v => v.Name));
        var urgent = catalog.Find(SalesEventCodes.OrderUrgent)!;
        Assert.Equal("/sales-orders", urgent.LinkTemplate);
        Assert.Contains(urgent.Variables, v => v.Name == "note");
        Assert.Contains(SalesEventCodes.ClientPendingApproval, SalesEventCodes.All);
        Assert.Contains(SalesEventCodes.OrderUrgent, SalesEventCodes.All);
    }

    [Fact]
    public async Task A_new_prospect_publishes_the_pending_approval_notice_once()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var row = Assert.Single(await OutboxAsync(x, SalesEventCodes.ClientPendingApproval));
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal(("Farmacia Nueva", dto.Code), (values["client"], values["clientCode"]));
        Assert.True(values.ContainsKey("seller"));
    }

    [Fact]
    public async Task Requesting_an_urgent_order_notifies_the_warehouse_and_a_normal_one_does_not()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var urgent = await x.DraftAsync(c, p, 1m, new SalesOrderRequest(IsUrgent: true, Notes: "Antes del mediodía"));
        var normal = await x.DraftAsync(c, p, 1m);
        Assert.Empty(await OutboxAsync(x, SalesEventCodes.OrderUrgent));   // el borrador no avisa
        await x.Sales.RequestAsync(urgent.PublicId, null, default);
        await x.Sales.RequestAsync(normal.PublicId, null, default);
        var row = Assert.Single(await OutboxAsync(x, SalesEventCodes.OrderUrgent));
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal((urgent.Number, "Antes del mediodía"), (values["orderNumber"], values["note"]));
    }

    [Fact]
    public async Task Needs_your_attention_lists_prospects_for_the_approver_and_urgent_orders_for_the_warehouse()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var prospect = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var pendingProvider = x.F.Get<SalesClientPendingAttentionProvider>();
        Assert.Equal((PermissionCatalog.SalesApproveClient, ModuleKeys.SalesOrders), (pendingProvider.RequiredPermission, pendingProvider.TenantModule));
        var pending = await pendingProvider.ReadAsync(Scope, 5, default);
        var item = Assert.Single(pending.Items);
        Assert.Equal((1, SalesClientPendingAttentionProvider.ItemCode, prospect.PublicId.ToString(), "Farmacia Nueva"), (pending.Total, item.Code, item.Params["publicId"], item.Params["name"]));

        var urgentProvider = x.F.Get<SalesUrgentAttentionProvider>();
        Assert.Equal(PermissionCatalog.WarehousePick, urgentProvider.RequiredPermission);
        var c = await x.ClientAsync("C1");
        var draft = await x.DraftAsync(c, p, 1m, new SalesOrderRequest(IsUrgent: true));
        Assert.Equal(0, (await urgentProvider.ReadAsync(Scope, 5, default)).Total);   // un borrador todavía no es del almacén
        var r = await x.Sales.RequestAsync(draft.PublicId, null, default);
        var urgent = await urgentProvider.ReadAsync(Scope, 5, default);
        Assert.Equal((1, r.Number, "danger"), (urgent.Total, urgent.Items[0].Params["number"], urgent.Items[0].Tone));
        // el urgente de un prospecto no sale (no se puede despachar todavía)
        var prospectRow = await x.F.Db.Clients.AsNoTracking().SingleAsync(cl => cl.PublicId == prospect.PublicId);
        await x.Sales.RequestAsync((await x.DraftAsync(prospectRow, p, 1m, new SalesOrderRequest(IsUrgent: true))).PublicId, null, default);
        Assert.Equal(1, (await urgentProvider.ReadAsync(Scope, 5, default)).Total);
        // aprobado, ya no está pendiente
        await x.Prospects.ApproveAsync(prospect.PublicId, null, default);
        Assert.Equal(0, (await pendingProvider.ReadAsync(Scope, 5, default)).Total);
        Assert.Equal(2, (await urgentProvider.ReadAsync(Scope, 5, default)).Total);
    }
}

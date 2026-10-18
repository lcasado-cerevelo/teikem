using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 117d — los envíos del consignatario con acceso propio: SOLO VE (Luis, 2026-10-18: el consignatario no crea órdenes) las órdenes de su cliente que se entregan en su consignatario; no ve las de otro consignatario del
/// mismo cliente ni las de otro cliente (404), y los DTOs no llevan precios, factura, COD ni número de orden del cliente.
/// </summary>
public class ConsigneeOrdersTests
{
    private const int ClientA = 501, ClientB = 502;
    private const int TiendaUser = 51, ColmadoUser = 52, AdminA = 41;
    private const int LocTienda = 7001, LocColmado = 7002, LocOtro = 7003;

    private sealed record World(WmsFixture F, ConsigneeOrdersService Svc, Guid TiendaOrder, Guid ColmadoOrder, Guid OtherClientOrder) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();

        public void AsConsignee(int userId, int clientId, int locationId)
        {
            F.Tenant.IsPlatformAdmin = false;
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = clientId; F.Tenant.PortalLocationId = locationId; F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }

        public void AsClientPerson(int userId, int clientId)
        {
            F.Tenant.UserId = userId; F.Tenant.IsPortal = true; F.Tenant.PortalClientId = clientId; F.Tenant.PortalLocationId = null; F.Tenant.ScopedClientIds = new[] { clientId };
            F.Db.ChangeTracker.Clear();
        }
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var db = f.Db;
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.Clients.AddRange(
            new Client { ClientId = ClientA, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 900 },
            new Client { ClientId = ClientB, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 900 });
        Location L(int id, int client, string name) => new()
        {
            LocationId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client, Name = name, City = "Ponce", Line1 = "Calle 1",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), IsActive = true,
        };
        db.Locations.AddRange(L(LocTienda, ClientA, "Tienda Norte"), L(LocColmado, ClientA, "Colmado Sur"), L(LocOtro, ClientB, "Tienda de B"));
        await db.SaveChangesAsync();

        async Task<TransportOrder> Order(int client, string number, int location, string waybill)
        {
            var o = await f.AddOrderAsync(client, number, OrderStatuses.Submitted);
            var tracked = await db.TransportOrders.AsTracking().SingleAsync(t => t.TransportOrderId == o.TransportOrderId);
            tracked.WaybillNumber = waybill; tracked.TotalPieces = 2; tracked.QuotedAmount = 99.5m; tracked.CodAmount = 40m; tracked.ClientInvoiceNumber = "F-" + number; tracked.TrackToken = "tok-" + number;
            db.OrderStops.Add(new OrderStop { OrderStopId = 8000 + o.TransportOrderId, TransportOrderId = o.TransportOrderId, StopTypeLookupId = f.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2, LocationId = location, SnapName = "Entrega", SnapLine1 = "Calle 1", SnapCity = "Ponce", StatusCodeId = 1 });
            db.CargoLines.Add(new CargoLine { TransportOrderId = o.TransportOrderId, Description = "Caja", Quantity = 2, WeightKg = 7m, IsActive = true });
            await db.SaveChangesAsync();
            return o;
        }
        var t1 = await Order(ClientA, "T-1", LocTienda, "W-1");
        var t2 = await Order(ClientA, "T-2", LocColmado, "W-2");
        var t3 = await Order(ClientB, "T-3", LocOtro, "W-3");
        await Order(ClientA, "T-4", LocTienda, "W-4");
        db.ChangeTracker.Clear();
        var guids = await db.TransportOrders.AsNoTracking().ToDictionaryAsync(o => o.TransportOrderId, o => o.PublicId);
        return new World(f, new ConsigneeOrdersService(db, f.Tenant, f.Lookups, new OrderProgressService(db, f.Tenant, f.Lookups)), guids[t1.TransportOrderId], guids[t2.TransportOrderId], guids[t3.TransportOrderId]);
    }

    [Fact]
    public async Task The_consignee_sees_only_the_orders_delivered_to_him_newest_first()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        var page = await w.Svc.ListAsync(null, 0, 25, default);
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { "W-4", "W-1" }, page.Items.Select(i => i.WaybillNumber));
        Assert.All(page.Items, i => Assert.Equal(2, i.TotalPieces));
        Assert.Equal("tok-T-1", page.Items[1].TrackToken);
        Assert.NotNull(page.Items[0].Progress);

        w.AsConsignee(ColmadoUser, ClientA, LocColmado);
        Assert.Equal("W-2", Assert.Single((await w.Svc.ListAsync(null, 0, 25, default)).Items).WaybillNumber);
    }

    [Fact]
    public async Task Search_by_waybill_and_paging_work()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        Assert.Equal("W-4", Assert.Single((await w.Svc.ListAsync(" W-4 ", 0, 25, default)).Items).WaybillNumber);
        var second = await w.Svc.ListAsync(null, 1, 1, default);
        Assert.Equal(("W-1", 2), (second.Items.Single().WaybillNumber, second.Total));
        Assert.Empty((await w.Svc.ListAsync("nada", 0, 25, default)).Items);
    }

    [Fact]
    public async Task The_detail_is_his_own_with_his_address_and_no_money_or_internal_data()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        var d = await w.Svc.GetAsync(w.TiendaOrder, default);
        Assert.Equal(("W-1", 2), (d.WaybillNumber, d.TotalPieces));
        Assert.Equal("Calle 1", d.Delivery!.Line1);
        var pkg = Assert.Single(d.Packages);
        Assert.Equal(("Caja", 2, (decimal?)null), (pkg.Description, pkg.Pieces, pkg.WeightKg));   // sin pesos
        Assert.NotNull(d.Progress);
        // el DTO no tiene dónde llevar precios, factura, COD ni el número de orden del cliente
        var names = typeof(Teikem.Infrastructure.Contracts.ConsigneeOrderDto).GetProperties().Select(p => p.Name).Concat(typeof(Teikem.Infrastructure.Contracts.ConsigneeOrderListItemDto).GetProperties().Select(p => p.Name)).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Quoted") || n.Contains("Cod") || n.Contains("Invoice") || n == "OrderNumber" || n.Contains("Amount"));
    }

    [Fact]
    public async Task Another_consignees_order_or_another_clients_order_is_404_like_one_that_does_not_exist()
    {
        await using var w = await WorldAsync();
        w.AsConsignee(TiendaUser, ClientA, LocTienda);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.GetAsync(w.ColmadoOrder, default));        // otro consignatario del mismo cliente
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.GetAsync(w.OtherClientOrder, default));     // de otro cliente
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.GetAsync(Guid.NewGuid(), default));         // inexistente
    }

    [Fact]
    public async Task A_person_of_the_client_or_a_session_without_a_consignee_has_no_consignee_orders()
    {
        await using var w = await WorldAsync();
        w.AsClientPerson(AdminA, ClientA);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.ListAsync(null, 0, 25, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Svc.GetAsync(w.TiendaOrder, default));
    }
}

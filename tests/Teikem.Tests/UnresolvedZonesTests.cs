using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-21 — «Por qué estos envíos salen sin zona»: los envíos activos sin zona se agrupan por código postal y pueblo de su parada de entrega y cada grupo dice su motivo con las zonas de hoy
/// (sin datos, ninguna zona lo cubre, lo cubren varias o ya hay zona que lo cubre y solo falta asignarla).
/// </summary>
public sealed class UnresolvedZonesTests
{
    private static async Task<WmsFixture> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<ZoneGroupService>();
            s.AddSingleton<DispatchZoneService>();
        });
        f.SetModules(ModuleKeys.Catalog, ModuleKeys.LtlGround);
        return f;
    }

    /// <summary>Los criterios de zona (ZoneMatchType) y un miembro de la zona, directo en la base (el fixture no trae ese catálogo).</summary>
    private static async Task AddMembersAsync(WmsFixture f, params (int ZoneId, string Type, string Value)[] members)
    {
        var ids = new Dictionary<string, int>();
        foreach (var type in new[] { ZoneMatchTypes.PostalCode, ZoneMatchTypes.Municipality })
        {
            var row = new LookupCode { LookupCodeId = 9000 + ids.Count, Entity = LookupDomains.ZoneMatchType, InternalCode = type, LabelJson = "{\"es\":\"x\",\"en\":\"x\"}", SortOrder = 1, IsSystem = true, IsActive = true };
            f.Db.LookupCodes.Add(row);
            ids[type] = row.LookupCodeId;
        }
        foreach (var (zoneId, type, value) in members) f.Db.Set<DispatchZoneMember>().Add(new DispatchZoneMember { DispatchZoneId = zoneId, MatchTypeLookupId = ids[type], MatchValue = value });
        await f.Db.SaveChangesAsync();
    }

    private static async Task OrderAsync(WmsFixture f, int clientId, string number, string? zip, string? city, bool withStop = true)
    {
        var o = await f.AddOrderAsync(clientId, number, OrderStatuses.Submitted);
        if (withStop)
            f.Db.Set<OrderStop>().Add(new OrderStop { TransportOrderId = o.TransportOrderId, StopTypeLookupId = f.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2, SnapLine1 = "Calle 1", SnapPostalCode = zip, SnapCity = city, StatusCodeId = 1 });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Groups_by_zip_and_town_with_the_reason_and_the_most_orders_first()
    {
        await using var f = await NewWorldAsync();
        var zones = f.Get<DispatchZoneService>();
        var sj = await zones.CreateAsync(new DispatchZoneRequest("Z-SJ", "San Juan"), default);
        var po = await zones.CreateAsync(new DispatchZoneRequest("Z-PO", "Ponce"), default);
        await AddMembersAsync(f, (sj.Id, ZoneMatchTypes.Municipality, "San Juan"), (po.Id, ZoneMatchTypes.PostalCode, "00731"));
        var c = await f.AddClientAsync("C1");
        await OrderAsync(f, c.ClientId, "A1", "00999", "Utuado");     // ninguna zona lo cubre
        await OrderAsync(f, c.ClientId, "A2", "00999-1234", "utuado");  // el mismo grupo (ZIP+4 y mayúsculas aparte)
        await OrderAsync(f, c.ClientId, "A3", "00999", "Utuado");
        await OrderAsync(f, c.ClientId, "B1", "00907", "San  Juan");    // lo cubre Z-SJ por el pueblo: solo falta asignar
        await OrderAsync(f, c.ClientId, "C1", null, null);              // la parada no trae datos
        await OrderAsync(f, c.ClientId, "D1", null, null, withStop: false);   // sin parada de entrega: también «sin datos»

        var r = await zones.UnresolvedOrdersAsync(100, default);
        Assert.Equal(6, r.TotalOrders);
        Assert.Equal(3, r.Groups);
        Assert.Equal(("00999", "Utuado", 3, "NO_MATCH"), (r.Rows[0].PostalCode, r.Rows[0].City, r.Rows[0].Orders, r.Rows[0].Reason));
        var resolvable = Assert.Single(r.Rows, x => x.Reason == "RESOLVABLE");
        Assert.Equal((1, "00907"), (resolvable.Orders, resolvable.PostalCode));
        var noData = Assert.Single(r.Rows, x => x.Reason == "NO_DATA");
        Assert.Equal(2, noData.Orders);
    }

    [Fact]
    public async Task Orders_that_already_have_a_zone_or_are_inactive_are_not_listed_and_top_limits_the_rows()
    {
        await using var f = await NewWorldAsync();
        var zones = f.Get<DispatchZoneService>();
        var z = await zones.CreateAsync(new DispatchZoneRequest("Z-1", "Uno"), default);
        var c = await f.AddClientAsync("C1");
        await OrderAsync(f, c.ClientId, "OK", "00901", "Cayey");
        await OrderAsync(f, c.ClientId, "X1", "00902", "Aibonito");
        await OrderAsync(f, c.ClientId, "X2", "00903", "Orocovis");
        var withZone = await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.OrderNumber == "OK");
        withZone.DeliveryZoneId = z.Id;
        var inactive = await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.OrderNumber == "X2");
        inactive.IsActive = false;
        await f.Db.SaveChangesAsync();

        var all = await zones.UnresolvedOrdersAsync(100, default);
        Assert.Equal(1, all.TotalOrders);
        Assert.Equal("00902", Assert.Single(all.Rows).PostalCode);
        Assert.Single((await zones.UnresolvedOrdersAsync(1, default)).Rows);
    }
}

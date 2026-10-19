using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 129: nivel de zona (Isla/Metro) — catálogo, el nivel en la zona, la zona de la orden, los filtros, y las tarifas del chofer y del contrato con nivel opcional (el nivel gana, la general respalda).
public sealed class ZoneGroupTests
{
    private static async Task<WmsFixture> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<ZoneGroupService>();
            s.AddSingleton<DispatchZoneService>();
            s.AddSingleton<DriverRateResolver>();
            s.AddSingleton<OrderPriceVisibility>();
            s.AddSingleton<OrderReadService>();
        });
        f.SetModules(ModuleKeys.Catalog, ModuleKeys.LtlGround);
        return f;
    }

    private static async Task<Driver> DriverAsync(WmsFixture f, string code)
    {
        var d = new Driver { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = code, FullName = $"Chofer {code}", StatusCodeId = 1, IsActive = true };
        f.Db.Drivers.Add(d);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return d;
    }

    [Fact]
    public async Task Levels_are_a_tenant_catalog_with_default_driver_and_the_zone_takes_one()
    {
        await using var f = await NewWorldAsync();
        var groups = f.Get<ZoneGroupService>();
        var zones = f.Get<DispatchZoneService>();
        var island = await DriverAsync(f, "ISLA");

        Assert.Equal(ZoneGroupRules.CodeRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => groups.CreateAsync(new ZoneGroupRequest(" "), default))).Message);
        var metro = await groups.CreateAsync(new ZoneGroupRequest("metro", "Metro", 1), default);
        var isla = await groups.CreateAsync(new ZoneGroupRequest("ISLA", "Isla", 2, island.PublicId), default);
        Assert.Equal(("METRO", "Metro", 1, (Guid?)null), (metro.Code, metro.Name, metro.SortOrder, metro.DefaultDriverPublicId));
        Assert.Equal((island.PublicId, "ISLA", "Chofer ISLA"), (isla.DefaultDriverPublicId!.Value, isla.DefaultDriverCode, isla.DefaultDriverName));
        Assert.Equal(ZoneGroupRules.DuplicateMessage, (await Assert.ThrowsAsync<ConflictException>(() => groups.CreateAsync(new ZoneGroupRequest("Metro"), default))).Message);
        Assert.Equal(ZoneGroupRules.CodeImmutableMessage, (await Assert.ThrowsAsync<ValidationException>(() => groups.UpdateAsync(metro.Id, new ZoneGroupRequest("X"), default))).Message);
        Assert.Equal(ZoneGroupRules.DriverNotFoundMessage, (await Assert.ThrowsAsync<ValidationException>(() => groups.UpdateAsync(metro.Id, new ZoneGroupRequest(DefaultDriverPublicId: Guid.NewGuid()), default))).Message);

        // la zona toma el nivel al crearse o al editarse, y lo puede soltar
        var z1 = await zones.CreateAsync(new DispatchZoneRequest("Z-ISLA", "Ponce y el sur", isla.Id), default);
        Assert.Equal((isla.Id, "ISLA", "Isla"), (z1.ZoneGroupId!.Value, z1.ZoneGroupCode, z1.ZoneGroupName));
        var z2 = await zones.CreateAsync(new DispatchZoneRequest("Z-SJ", "San Juan"), default);
        Assert.Null(z2.ZoneGroupId);
        z2 = await zones.UpdateAsync(z2.Id, new DispatchZoneRequest(ZoneGroupId: metro.Id), default);
        Assert.Equal("METRO", z2.ZoneGroupCode);
        Assert.Equal(ZoneGroupRules.NotFoundMessage, (await Assert.ThrowsAsync<ValidationException>(() => zones.UpdateAsync(z2.Id, new DispatchZoneRequest(ZoneGroupId: 9999), default))).Message);
        z2 = await zones.UpdateAsync(z2.Id, new DispatchZoneRequest(ClearZoneGroup: true), default);
        Assert.Null(z2.ZoneGroupCode);
        Assert.Contains(await zones.ListAsync(false, default), z => z.Code == "Z-ISLA" && z.ZoneGroupCode == "ISLA");

        // un nivel con zonas activas no se inactiva; la cuenta de zonas sale en la lista
        Assert.Equal(ZoneGroupRules.InUseMessage, (await Assert.ThrowsAsync<ConflictException>(() => groups.SetActiveAsync(isla.Id, false, default))).Message);
        Assert.Equal(1, (await groups.ListAsync(false, default)).Single(g => g.Code == "ISLA").ZoneCount);
        var off = await groups.SetActiveAsync(metro.Id, false, default);
        Assert.False(off.IsActive);
        Assert.DoesNotContain(await groups.ListAsync(false, default), g => g.Code == "METRO");
        Assert.Contains(await groups.ListAsync(true, default), g => g.Code == "METRO");
    }

    [Fact]
    public void The_order_zone_is_resolved_by_zip_then_town_and_ambiguity_leaves_it_empty()
    {
        var members = new[] { new ZoneMember("ISLA", ZoneMatchTypes.PostalCode, "00716"), new ZoneMember("METRO", ZoneMatchTypes.Municipality, "san juan"), new ZoneMember("OTRA", ZoneMatchTypes.Municipality, "san juan") };
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["ISLA"] = 1, ["METRO"] = 2, ["OTRA"] = 3 };
        Assert.Equal(1, OrderZoneResolver.Resolve(members, ids, "00716", "Ponce"));
        Assert.Null(OrderZoneResolver.Resolve(members, ids, "00901", "San Juan"));   // dos zonas por municipio: ambigua
        Assert.Null(OrderZoneResolver.Resolve(members, ids, null, null));
        Assert.Null(OrderZoneResolver.Resolve(members, ids, "00999", "Nadie"));
    }

    [Fact]
    public void Contract_rates_with_a_level_win_for_that_level_and_the_general_backs_up()
    {
        var general = new RateRow(1, 10, RateKinds.PerService, "STANDARD", "BOX", 5m, Array.Empty<TierRow>());
        var isla = new RateRow(2, 10, RateKinds.PerService, "STANDARD", "BOX", 9m, Array.Empty<TierRow>(), ZoneGroupId: 7);
        var rows = new[] { general, isla };
        var flags = new ContractFlags(true, false, false, false, null, null, null);
        Assert.Equal(9m, ContractRateResolver.BaseRate(rows, flags, "STANDARD", "BOX", 7).Amount);      // el nivel de la orden gana
        Assert.Equal(5m, ContractRateResolver.BaseRate(rows, flags, "STANDARD", "BOX", 8).Amount);      // otro nivel: la general
        Assert.Equal(5m, ContractRateResolver.BaseRate(rows, flags, "STANDARD", "BOX", null).Amount);   // sin nivel: la general
        Assert.Equal(9m, ContractRateResolver.BaseRate(new[] { isla }, flags, "STANDARD", "BOX", 7).Amount);
        Assert.Null(ContractRateResolver.BaseRate(new[] { isla }, flags, "STANDARD", "BOX", null).Amount);   // solo tarifa de nivel y la orden no tiene nivel: nada
        var quote = ContractRateResolver.QuoteOrder(rows, flags, new[] { new QuoteLine("STANDARD", "BOX", 1) }, 0m, 7);
        Assert.Equal(9m, quote.Total);
    }

    [Fact]
    public async Task Driver_delivery_rates_with_a_level_win_for_that_level_and_the_general_backs_up()
    {
        await using var f = await NewWorldAsync();
        var driver = await DriverAsync(f, "D1");
        var groups = f.Get<ZoneGroupService>();
        var isla = await groups.CreateAsync(new ZoneGroupRequest("ISLA", "Isla"), default);
        var metro = await groups.CreateAsync(new ZoneGroupRequest("METRO", "Metro"), default);
        var today = TenantClock.Default.Today;
        f.Db.DriverDeliveryRates.AddRange(
            new DriverDeliveryRate { TenantId = WmsFixture.TenantId, DriverId = driver.DriverId, ServiceTypeLookupId = 1, PackageTypeLookupId = 2, Rate = 1.50m, EffectiveFrom = today.AddDays(-10), IsActive = true },
            new DriverDeliveryRate { TenantId = WmsFixture.TenantId, DriverId = driver.DriverId, ServiceTypeLookupId = 1, PackageTypeLookupId = 2, Rate = 4.00m, EffectiveFrom = today.AddDays(-10), IsActive = true, ZoneGroupId = isla.Id });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var resolver = f.Get<DriverRateResolver>();
        Assert.Equal(4.00m, (await resolver.DeliveryRateAsync(driver.DriverId, 1, 2, today, default, isla.Id)).Amount);
        Assert.Equal(1.50m, (await resolver.DeliveryRateAsync(driver.DriverId, 1, 2, today, default, metro.Id)).Amount);   // sin tarifa de ese nivel: la general
        Assert.Equal(1.50m, (await resolver.DeliveryRateAsync(driver.DriverId, 1, 2, today, default)).Amount);
        // reglas puras
        var all = await f.Db.DriverDeliveryRates.AsNoTracking().ToListAsync();
        Assert.Single(DriverPayoutRules.ForZoneGroup(all, isla.Id), r => r.ZoneGroupId == isla.Id);
        Assert.Single(DriverPayoutRules.ForZoneGroup(all, null), r => r.ZoneGroupId == null);
    }

    [Fact]
    public async Task Orders_filter_by_zone_and_level_and_carry_them_in_the_row_and_the_detail()
    {
        await using var f = await NewWorldAsync();
        var groups = f.Get<ZoneGroupService>();
        var zones = f.Get<DispatchZoneService>();
        var isla = await groups.CreateAsync(new ZoneGroupRequest("ISLA", "Isla"), default);
        var zIsla = await zones.CreateAsync(new DispatchZoneRequest("Z-ISLA", "Sur", isla.Id), default);
        var zMetro = await zones.CreateAsync(new DispatchZoneRequest("Z-SJ", "San Juan"), default);
        var c = await f.AddClientAsync("C1");
        var a = await f.AddOrderAsync(c.ClientId, "A", OrderStatuses.Submitted);
        var b = await f.AddOrderAsync(c.ClientId, "B", OrderStatuses.Submitted);
        var n = await f.AddOrderAsync(c.ClientId, "N", OrderStatuses.Submitted);
        var ta = await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.TransportOrderId == a.TransportOrderId);
        var tb = await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.TransportOrderId == b.TransportOrderId);
        (ta.DeliveryZoneId, tb.DeliveryZoneId) = (zIsla.Id, zMetro.Id);
        f.Db.Set<OrderStop>().Add(new OrderStop { TransportOrderId = a.TransportOrderId, StopTypeLookupId = f.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2, SnapLine1 = "Calle 1", SnapCity = "Ponce", StatusCodeId = 1 });   // la ficha exige parada de entrega
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var reader = f.Get<OrderReadService>();
        var byZone = await reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, ZoneIds: new[] { zMetro.Id }), OrderScope.Any, default);
        Assert.Equal(new[] { "B" }, byZone.Items.Select(i => i.OrderNumber).ToArray());
        Assert.Equal(("Z-SJ", (string?)null), (byZone.Items[0].ZoneCode, byZone.Items[0].ZoneGroupCode));
        var byLevel = await reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, ZoneGroupIds: new[] { isla.Id }), OrderScope.Any, default);
        Assert.Equal(new[] { "A" }, byLevel.Items.Select(i => i.OrderNumber).ToArray());
        Assert.Equal("ISLA", byLevel.Items[0].ZoneGroupCode);
        var noZone = await reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, NoZone: true), OrderScope.Any, default);
        Assert.Equal(new[] { "N" }, noZone.Items.Select(i => i.OrderNumber).ToArray());
        // la ficha expone zona y nivel (el mapeo es el mismo del listado: ZonesOfAsync)
        var props = typeof(OrderDetailDto).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var name in new[] { "ZoneCode", "ZoneName", "ZoneGroupCode", "ZoneGroupName" }) Assert.Contains(name, props);
        _ = n;
    }
}

using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 97 (P10): número de conduce (waybill) — el identificador único de cada orden de entrega en la compañía.
public sealed class WaybillTests
{
    [Fact]
    public void The_waybill_kind_and_pattern_are_known()
    {
        Assert.True(NumberingRules.IsKnownKind(WaybillCodes.NumberKind));
        // todo contador que un servicio pide al NumberSequenceService real debe ser conocido (el aviso de salida no lo era: «Error interno» al guardar)
        Assert.True(NumberingRules.IsKnownKind(Teikem.Domain.ExitNotices.ExitNoticeCodes.NumberKind));
        Assert.Equal("CND-0000007", NumberFormat.Resolve(WaybillCodes.NumberPattern, 7));
        Assert.Equal("CND-1234567", NumberFormat.Resolve(WaybillCodes.NumberPattern, 1234567));
    }

    private static async Task GiveWaybillsAsync(OrderReadFixture f)
    {
        var n = 1;
        foreach (var o in await f.Db.TransportOrders.AsTracking().IgnoreQueryFilters().OrderBy(o => o.TransportOrderId).ToListAsync())
            o.WaybillNumber = $"CND-{n++:0000000}";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Lists_details_search_filter_and_lookup_use_the_waybill()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        await GiveWaybillsAsync(f);
        var scoped = await f.Db.TransportOrders.AsNoTracking().OrderBy(o => o.TransportOrderId).Select(o => new { o.TransportOrderId, o.WaybillNumber }).ToListAsync();
        var o2 = scoped.Single(o => o.TransportOrderId == f.O2.TransportOrderId);

        var all = await f.Reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, false, 0, 100), OrderScope.Any, default);
        Assert.All(all.Items, i => Assert.StartsWith("CND-", i.WaybillNumber));
        Assert.Equal(o2.WaybillNumber, all.Items.Single(i => i.Id == f.O2.TransportOrderId).WaybillNumber);

        // búsqueda libre y filtro parcial por conduce
        var free = await f.Reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, o2.WaybillNumber, null, null, false, 0, 100), OrderScope.Any, default);
        Assert.Equal(new[] { f.O2.TransportOrderId }, free.Items.Select(i => i.Id).ToArray());
        var filtered = await f.Reader.GetListAsync(new OrderListQuery(null, null, null, null, null, null, null, null, null, false, 0, 100, Waybill: o2.WaybillNumber![^3..]), OrderScope.Any, default);
        Assert.Contains(f.O2.TransportOrderId, filtered.Items.Select(i => i.Id));

        // buscar o escanear: el conduce es exacto y gana sobre el número de orden
        var lookup = await f.Reader.LookupAsync(o2.WaybillNumber!, OrderScope.Any, default);
        Assert.Equal((OrderReadService.MatchedByWaybill, 1), (lookup.MatchedBy, lookup.Matches.Count));
        Assert.Equal(f.O2.TransportOrderId, lookup.Matches[0].Id);
        // un número de orden repetido entre clientes sigue siendo ambiguo, el conduce no
        Assert.Equal(f.O2.TransportOrderId, (await f.Reader.LookupAsync(o2.WaybillNumber, OrderScope.Any, default)).Matches.Single().Id);

        // la ficha
        var detail = await f.Reader.GetAsync(f.O2.PublicId, OrderScope.Any, default);
        Assert.Equal(o2.WaybillNumber, detail.WaybillNumber);
    }

    [Fact]
    public async Task Another_company_never_finds_the_waybill()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        await GiveWaybillsAsync(f);
        var code = (await f.Db.TransportOrders.AsNoTracking().FirstAsync(o => o.TransportOrderId == f.O1.TransportOrderId)).WaybillNumber!;
        using (f.Tenant.As(2))
            Assert.Empty((await f.Reader.LookupAsync(code, OrderScope.Any, default)).Matches);
    }
}

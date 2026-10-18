using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 114 — tipo de servicio «pick up» (recojo y traigo): Sometida → Recogida → En almacén (final), sin ruta ni chofer; las marcas viven en el ExtraJson del tipo de servicio
/// ({"pickUp":true,"billable":true}) y el ajuste de la compañía las pisa.
/// </summary>
public class PickUpServiceTypeTests
{
    [Theory]
    [InlineData(null, false, true)]
    [InlineData("", false, true)]
    [InlineData("{}", false, true)]
    [InlineData("{\"pickUp\":true}", true, true)]
    [InlineData("{\"pickUp\":true,\"billable\":false}", true, false)]
    [InlineData("{\"PICKUP\":true,\"Billable\":false}", true, false)]
    [InlineData("{\"pickUp\":\"si\"}", false, true)]
    [InlineData("esto no es json", false, true)]
    [InlineData("[1,2]", false, true)]
    public void The_marks_are_read_from_the_extra_json_and_a_broken_one_is_ignored(string? json, bool pickUp, bool billable)
        => Assert.Equal(new ServiceTypeMarks(pickUp, billable), ServiceTypeMarks.Parse(json));

    [Fact]
    public void A_company_setting_overrides_only_the_marks_it_names()
        => Assert.Equal(new ServiceTypeMarks(true, false), ServiceTypeMarks.Parse("{\"pickUp\":true,\"billable\":true}").Apply("{\"billable\":false}"));

    /// <summary>Marca como «pick up» el tipo de servicio que usan las órdenes del fixture.</summary>
    private static async Task MarkPickUpAsync(TripServiceFixture f, string json = "{\"pickUp\":true,\"billable\":true}")
    {
        var lookup = await f.Db.LookupCodes.SingleAsync(l => l.LookupCodeId == 1);
        lookup.ExtraJson = json;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<ScanActionResultDto> ScanAsync(TripServiceFixture f, SeededOrder o, string action, int piece = 1)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(action, TripServiceFixture.PieceNumberOf(o.Id, piece), TripServiceFixture.Today), default);

    [Fact]
    public async Task Arrive_on_a_pick_up_order_ends_in_warehouse_and_the_next_pieces_are_counted_there()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await MarkPickUpAsync(f);
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 3);

        var first = await ScanAsync(f, o, ScanActions.Arrive, 1);
        Assert.Equal(ScanActionOutcomes.Applied, first.Outcome);
        Assert.Equal(OrderStatuses.InWarehouse, await f.OrderStatusAsync(o.Id));

        var second = await ScanAsync(f, o, ScanActions.Arrive, 2);                              // la orden ya es final, pero la pieza se cuenta
        Assert.Equal(ScanActionOutcomes.Counted, second.Outcome);
        Assert.Equal((2, 3), (second.Scanned, second.Total));
        Assert.Equal(ScanActionOutcomes.Duplicate, (await ScanAsync(f, o, ScanActions.Arrive, 2)).Outcome);
    }

    [Fact]
    public async Task A_submitted_pick_up_order_jumps_through_picked_up_to_in_warehouse()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await MarkPickUpAsync(f);
        var o = await f.SeedOrderAsync(OrderStatuses.Submitted, pieces: 1);

        var r = await ScanAsync(f, o, ScanActions.Arrive);
        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(new[] { OrderStatuses.Pickup }, r.Skipped.ToArray());                      // Recogida queda registrada «brincada»
        Assert.Equal(OrderStatuses.InWarehouse, await f.OrderStatusAsync(o.Id));
    }

    [Fact]
    public async Task A_pick_up_order_does_not_go_to_a_route_or_leave()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await MarkPickUpAsync(f);
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 1);
        foreach (var action in new[] { ScanActions.Route, ScanActions.Depart })
        {
            var r = await ScanAsync(f, o, action);
            Assert.Equal(ScanActionOutcomes.NotFound, r.Outcome);
            Assert.Equal("notfound", r.Voice);
            Assert.Equal(string.Format(ServiceTypeMarks.PickUpOnlyMessageFormat, o.OrderNumber), r.Message);
        }
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(o.Id));
    }

    [Fact]
    public async Task Closing_the_count_of_an_order_in_warehouse_lists_the_pieces_that_never_arrived()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await MarkPickUpAsync(f);
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 3);
        for (var i = 1; i <= 3; i++) await ScanAsync(f, o, ScanActions.Pickup, i);
        await ScanAsync(f, o, ScanActions.Arrive, 1);
        await ScanAsync(f, o, ScanActions.Arrive, 2);

        var asked = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(o.PublicId), default);
        Assert.True(asked.NeedsConfirmation);
        Assert.Equal(new[] { TripServiceFixture.PieceNumberOf(o.Id, 3) }, asked.Preview!.MissingNumbers.ToArray());
    }

    [Fact]
    public async Task A_regular_service_type_is_not_affected_by_the_pick_up_rules()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 1);
        Assert.Equal(ScanActionOutcomes.Applied, (await ScanAsync(f, o, ScanActions.Arrive)).Outcome);
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(o.Id));
    }
}

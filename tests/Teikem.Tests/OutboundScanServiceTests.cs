using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using TripEntity = Teikem.Domain.Trips.Trip;
using TripOrderEntity = Teikem.Domain.Trips.TripOrder;
using TripRules = Teikem.Domain.Trips.TripRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P8 — OutboundScanService (maestro L273) sobre InMemory: el escaneo asigna DE VERDAD la orden a la ruta abierta del
/// día de su zona (la de menor TripId), salta a la siguiente si la primera ya no admite la orden al re-verificar bajo
/// bloqueo, responde NO_OPEN_ROUTE si no queda ninguna, convierte una carrera (ConflictException) en ALREADY_ASSIGNED con la
/// ruta ganadora y nunca cambia el OrderStatus.
/// </summary>
public class OutboundScanServiceTests
{
    private static async Task<OutboundScanResultDto> ScanAsync(TripServiceFixture f, string code)
        => await f.Get<OutboundScanService>().ScanAsync(new OutboundScanRequest(code, TripServiceFixture.Today), default);

    [Fact]
    public async Task Scan_assigns_to_the_lowest_open_trip_of_the_zone_and_date_without_changing_the_order_status()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var first = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var second = await f.SeedTripAsync("2026-0002", TripStatuses.Planned, f.Zone1);
        await f.SeedTripAsync("2026-0003", TripStatuses.Draft, f.Zone2);
        await f.SeedTripAsync("2026-0004", TripStatuses.Draft, f.Zone1, planDate: TripServiceFixture.Today.AddDays(1));
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Equal("FOUND_ASSIGNED", r.Outcome);
        Assert.Equal("found", r.Voice);
        Assert.Equal(OrderReadService.MatchedByPackBatch, r.MatchedBy);
        Assert.Equal(first.PublicId, r.TripPublicId);
        Assert.Equal("Asignada a la ruta 2026-0001.", r.Message);
        Assert.Equal("Z1", r.ZoneCode);
        var link = await f.Db.TripOrders.AsNoTracking().SingleAsync(t => t.TransportOrderId == order.Id && t.IsCurrent);
        Assert.Equal(first.TripId, link.TripId);
        Assert.NotEqual(second.TripId, link.TripId);
        Assert.Equal(1, await f.Db.RouteStops.AsNoTracking().CountAsync(s => s.OrderStopId == order.StopId));
        Assert.Equal(OrderStatuses.Confirmed, await f.OrderStatusAsync(order.Id));

        // Escanear otra vez: 'Ya' (dup) con la misma ruta, sin duplicar la parada.
        var dup = await ScanAsync(f, order.OrderNumber);
        Assert.Equal("ALREADY_ASSIGNED", dup.Outcome);
        Assert.Equal("dup", dup.Voice);
        Assert.Equal(first.PublicId, dup.TripPublicId);
        Assert.Equal(1, await f.Db.RouteStops.AsNoTracking().CountAsync(s => s.OrderStopId == order.StopId));
    }

    [Fact]
    public async Task Dispatched_or_other_date_trips_are_not_open_and_the_next_open_one_is_used()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var dispatched = await f.SeedTripAsync("2026-0001", TripStatuses.Dispatched, f.Zone1);
        var open = await f.SeedTripAsync("2026-0002", TripStatuses.Planned, f.Zone1);
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Equal("FOUND_ASSIGNED", r.Outcome);
        Assert.Equal(open.PublicId, r.TripPublicId);
        Assert.False(await f.Db.TripOrders.AnyAsync(t => t.TripId == dispatched.TripId));
    }

    [Fact]
    public async Task Under_lock_a_full_trip_is_skipped_for_the_next_open_one()
    {
        // La ruta de menor id sigue 'abierta' al leer los hechos, pero al re-verificar bajo bloqueo ya no admite la orden
        // (tope técnico de 300 paradas): el escaneo pasa a la siguiente ruta abierta de la zona y la fecha.
        await using var f = await TripServiceFixture.CreateAsync();
        var full = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var next = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);
        var fill = new List<int>();
        for (var i = 0; i < 300; i++) fill.Add((await f.SeedOrderAsync(OrderStatuses.Confirmed)).Id);
        await f.AddOrdersAsync(full.TripId, fill.ToArray());
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Equal("FOUND_ASSIGNED", r.Outcome);
        Assert.Equal(next.PublicId, r.TripPublicId);
    }

    [Fact]
    public async Task Without_an_open_trip_the_order_stays_unassigned()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedTripAsync("2026-0001", TripStatuses.Dispatched, f.Zone1);
        await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1, isActive: false);
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Equal("FOUND_UNASSIGNED", r.Outcome);
        Assert.Equal("NO_OPEN_ROUTE", r.ReasonCode);
        Assert.Equal("found", r.Voice);
        Assert.Equal($"No hay ruta abierta para la zona Z1 en la fecha {TripServiceFixture.Today:yyyy-MM-dd}; queda sin asignar.", r.Message);
        Assert.False(await f.Db.TripOrders.AnyAsync());
    }

    [Fact]
    public async Task A_race_lost_under_lock_is_reported_as_already_assigned_to_the_winner()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var target = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var winner = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone2);
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        // Cuando el escaneo ya bloqueó la ruta destino (Trip tracked), otra solicitud asigna la orden a la ruta 'winner'.
        f.Lookups.BeforeGetId = async () =>
        {
            if (!f.Db.ChangeTracker.Entries<TripEntity>().Any(e => e.Entity.TripId == target.TripId)) return;
            f.Lookups.BeforeGetId = null;
            f.Db.TripOrders.Add(new TripOrderEntity
            {
                TenantId = TripServiceFixture.TenantId, TripId = winner.TripId, TransportOrderId = order.Id,
                SortHint = 1, IsCurrent = true, AssignedAtUtc = DateTime.UtcNow, AssignedBy = 2,
            });
            await f.Db.SaveChangesAsync();
        };

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Null(f.Lookups.BeforeGetId);   // el gancho sí corrió dentro de la asignación
        Assert.Equal("ALREADY_ASSIGNED", r.Outcome);
        Assert.Equal("dup", r.Voice);
        Assert.Equal(winner.PublicId, r.TripPublicId);
        Assert.Equal("Ya estaba en la ruta 2026-0002.", r.Message);
        Assert.False(await f.Db.TripOrders.AnyAsync(t => t.TripId == target.TripId));
        Assert.Equal(OrderStatuses.Confirmed, await f.OrderStatusAsync(order.Id));
    }

    [Fact]
    public async Task An_order_that_stops_being_eligible_under_lock_is_reported_as_not_eligible()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var target = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var order = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var onHoldId = f.Id(StatusDomains.OrderStatus, OrderStatuses.OnHold);

        // Con la ruta ya bloqueada (Trip tracked), otra solicitud manda la orden a ON_HOLD: la re-verificación de
        // RouteWriter.AddOrdersAsync bajo bloqueo la rechaza y el escaneo lo reporta como NOT_ELIGIBLE.
        f.Lookups.BeforeGetId = async () =>
        {
            if (!f.Db.ChangeTracker.Entries<TripEntity>().Any(e => e.Entity.TripId == target.TripId)) return;
            f.Lookups.BeforeGetId = null;
            var o = await f.Db.TransportOrders.AsTracking().SingleAsync(x => x.TransportOrderId == order.Id);
            o.StatusCodeId = onHoldId;
            await f.Db.SaveChangesAsync();
        };

        var r = await ScanAsync(f, order.PackBatchNumber);

        Assert.Null(f.Lookups.BeforeGetId);   // el gancho corrió dentro de la asignación
        Assert.Equal("NOT_ELIGIBLE", r.Outcome);
        Assert.Equal("notfound", r.Voice);
        Assert.Equal(TripRules.LateralOrderMessage(OrderStatuses.OnHold), r.Message);
        Assert.Null(r.TripPublicId);
        Assert.False(await f.Db.TripOrders.AnyAsync());
    }

    [Fact]
    public async Task Not_found_no_zone_and_not_eligible_are_typed_results()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var noZone = await f.SeedOrderAsync(OrderStatuses.Confirmed, "00999");
        var draft = await f.SeedOrderAsync(OrderStatuses.Draft);

        var missing = await ScanAsync(f, "NOEXISTE");
        Assert.Equal("NOT_FOUND", missing.Outcome);
        Assert.Equal("notfound", missing.Voice);

        var nz = await ScanAsync(f, noZone.PackBatchNumber);
        Assert.Equal("FOUND_UNASSIGNED", nz.Outcome);
        Assert.Equal("NO_ZONE", nz.ReasonCode);

        var ne = await ScanAsync(f, draft.PackBatchNumber);
        Assert.Equal("NOT_ELIGIBLE", ne.Outcome);
        Assert.Equal("notfound", ne.Voice);
        Assert.Equal("La orden está en Entrada; confírmela antes de asignarla a una ruta.", ne.Message);

        var empty = await Assert.ThrowsAsync<ValidationException>(() => ScanAsync(f, "  "));
        Assert.Equal("Escanee o escriba un código.", empty.Message);
        Assert.False(await f.Db.TripOrders.AnyAsync());
    }
}

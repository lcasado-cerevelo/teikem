using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;
using DispatchBatchRules = Teikem.Domain.Trips.DispatchBatchRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P8 — TripDispatchService (maestro L271) sobre InMemory con TripIssueBuilder, TripReadService y los efectos
/// reales: el selector solo muestra rutas activas DRAFT/PLANNED del día con CanDispatch = sin bloqueantes, ordenadas por
/// zona y código; el despacho en lote es por ruta (una que falla no impide las demás), un id ajeno queda como 'Ruta no
/// encontrada.' y los ítems salen en el orden pedido; sin DISPATCHED habilitada en el pipeline, 422 explícito.
/// </summary>
public class TripDispatchServiceTests
{
    [Fact]
    public async Task Selector_lists_only_open_active_trips_of_the_day_with_their_blockers()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var a = await f.SeedTripAsync("2026-0002", TripStatuses.Planned, f.Zone2);
        var b = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1, withVehicle: false);
        var c = await f.SeedTripAsync("2026-0003", TripStatuses.Draft, f.Zone1);
        await f.SeedTripAsync("2026-0004", TripStatuses.Dispatched, f.Zone1);
        await f.SeedTripAsync("2026-0005", TripStatuses.Draft, f.Zone1, isActive: false);
        await f.SeedTripAsync("2026-0006", TripStatuses.Planned, f.Zone1, planDate: TripServiceFixture.Today.AddDays(1));
        await f.AddOrdersAsync(a.TripId, (await f.SeedOrderAsync(OrderStatuses.Confirmed, "00902")).Id);
        await f.AddOrdersAsync(b.TripId, (await f.SeedOrderAsync(OrderStatuses.Confirmed)).Id);
        await f.AddOrdersAsync(c.TripId, (await f.SeedOrderAsync(OrderStatuses.Confirmed)).Id);

        var list = await f.Get<TripDispatchService>().ListDispatchableAsync(TripServiceFixture.Today, default);

        // Zona y luego código: Z1 (0001, 0003) antes que Z2 (0002).
        Assert.Equal(new[] { "2026-0001", "2026-0003", "2026-0002" }, list.Select(d => d.Trip.Code));
        var noVehicle = list.Single(d => d.Trip.PublicId == b.PublicId);
        Assert.False(noVehicle.CanDispatch);
        Assert.Contains(noVehicle.Issues, i => i.Code == "NO_VEHICLE" && i.Blocking);
        Assert.True(list.Single(d => d.Trip.PublicId == a.PublicId).CanDispatch);
        Assert.True(list.Single(d => d.Trip.PublicId == c.PublicId).CanDispatch);
    }

    [Fact]
    public async Task Consolidated_route_with_orders_of_two_clients_shows_each_client_and_dispatches()
    {
        // Consolidación multi-cliente (maestro L263): órdenes de distintos clientes en una misma ruta física, aunque
        // repitan el número (la numeración es por cliente).
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedClientAsync(1, "Cliente Uno");
        await f.SeedClientAsync(2, "Cliente Dos");
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        var a = await f.SeedOrderAsync(OrderStatuses.Confirmed, clientId: 1, orderNumber: "R-00001");
        var b = await f.SeedOrderAsync(OrderStatuses.Confirmed, clientId: 2, orderNumber: "R-00001");
        await f.AddOrdersAsync(tripId, a.Id, b.Id);

        var detail = await f.Get<TripReadService>().GetAsync(publicId, default);
        Assert.Equal(new[] { (a.PublicId, "Cliente Uno"), (b.PublicId, "Cliente Dos") },
            detail.Stops.Select(s => (s.OrderPublicId, s.ClientName)));
        Assert.All(detail.Stops, s => Assert.Equal("R-00001", s.OrderNumber));

        var dispatched = await f.Get<TripDispatchService>().DispatchAsync(publicId, null, default);
        Assert.Equal(TripStatuses.Dispatched, dispatched.StatusCode);
        Assert.Equal(OrderStatuses.Planned, await f.OrderStatusAsync(a.Id));
        Assert.Equal(OrderStatuses.Planned, await f.OrderStatusAsync(b.Id));
    }

    [Fact]
    public async Task Vehicle_with_an_open_work_order_blocks_the_selector_and_the_dispatch()
    {
        // Maestro L286: el vehículo debe estar disponible (sin mantenimiento abierto que lo inhabilite) para despachar.
        await using var f = await TripServiceFixture.CreateAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        var o = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.AddOrdersAsync(tripId, o.Id);
        f.Availability.BlockedVehicles.Add(9);   // vehículo de SeedTripAsync

        var item = Assert.Single(await f.Get<TripDispatchService>().ListDispatchableAsync(TripServiceFixture.Today, default));
        Assert.False(item.CanDispatch);
        var issue = Assert.Single(item.Issues, i => i.Code == "VEHICLE_UNAVAILABLE");
        Assert.True(issue.Blocking);
        Assert.StartsWith("El vehículo no está disponible para despacho: ", issue.Message);
        Assert.Contains(AlwaysAvailable.BlockedVehicleMessage, issue.Message);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<TripDispatchService>().DispatchAsync(publicId, null, default));
        Assert.Equal(422, ex.StatusCode);
        Assert.StartsWith("La ruta 2026-0001 no se puede despachar:", ex.Message);
        Assert.Contains("El vehículo no está disponible para despacho:", ex.Message);
        Assert.Equal(TripStatuses.Planned, await f.TripStatusAsync(tripId));
        Assert.Equal(OrderStatuses.Confirmed, await f.OrderStatusAsync(o.Id));
    }

    [Fact]
    public async Task Batch_dispatch_is_per_trip_reports_foreign_ids_and_keeps_the_requested_order()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var ok = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        var blocked = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1, withVehicle: false);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.AddOrdersAsync(ok.TripId, o1.Id);
        await f.AddOrdersAsync(blocked.TripId, o2.Id);
        var foreign = Guid.NewGuid();

        var result = await f.Get<TripDispatchService>().DispatchBatchAsync(
            new TripBatchDispatchRequest(new[] { blocked.PublicId, foreign, ok.PublicId }), default);

        Assert.Equal(3, result.Requested);
        Assert.Equal(1, result.Dispatched);
        Assert.Equal(new[] { blocked.PublicId, foreign, ok.PublicId }, result.Items.Select(i => i.TripPublicId));

        var blockedItem = result.Items[0];
        Assert.False(blockedItem.Dispatched);
        Assert.StartsWith("La ruta 2026-0002 no se puede despachar:", blockedItem.Error);
        Assert.Contains(blockedItem.Issues, i => i.Code == "NO_VEHICLE");
        var foreignItem = result.Items[1];
        Assert.False(foreignItem.Dispatched);
        Assert.Null(foreignItem.Code);
        Assert.Equal(DispatchBatchRules.TripNotFoundMessage, foreignItem.Error);
        Assert.Equal("Ruta no encontrada.", foreignItem.Error);
        Assert.True(result.Items[2].Dispatched);

        // La despachada congela su ruta y lleva sus órdenes hasta PLANNED; la bloqueada no cambia.
        Assert.Equal(TripStatuses.Dispatched, await f.TripStatusAsync(ok.TripId));
        var route = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.TripId == ok.TripId && r.IsActive);
        Assert.Equal(RouteStatuses.Active, f.CodeOf(route.StatusCodeId));
        Assert.Equal(OrderStatuses.Planned, await f.OrderStatusAsync(o1.Id));
        Assert.Equal(TripStatuses.Draft, await f.TripStatusAsync(blocked.TripId));
        Assert.Equal(OrderStatuses.Confirmed, await f.OrderStatusAsync(o2.Id));

        // El selector ya no muestra la ruta despachada.
        var list = await f.Get<TripDispatchService>().ListDispatchableAsync(TripServiceFixture.Today, default);
        Assert.DoesNotContain(list, d => d.Trip.PublicId == ok.PublicId);
        Assert.Contains(list, d => d.Trip.PublicId == blocked.PublicId);
    }

    [Fact]
    public async Task Dispatch_with_dispatched_disabled_in_the_pipeline_is_422()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        await f.AddOrdersAsync(trip.TripId, (await f.SeedOrderAsync(OrderStatuses.Confirmed)).Id);
        await f.DisableAsync(StatusDomains.TripStatus, TripStatuses.Dispatched);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<TripDispatchService>().DispatchAsync(trip.PublicId, null, default));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("El pipeline de rutas de esta compañía no tiene habilitada la etapa DISPATCHED.", ex.Message);
        Assert.Equal(TripStatuses.Planned, await f.TripStatusAsync(trip.TripId));
    }

    [Fact]
    public async Task Dispatch_with_blockers_is_422_with_errors_per_issue_code()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1, withVehicle: false);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<TripDispatchService>().DispatchAsync(trip.PublicId, null, default));
        Assert.StartsWith("La ruta 2026-0001 no se puede despachar:", ex.Message);
        Assert.NotNull(ex.Errors);
        Assert.Contains("NO_VEHICLE", ex.Errors!.Keys);
        Assert.Contains("NO_STOPS", ex.Errors!.Keys);
    }

    [Fact]
    public async Task Route_without_driver_is_not_dispatchable_and_dispatch_is_422_no_driver()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1, withDriver: false);
        await f.AddOrdersAsync(trip.TripId, (await f.SeedOrderAsync(OrderStatuses.Confirmed)).Id);
        var svc = f.Get<TripDispatchService>();

        var item = (await svc.ListDispatchableAsync(TripServiceFixture.Today, default)).Single(d => d.Trip.PublicId == trip.PublicId);
        Assert.False(item.CanDispatch);
        Assert.Contains(item.Issues, i => i.Code == "NO_DRIVER" && i.Blocking);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.DispatchAsync(trip.PublicId, null, default));
        Assert.Equal(422, ex.StatusCode);
        Assert.StartsWith("La ruta 2026-0001 no se puede despachar:", ex.Message);
        Assert.Contains("NO_DRIVER", ex.Errors!.Keys);
        Assert.DoesNotContain("NO_VEHICLE", ex.Errors!.Keys);
        Assert.Equal(TripStatuses.Planned, await f.TripStatusAsync(trip.TripId));
    }
}

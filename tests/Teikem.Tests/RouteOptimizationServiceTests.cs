using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;
using RouteEditRules = Teikem.Domain.Trips.RouteEditRules;
using RouteOptimizationResult = Teikem.Domain.Trips.RouteOptimizationResult;
using RoutePlanJson = Teikem.Domain.Trips.RoutePlanJson;
using TripRules = Teikem.Domain.Trips.TripRules;
using UnassignedPlanStop = Teikem.Domain.Trips.UnassignedPlanStop;
using UnassignedReasons = Teikem.Domain.Trips.UnassignedReasons;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P8 — RouteOptimizationService (maestro L264-L268) sobre InMemory con un IRouteOptimizer falso:
/// - optimización en tres fases: corrida PENDING → OK con la versión nueva, ResponseJson y UnassignedCount; lo que no cabe
///   vuelve a 'sin asignar' y el Trip pasa de DRAFT a PLANNED con 'Ruta optimizada (versión n)';
/// - FASE E: excepción del motor, resultado inválido y cancelación dejan la corrida en ERROR (nunca PENDING) y no tocan la ruta;
/// - ruta modificada durante el cálculo: corrida en ERROR 'Descartada: …' y 409 'La ruta cambió mientras se optimizaba …';
/// - sin paradas: 422 sin crear corrida;
/// - reordenar recalcula las ETAs en la misma versión, sin corrida (L265);
/// - pin manual: precisión MANUAL, BOLA por routeStopId (404) y 422 con la ruta despachada (L268).
/// </summary>
public class RouteOptimizationServiceTests
{
    private static async Task<(TripServiceFixture F, int TripId, Guid TripPublicId, List<SeededOrder> Orders)> DraftTripWithOrdersAsync(int count)
    {
        var f = await TripServiceFixture.CreateAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var orders = new List<SeededOrder>();
        for (var i = 0; i < count; i++) orders.Add(await f.SeedOrderAsync(OrderStatuses.Confirmed));
        if (count > 0) await f.AddOrdersAsync(tripId, orders.Select(o => o.Id).ToArray());
        return (f, tripId, publicId, orders);
    }

    private static async Task<List<string>> RunStatusesAsync(TripServiceFixture f)
        => (await f.Db.OptimizationRuns.AsNoTracking().OrderBy(r => r.OptimizationRunId).Select(r => r.StatusCodeId).ToListAsync())
            .Select(f.CodeOf).ToList();

    // ================================================================ camino feliz

    [Fact]
    public async Task Optimize_creates_version_2_closes_the_run_ok_and_releases_what_does_not_fit()
    {
        var (f, tripId, publicId, orders) = await DraftTripWithOrdersAsync(3);
        await using var _ = f;
        f.Optimizer.Handler = (req, _) => Task.FromResult(new RouteOptimizationResult(
            new[] { orders[2].StopId, orders[0].StopId },
            new[] { new UnassignedPlanStop(orders[1].StopId, UnassignedReasons.CapacityStops) },
            null));
        var v1 = await f.Db.Routes.AsNoTracking().SingleAsync();
        var v1Before = await f.Db.RouteStops.AsNoTracking().Where(s => s.RouteId == v1.RouteId).OrderBy(s => s.Sequence)
            .Select(s => new { s.OrderStopId, s.Sequence, s.PlannedArrivalUtc, s.PlannedDepartureUtc }).ToListAsync();
        Assert.Equal(3, v1Before.Count);

        var result = await f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default);

        Assert.Equal(OptimizerEngines.Heuristic, result.EngineCode);
        Assert.Equal(OptimizationRunStatuses.Ok, result.StatusCode);
        Assert.Equal(2, result.RouteVersion);
        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(1, result.UnassignedCount);
        var u = Assert.Single(result.Unassigned);
        Assert.Equal(orders[1].PublicId, u.OrderPublicId);
        Assert.Equal(UnassignedReasons.CapacityStops, u.ReasonCode);
        Assert.Equal("Excede el máximo de paradas del vehículo.", u.Reason);

        // Corrida OK apuntando a la versión nueva, con el plan y los totales.
        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(result.RunId, run.OptimizationRunId);
        Assert.Equal(OptimizationRunStatuses.Ok, f.CodeOf(run.StatusCodeId));
        var active = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.IsActive);
        Assert.Equal(2, active.Version);
        Assert.Equal(active.RouteId, run.RouteId);
        Assert.Equal(1, run.UnassignedCount);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.NotNull(run.ResponseJson);
        Assert.Equal(orders[1].PublicId, Assert.Single(RoutePlanJson.ParseUnassigned(run.ResponseJson)).OrderPublicId);
        // Historial OPTIMIZATION_RUN: nacimiento (PENDING) y cierre (OK).
        Assert.Equal(2, (await f.CommentsAsync(EntityTypes.OptimizationRun, run.OptimizationRunId)).Count);

        // Versiones: la v1 queda archivada; la v2 OPTIMIZED con la secuencia del motor.
        var old = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.RouteId == v1.RouteId);
        Assert.False(old.IsActive);
        Assert.Equal(RouteStatuses.Archived, f.CodeOf(old.StatusCodeId));
        // Maestro L264: la versión archivada conserva INTACTO el plan anterior (las 3 paradas, su secuencia y sus ETAs),
        // también la que no cupo: la liberación ocurre sobre la versión nueva.
        var v1After = await f.Db.RouteStops.AsNoTracking().Where(s => s.RouteId == v1.RouteId).OrderBy(s => s.Sequence)
            .Select(s => new { s.OrderStopId, s.Sequence, s.PlannedArrivalUtc, s.PlannedDepartureUtc }).ToListAsync();
        Assert.Equal(v1Before, v1After);
        Assert.Equal(3, old.StopCount);
        Assert.Equal(2, active.StopCount);
        Assert.Equal(RouteStatuses.Optimized, f.CodeOf(active.StatusCodeId));
        Assert.Equal(new[] { orders[2].StopId, orders[0].StopId },
            await f.Db.RouteStops.AsNoTracking().Where(s => s.RouteId == active.RouteId).OrderBy(s => s.Sequence).Select(s => s.OrderStopId).ToListAsync());

        // Lo que no cupo vuelve a 'sin asignar' sin tocar su estatus; el Trip pasa a PLANNED con su comentario.
        Assert.False(await f.Db.TripOrders.AnyAsync(t => t.TransportOrderId == orders[1].Id && t.IsCurrent));
        Assert.Equal(OrderStatuses.Confirmed, await f.OrderStatusAsync(orders[1].Id));
        Assert.Equal(TripStatuses.Planned, await f.TripStatusAsync(tripId));
        Assert.Contains("Ruta optimizada (versión 2)", await f.CommentsAsync(EntityTypes.Trip, tripId));
        Assert.Equal(TripStatuses.Planned, result.Trip.StatusCode);
        Assert.Equal(orders[1].PublicId, Assert.Single(result.Trip.LastRunUnassigned).OrderPublicId);

        // La lista de corridas la muestra con su versión.
        var listed = Assert.Single(await f.Get<RouteOptimizationService>().ListRunsAsync(publicId, default));
        Assert.Equal(2, listed.RouteVersion);
        Assert.Equal(OptimizationRunStatuses.Ok, listed.StatusCode);
    }

    [Fact]
    public async Task Engine_receives_vehicle_capacity_windows_service_time_and_load_of_each_stop()
    {
        // Maestro L264: optimizar 'respetando capacidad del vehículo, ventanas de tiempo y tiempo de servicio'. El motor
        // solo respeta lo que recibe: la solicitud debe traer la capacidad del vehículo de la ruta, la hora de salida y, por
        // parada, ventana, servicio, peso y volumen de la orden.
        var (f, tripId, publicId, orders) = await DraftTripWithOrdersAsync(2);
        await using var _ = f;
        await f.SeedVehicleAsync(9, maxStops: 5, maxWeightKg: 100m, maxVolumeM3: 2m);   // vehículo de SeedTripAsync
        var windowStart = TripServiceFixture.Today.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc);
        var windowEnd = windowStart.AddHours(2);
        var order = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orders[0].Id);
        order.TotalWeightKg = 30m;
        order.TotalVolumeM3 = 0.5m;
        var stop = await f.Db.OrderStops.SingleAsync(s => s.OrderStopId == orders[0].StopId);
        stop.WindowStartUtc = windowStart;
        stop.WindowEndUtc = windowEnd;
        stop.ServiceMinutes = 20;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var plannedStart = await f.Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.PlannedStartUtc).SingleAsync();

        Teikem.Domain.Trips.RouteOptimizationRequest? seen = null;
        f.Optimizer.Handler = (req, _) =>
        {
            seen = req;
            return Task.FromResult(new RouteOptimizationResult(req.Stops.Select(s => s.OrderStopId).ToList(), Array.Empty<UnassignedPlanStop>(), null));
        };

        await f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default);

        Assert.NotNull(seen);
        Assert.Equal(new Teikem.Domain.Trips.VehicleCapacity(5, 100m, 2m), seen!.Capacity);
        Assert.NotNull(plannedStart);
        Assert.Equal(plannedStart, seen.StartUtc);
        Assert.Equal(2, seen.Stops.Count);
        var s0 = seen.Stops.Single(s => s.OrderStopId == orders[0].StopId);
        Assert.Equal(30m, s0.WeightKg);
        Assert.Equal(0.5m, s0.VolumeM3);
        Assert.Equal(windowStart, s0.WindowStartUtc);
        Assert.Equal(windowEnd, s0.WindowEndUtc);
        Assert.Equal(20, s0.ServiceMinutes);
        Assert.Equal("Z1", s0.ZoneCode);
        var s1 = seen.Stops.Single(s => s.OrderStopId == orders[1].StopId);
        Assert.Null(s1.WeightKg);
        Assert.Null(s1.WindowEndUtc);
        Assert.Equal(10, s1.ServiceMinutes);
    }

    [Fact]
    public async Task What_did_not_fit_is_listed_until_it_is_reassigned_to_another_trip()
    {
        // Maestro L266: 'paradas no asignadas visibles para reasignar a otro trip'. LastRunUnassigned solo muestra las que
        // SIGUEN sin ruta vigente: al reasignarla a otra ruta desaparece y, si se libera de nuevo, vuelve a aparecer.
        var (f, _, publicId, orders) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        f.Optimizer.Handler = (req, _) => Task.FromResult(new RouteOptimizationResult(
            new[] { orders[0].StopId },
            new[] { new UnassignedPlanStop(orders[1].StopId, UnassignedReasons.CapacityWeight) },
            null));
        await f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default);
        var reader = f.Get<TripReadService>();
        Assert.Equal(orders[1].PublicId, Assert.Single((await reader.GetAsync(publicId, default)).LastRunUnassigned).OrderPublicId);

        var (otherTripId, _) = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);
        await f.AddOrdersAsync(otherTripId, orders[1].Id);
        Assert.Empty((await reader.GetAsync(publicId, default)).LastRunUnassigned);

        var other = await f.Db.LockTripAsync(otherTripId, default);
        await f.Get<RouteWriter>().ReleaseOrderAsync(other, orders[1].Id, default);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var again = Assert.Single((await reader.GetAsync(publicId, default)).LastRunUnassigned);
        Assert.Equal(orders[1].PublicId, again.OrderPublicId);
        Assert.Equal("Excede la capacidad de peso del vehículo.", again.Reason);
    }

    // ================================================================ FASE E y ruta cambiada

    [Fact]
    public async Task Engine_exception_leaves_the_run_in_error_and_the_route_untouched()
    {
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        f.Optimizer.Handler = (_, _) => throw new InvalidOperationException("boom");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default));
        Assert.Equal(RouteEditRules.EngineErrorMessage, ex.Message);

        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OptimizationRunStatuses.Error, f.CodeOf(run.StatusCodeId));
        Assert.Contains("boom", run.ErrorMessage);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(1, (await f.Db.Routes.AsNoTracking().SingleAsync(r => r.IsActive)).Version);
        Assert.Equal(TripStatuses.Draft, await f.TripStatusAsync(tripId));
    }

    [Fact]
    public async Task Invalid_engine_result_leaves_the_run_in_error()
    {
        var (f, _, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        f.Optimizer.Handler = (_, _) => Task.FromResult(new RouteOptimizationResult(new[] { 999999 }, Array.Empty<UnassignedPlanStop>(), null));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default));
        Assert.Equal(RouteEditRules.EngineErrorMessage, ex.Message);
        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OptimizationRunStatuses.Error, f.CodeOf(run.StatusCodeId));
        Assert.StartsWith(RouteEditRules.InvalidResultRunErrorPrefix, run.ErrorMessage);
        Assert.Single(await f.Db.Routes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Engine_timeout_leaves_the_run_in_error_with_409()
    {
        // Cancelación que NO viene del cliente (el token de la solicitud sigue vivo): es la rama del timeout de 60 s.
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(1);
        await using var _f = f;
        f.Optimizer.Handler = (_, _) => throw new OperationCanceledException();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default));
        Assert.Equal(RouteEditRules.EngineErrorMessage, ex.Message);
        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OptimizationRunStatuses.Error, f.CodeOf(run.StatusCodeId));
        Assert.Equal(RouteEditRules.EngineTimeoutRunErrorMessage, run.ErrorMessage);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(1, (await f.Db.Routes.AsNoTracking().SingleAsync(r => r.IsActive)).Version);
        Assert.Equal(TripStatuses.Draft, await f.TripStatusAsync(tripId));
    }

    [Fact]
    public async Task Client_cancellation_during_the_engine_leaves_the_run_in_error()
    {
        var (f, _, publicId, _) = await DraftTripWithOrdersAsync(1);
        await using var _f = f;
        using var cts = new CancellationTokenSource();
        f.Optimizer.Handler = async (_, token) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("no llega");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, cts.Token));
        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OptimizationRunStatuses.Error, f.CodeOf(run.StatusCodeId));
        Assert.Equal(RouteEditRules.CancelledRunErrorMessage, run.ErrorMessage);
    }

    [Fact]
    public async Task Route_changed_during_the_engine_discards_the_run_with_409()
    {
        var (f, tripId, publicId, orders) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        f.Optimizer.Handler = async (req, token) =>
        {
            // Otra solicitud crea una versión nueva mientras el motor calcula (fuera de la transacción de la corrida).
            var trip = await f.Db.LockTripAsync(tripId, token);
            await f.Get<RouteWriter>().ReplaceActiveRouteAsync(trip, new[] { orders[1].StopId, orders[0].StopId }, RouteStatuses.Optimized, "manual", token);
            await f.Db.SaveChangesAsync(token);
            return new RouteOptimizationResult(req.Stops.Select(s => s.OrderStopId).ToList(), Array.Empty<UnassignedPlanStop>(), null);
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default));
        Assert.Equal("La ruta cambió mientras se optimizaba; vuelva a optimizar.", ex.Message);

        var run = await f.Db.OptimizationRuns.AsNoTracking().SingleAsync();
        Assert.Equal(OptimizationRunStatuses.Error, f.CodeOf(run.StatusCodeId));
        Assert.Equal("Descartada: la ruta cambió durante el cálculo.", run.ErrorMessage);
        // Queda la versión que ganó (2) y ninguna otra vigente; el resultado descartado no creó la 3.
        var active = await f.Db.Routes.AsNoTracking().Where(r => r.IsActive).ToListAsync();
        Assert.Equal(2, Assert.Single(active).Version);
        Assert.Equal(2, await f.Db.Routes.CountAsync());
        Assert.Equal(TripStatuses.Draft, await f.TripStatusAsync(tripId));
    }

    [Fact]
    public async Task Optimizing_without_stops_is_422_and_creates_no_run()
    {
        var (f, _, publicId, _) = await DraftTripWithOrdersAsync(0);
        await using var _f = f;

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<RouteOptimizationService>().OptimizeAsync(publicId, null, default));
        Assert.Equal("La ruta no tiene paradas que optimizar.", ex.Message);
        Assert.Empty(await f.Db.OptimizationRuns.AsNoTracking().ToListAsync());
        Assert.Equal(0, f.Optimizer.Calls);
    }

    [Fact]
    public async Task No_run_is_ever_left_pending()
    {
        var (f, _, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        var svc = f.Get<RouteOptimizationService>();

        await svc.OptimizeAsync(publicId, null, default);
        f.Optimizer.Handler = (_, _) => throw new TimeoutException("lento");
        await Assert.ThrowsAsync<ConflictException>(() => svc.OptimizeAsync(publicId, null, default));

        var statuses = await RunStatusesAsync(f);
        Assert.Equal(new[] { OptimizationRunStatuses.Ok, OptimizationRunStatuses.Error }, statuses);
    }

    // ================================================================ reordenar (L265)

    [Fact]
    public async Task Reorder_recomputes_etas_in_the_same_version_without_a_run()
    {
        var (f, _, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        var ids = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).Select(s => s.RouteStopId).ToListAsync();
        var before = await f.Get<TripReadService>().GetAsync(publicId, default);

        var after = await f.Get<RouteOptimizationService>().ReorderAsync(publicId, new RouteSequenceRequest(new[] { ids[1], ids[0] }), default);

        Assert.Equal(new[] { ids[1], ids[0] }, after.Stops.Select(s => s.Id));
        Assert.Equal(before.RouteVersion, after.RouteVersion);
        Assert.True(after.Stops[0].PlannedArrivalUtc < after.Stops[1].PlannedArrivalUtc);
        Assert.True(after.Stops[0].PlannedArrivalUtc < before.Stops[1].PlannedArrivalUtc);
        Assert.Empty(await f.Db.OptimizationRuns.AsNoTracking().ToListAsync());

        var other = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);
        var foreign = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.AddOrdersAsync(other.TripId, foreign.Id);
        var foreignStop = await f.Db.RouteStops.AsNoTracking().Where(s => s.OrderStopId == foreign.StopId).Select(s => s.RouteStopId).SingleAsync();
        var bad = await Assert.ThrowsAsync<ValidationException>(() => f.Get<RouteOptimizationService>()
            .ReorderAsync(publicId, new RouteSequenceRequest(new[] { ids[0], foreignStop }), default));
        Assert.Equal(TripRules.SequenceMessage, bad.Message);
    }

    // ================================================================ pin manual (L268)

    [Fact]
    public async Task Manual_pin_marks_manual_accuracy_and_rejects_foreign_stops_and_dispatched_routes()
    {
        var (f, tripId, publicId, orders) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        var svc = f.Get<RouteOptimizationService>();
        var stop1 = await f.Db.RouteStops.AsNoTracking().Where(s => s.OrderStopId == orders[0].StopId).Select(s => s.RouteStopId).SingleAsync();

        var detail = await svc.SetStopLocationAsync(publicId, stop1, new StopLocationRequest(18.4655, -66.1057), default);
        Assert.Equal(GeocodeAccuracies.Manual, detail.Stops.Single(s => s.Id == stop1).GeocodeAccuracyCode);
        var manualId = await f.Lookups.GetIdAsync(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.Manual);
        Assert.Equal(manualId, (await f.Db.OrderStops.AsNoTracking().SingleAsync(s => s.OrderStopId == orders[0].StopId)).GeocodeAccuracyLookupId);
        Assert.All(detail.Stops, s => Assert.NotNull(s.PlannedArrivalUtc));

        var invalid = await Assert.ThrowsAsync<ValidationException>(() => svc.SetStopLocationAsync(publicId, stop1, new StopLocationRequest(91, 0), default));
        Assert.Equal("La latitud debe estar entre -90 y 90.", invalid.Message);
        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.SetStopLocationAsync(publicId, stop1, new StopLocationRequest(null, null), default));
        Assert.Equal("Indique la latitud y la longitud.", missing.Message);

        // BOLA por id hijo: una parada de otra ruta por la URL de esta → 404 sin tocar nada.
        var other = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);
        var foreign = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.AddOrdersAsync(other.TripId, foreign.Id);
        var foreignStop = await f.Db.RouteStops.AsNoTracking().Where(s => s.OrderStopId == foreign.StopId).Select(s => s.RouteStopId).SingleAsync();
        var notFound = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.SetStopLocationAsync(publicId, foreignStop, new StopLocationRequest(18.4, -66.1), default));
        Assert.Equal(404, notFound.StatusCode);
        Assert.Equal("Parada no encontrada en esta ruta.", notFound.Message);
        Assert.NotEqual(manualId, (await f.Db.OrderStops.AsNoTracking().SingleAsync(s => s.OrderStopId == foreign.StopId)).GeocodeAccuracyLookupId);

        // Ruta despachada: el pin es contenido congelado → 422.
        await f.SetTripStatusAsync(tripId, TripStatuses.Dispatched);
        var frozen = await Assert.ThrowsAsync<StatusRuleException>(() => svc.SetStopLocationAsync(publicId, stop1, new StopLocationRequest(18.4, -66.1), default));
        Assert.Equal("La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.", frozen.Message);
    }
}

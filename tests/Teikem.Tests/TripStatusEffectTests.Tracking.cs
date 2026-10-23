using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Seguimiento de ruta (2026-10-23): avisos, validaciones y «ruta en curso». Los puntos van por SQL crudo (GEOGRAPHY): con InMemory no se insertan, aquí se prueba el resto.</summary>
public partial class TripStatusEffectTests
{
    private static DriverTrackingService TrackingOf(Fixture f)
        => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), Options.Create(new DriverTrackingOptions()));

    [Fact]
    public void Config_returns_the_global_values()
    {
        var c = new DriverTrackingService(null!, null!, Options.Create(new DriverTrackingOptions { MinDistanceMeters = 200 })).Config();
        Assert.Equal((200, 90, 120, 10), (c.MinDistanceMeters, c.MaxIntervalSeconds, c.UploadEverySeconds, c.RetentionDays));
    }

    [Fact]
    public async Task Without_a_route_in_progress_only_the_notices_are_kept_and_a_resend_does_not_repeat_them()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, _, _) = await DispatchedAsync(f, 1);     // despachado, NO aceptado: la ruta no está en curso
        var svc = TrackingOf(f);
        var at = DateTime.UtcNow.AddMinutes(-1);
        var req = new DriverTrackingUploadRequest(new[] { new DriverTrackingPointDto(18.4, -66.1, DateTime.UtcNow.AddMinutes(-2), 30m, 90) }, new[] { new DriverTrackingEventDto("stopped_by_driver", at) });

        var r = await svc.UploadAsync(req, default);

        Assert.Equal((false, 0, 1, 1), (r.RouteActive, r.Accepted, r.Skipped, r.Events));
        Assert.Equal(("STOPPED_BY_DRIVER", (int?)null), ((await f.Db.DriverTrackingEvents.SingleAsync()).EventType, (await f.Db.DriverTrackingEvents.SingleAsync()).TripId));

        var again = await svc.UploadAsync(req, default);
        Assert.Equal(0, again.Events);
        Assert.Equal(1, await f.Db.DriverTrackingEvents.CountAsync());
    }

    [Fact]
    public async Task With_the_route_in_progress_the_upload_is_linked_to_it_and_bad_input_is_refused()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, _) = await DispatchedAsync(f, 1);
        await app.AcceptAsync(trip.PublicId, null, default);
        var svc = TrackingOf(f);

        var r = await svc.UploadAsync(new DriverTrackingUploadRequest(null, new[] { new DriverTrackingEventDto("STARTED", DateTime.UtcNow.AddMinutes(-5)) }), default);

        Assert.True(r.RouteActive);
        Assert.Equal(trip.TripId, (await f.Db.DriverTrackingEvents.SingleAsync()).TripId);

        var many = Enumerable.Range(0, 501).Select(i => new DriverTrackingPointDto(18.4, -66.1, DateTime.UtcNow.AddSeconds(-i), null, null)).ToList();
        Assert.Equal(DriverTrackingRules.TooManyPointsMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(new DriverTrackingUploadRequest(many, null), default))).Message);
        Assert.Equal(DriverTrackingRules.EventTypeInvalidMessage,
            (await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(new DriverTrackingUploadRequest(null, new[] { new DriverTrackingEventDto("NOPE", DateTime.UtcNow) }), default))).Message);
    }

    [Fact]
    public async Task A_user_that_is_not_a_driver_is_forbidden()
    {
        await using var f = await Fixture.CreateAsync();
        await DispatchedAsync(f, 1, linkDriver: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => TrackingOf(f).UploadAsync(new DriverTrackingUploadRequest(null, null), default));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Avisos del chofer (2026-10-23): el despacho deja «Nuevo manifiesto», el chofer solo ve los suyos y los marca leídos; la oficina escribe avisos.</summary>
public partial class TripStatusEffectTests
{
    private static DriverNoticeService NoticesOf(Fixture f) => new(f.Db, f.Services.GetRequiredService<ITenantContext>());

    [Fact]
    public async Task Dispatching_a_route_leaves_a_new_manifest_notice_for_its_driver_and_reading_it_clears_the_unread_count()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Drivers.Add(new Teikem.Domain.Fleet.Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 1, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Pickup, 1);

        await f.TransitionTripAsync(trip, TripStatuses.Dispatched);

        var svc = NoticesOf(f);
        var list = await svc.ListAsync(default);
        Assert.Equal(1, list.Unread);
        var n = Assert.Single(list.Items);
        Assert.Equal((DriverNoticeCodes.ManifestDispatched, false, trip.PublicId), (n.Kind, n.Read, n.TripPublicId));
        Assert.Contains(trip.Code, n.Title);

        Assert.Equal(1, await svc.MarkReadAsync(new DriverNoticesReadRequest(null, true), default));
        Assert.Equal(0, (await svc.ListAsync(default)).Unread);
        Assert.Equal(0, await svc.MarkReadAsync(new DriverNoticesReadRequest(null, true), default));
    }

    [Fact]
    public async Task The_office_can_send_a_notice_and_the_input_is_validated()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, _, _) = await DispatchedAsync(f, 1);
        var driver = await f.Db.Drivers.AsNoTracking().SingleAsync();
        var svc = NoticesOf(f);

        var sent = await svc.SendAsync(new DriverNoticeSendRequest(driver.PublicId, " Cambio de ruta ", " llame a la oficina "), default);

        Assert.Equal(("Cambio de ruta", "llame a la oficina", DriverNoticeCodes.Message), (sent.Title, sent.Body, sent.Kind));
        Assert.Equal(DriverNoticeRules.TitleRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(new DriverNoticeSendRequest(driver.PublicId, " ", null), default))).Message);
        Assert.Equal(DriverNoticeRules.TitleTooLongMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(new DriverNoticeSendRequest(driver.PublicId, new string('t', 121), null), default))).Message);
        Assert.Equal(DriverNoticeRules.BodyTooLongMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.SendAsync(new DriverNoticeSendRequest(driver.PublicId, "ok", new string('b', 1001)), default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.SendAsync(new DriverNoticeSendRequest(Guid.NewGuid(), "ok", null), default));
    }

    [Fact]
    public async Task A_user_that_is_not_a_driver_cannot_read_notices()
    {
        await using var f = await Fixture.CreateAsync();
        await DispatchedAsync(f, 1, linkDriver: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => NoticesOf(f).ListAsync(default));
    }
}

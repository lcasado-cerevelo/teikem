using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Avisos emergentes (2026-10-23): registro del teléfono y envío de los avisos pendientes (con un FCM falso).</summary>
public partial class TripStatusEffectTests
{
    private sealed class FakePush(PushOutcome outcome, bool configured = true) : IPushSender
    {
        public List<(string Token, string Title)> Sent { get; } = new();
        public bool IsConfigured => configured;
        public Task<PushOutcome> SendAsync(string deviceToken, string title, string? body, IReadOnlyDictionary<string, string>? data, CancellationToken ct)
        {
            Sent.Add((deviceToken, title));
            return Task.FromResult(outcome);
        }
    }

    private static DriverPushService PushOf(Fixture f, IPushSender sender) => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), sender);

    [Fact]
    public async Task Registering_the_phone_is_idempotent_and_validated()
    {
        await using var f = await Fixture.CreateAsync();
        await DispatchedAsync(f, 1);
        var svc = PushOf(f, new FakePush(PushOutcome.Sent));

        await svc.RegisterDeviceAsync(new DriverDeviceRegisterRequest("android", " tok-1 ", "1.0"), default);
        await svc.RegisterDeviceAsync(new DriverDeviceRegisterRequest("ANDROID", "tok-1", "1.1"), default);

        var d = await f.Db.DriverDevices.SingleAsync();
        Assert.Equal(("tok-1", "1.1", true), (d.PushToken, d.AppVersion, d.IsActive));
        Assert.Equal(DriverPushService.TokenRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.RegisterDeviceAsync(new DriverDeviceRegisterRequest("ANDROID", " ", null), default))).Message);
        Assert.Equal(DriverPushService.PlatformInvalidMessage, (await Assert.ThrowsAsync<ValidationException>(() => svc.RegisterDeviceAsync(new DriverDeviceRegisterRequest("WEB", "x", null), default))).Message);
    }

    [Fact]
    public async Task Pending_notices_are_pushed_once_and_a_dead_token_is_deactivated()
    {
        await using var f = await Fixture.CreateAsync();
        await DispatchedAsync(f, 1);
        var driver = await f.Db.Drivers.AsNoTracking().SingleAsync();
        await NoticesOf(f).SendAsync(new DriverNoticeSendRequest(driver.PublicId, "Hola", "texto"), default);
        var ok = new FakePush(PushOutcome.Sent);
        await PushOf(f, ok).RegisterDeviceAsync(new DriverDeviceRegisterRequest("ANDROID", "tok-1", null), default);

        var first = await PushOf(f, ok).DispatchPendingAsync(default);
        var second = await PushOf(f, ok).DispatchPendingAsync(default);

        Assert.Equal((1, 0, 0), first);
        Assert.Equal((0, 0, 0), second);                 // ya salió: no se repite
        Assert.Equal(new[] { ("tok-1", "Hola") }, ok.Sent);

        await NoticesOf(f).SendAsync(new DriverNoticeSendRequest(driver.PublicId, "Otro", null), default);
        var dead = new FakePush(PushOutcome.InvalidToken);
        await PushOf(f, dead).DispatchPendingAsync(default);
        Assert.False((await f.Db.DriverDevices.SingleAsync()).IsActive);   // la app se desinstaló
    }

    [Fact]
    public async Task Without_firebase_configured_nothing_is_sent_and_the_notice_stays_pending()
    {
        await using var f = await Fixture.CreateAsync();
        await DispatchedAsync(f, 1);
        var driver = await f.Db.Drivers.AsNoTracking().SingleAsync();
        await NoticesOf(f).SendAsync(new DriverNoticeSendRequest(driver.PublicId, "Hola", null), default);

        Assert.Equal((0, 0, 0), await PushOf(f, new FakePush(PushOutcome.Sent, configured: false)).DispatchPendingAsync(default));
        Assert.Null((await f.Db.DriverNotices.SingleAsync()).PushedAtUtc);
    }
}

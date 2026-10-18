using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 113 — «Devolver tras N intentos» (por omisión 2): solo una sugerencia y una marca, por compañía; 0 = sin sugerencia; nunca bloquea.</summary>
public class ReturnSuggestionTests
{
    [Theory]
    [InlineData(2, 2, "FAILED", true)]
    [InlineData(2, 3, "UNASSIGNED", true)]
    [InlineData(2, 1, "FAILED", false)]
    [InlineData(0, 9, "FAILED", false)]
    [InlineData(2, 5, "SUBMITTED", false)]
    [InlineData(2, 5, "DELIVERED", false)]
    [InlineData(2, 5, "RETURNED", false)]
    [InlineData(2, 5, "CANCELLED", false)]
    public void The_suggestion_needs_enough_attempts_a_live_order_and_a_threshold_above_zero(int after, int attempts, string status, bool expected)
        => Assert.Equal(expected, ReturnSuggestionRules.Suggest(after, attempts, status));

    [Fact]
    public void The_threshold_goes_from_zero_to_twenty_and_defaults_to_two()
    {
        Assert.Equal(2, ReturnSuggestionRules.DefaultAfterAttempts);
        Assert.True(ReturnSuggestionRules.IsValid(0));
        Assert.True(ReturnSuggestionRules.IsValid(20));
        Assert.False(ReturnSuggestionRules.IsValid(-1));
        Assert.False(ReturnSuggestionRules.IsValid(21));
        Assert.Equal(2, new Teikem.Domain.Tenancy.Tenant().ReturnSuggestAfterAttempts);
    }

    private static async Task AddAttemptsAsync(TripServiceFixture f, SeededOrder o, int failed)
    {
        for (var i = 1; i <= failed; i++)
            f.Db.Set<DeliveryAttempt>().Add(new DeliveryAttempt { TenantId = TripServiceFixture.TenantId, TransportOrderId = o.Id, AttemptNumber = i, AttemptedAtUtc = DateTime.UtcNow.AddHours(-i), Delivered = false });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<ScanActionResultDto> ScanAsync(TripServiceFixture f, SeededOrder o, string action = ScanActions.Arrive)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(action, TripServiceFixture.PieceNumberOf(o.Id, 1), TripServiceFixture.Today), default);

    [Fact]
    public async Task The_scan_of_an_order_with_two_failed_attempts_suggests_returning_it_without_blocking_anything()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Failed, pieces: 1);
        await AddAttemptsAsync(f, o, 2);

        var r = await ScanAsync(f, o);

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);                                  // el escaneo se hizo igual
        Assert.Equal((2, true), (r.FailedAttempts, r.SuggestReturn));
        Assert.EndsWith(ReturnSuggestionRules.Message(2), r.Message);
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(o.Id));
    }

    [Fact]
    public async Task With_one_attempt_or_with_the_suggestion_turned_off_nothing_is_suggested()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var one = await f.SeedOrderAsync(OrderStatuses.Failed, pieces: 1);
        await AddAttemptsAsync(f, one, 1);
        var r1 = await ScanAsync(f, one);
        Assert.Equal((1, false), (r1.FailedAttempts, r1.SuggestReturn));
        Assert.DoesNotContain("se sugiere devolverla", r1.Message);

        await f.Get<ReturnSuggestionService>().SetSettingsAsync(new ReturnSettingsRequest(0), default);
        var many = await f.SeedOrderAsync(OrderStatuses.Failed, pieces: 1);
        await AddAttemptsAsync(f, many, 5);
        var r2 = await ScanAsync(f, many);
        Assert.Equal(false, r2.SuggestReturn);
        Assert.DoesNotContain("se sugiere devolverla", r2.Message);
    }

    [Fact]
    public async Task A_voided_attempt_does_not_count()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Failed, pieces: 1);
        await AddAttemptsAsync(f, o, 2);
        var one = await f.Db.Set<DeliveryAttempt>().FirstAsync(a => a.TransportOrderId == o.Id);
        one.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var r = await ScanAsync(f, o);
        Assert.Equal((1, false), (r.FailedAttempts, r.SuggestReturn));
    }

    [Fact]
    public async Task The_setting_is_read_and_written_per_company_and_validated()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var svc = f.Get<ReturnSuggestionService>();
        Assert.Equal(2, (await svc.GetSettingsAsync(default)).SuggestAfterAttempts);
        Assert.Equal(3, (await svc.SetSettingsAsync(new ReturnSettingsRequest(3), default)).SuggestAfterAttempts);
        Assert.Equal(3, (await svc.GetSettingsAsync(default)).SuggestAfterAttempts);

        foreach (var bad in new int?[] { null, -1, 21 })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SetSettingsAsync(new ReturnSettingsRequest(bad), default));
            Assert.Equal(ReturnSuggestionRules.InvalidMessage, ex.Message);
        }
        Assert.Equal(3, (await svc.GetSettingsAsync(default)).SuggestAfterAttempts);
    }

    [Fact]
    public void Changing_the_setting_needs_admin_tenant_and_reading_it_only_a_session()
    {
        var t = typeof(ReturnSettingsController);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/v1/tenant/return-settings", t.GetCustomAttribute<RouteAttribute>()!.Template);
        var put = t.GetMethod(nameof(ReturnSettingsController.Put))!;
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant }, put.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        Assert.Empty(t.GetMethod(nameof(ReturnSettingsController.Get))!.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}

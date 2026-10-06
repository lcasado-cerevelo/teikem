using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Tarea 25 — conteo informado al capturar: reglas puras (CountRevealRules) y el servicio (CheckLineAsync) con el ledger y los permisos reales
/// (InMemory, CycleCountFixture): el contador ve lo esperado de una línea solo después de aceptarla; fuera del margen se le pide recontar sin decirle
/// el esperado; tras el reconteo (o una coincidencia) la línea se cierra y solo acepta la última cifra verificada.
/// </summary>
public sealed class CountRevealTests
{
    private const int Supervisor = 1;
    private const int Counter = 2;

    // ================================================================ reglas puras

    [Theory]
    [InlineData("NONE", null, false)]
    [InlineData("NONE", true, false)]
    [InlineData("MARKED", null, false)]
    [InlineData("MARKED", false, false)]
    [InlineData("MARKED", true, true)]
    [InlineData("ALL", null, true)]
    [InlineData("ALL", true, true)]
    [InlineData("ALL", false, false)]
    [InlineData(null, true, true)]    // sin modo = MARKED
    [InlineData("rara", null, false)] // desconocido = MARKED
    public void Counter_sees_expected_at_capture_per_the_matrix(string? mode, bool? userFlag, bool expected)
        => Assert.Equal(expected, CountRevealRules.CanRevealAtCapture(mode, userFlag));

    [Fact]
    public void View_is_full_for_the_supervisor_at_capture_for_an_allowed_counter_and_none_otherwise()
    {
        Assert.Equal(CountRevealViews.Full, CountRevealRules.ViewFor(true, false));
        Assert.Equal(CountRevealViews.Full, CountRevealRules.ViewFor(true, true));
        Assert.Equal(CountRevealViews.AtCapture, CountRevealRules.ViewFor(false, true));
        Assert.Equal(CountRevealViews.None, CountRevealRules.ViewFor(false, false));
    }

    [Theory]
    [InlineData(20, 20, 0, true)]
    [InlineData(20, 19, 0, false)]
    [InlineData(20, 19, 5, true)]    // 1 de 20 = 5 %
    [InlineData(20, 18, 5, false)]   // 2 de 20 = 10 %
    [InlineData(0, 1, 50, false)]    // esperado 0: cualquier cantidad es diferencia
    public void Tolerance_is_a_percentage_of_the_expected(double expected, double counted, double pct, bool within)
        => Assert.Equal(within, CountRevealRules.WithinTolerance((decimal)expected, (decimal)counted, (decimal)pct));

    [Fact]
    public void Next_state_is_match_or_recount_and_a_recount_always_ends_final()
    {
        Assert.Equal(CountCheckStates.Match, CountRevealRules.Next(null, true));
        Assert.Equal(CountCheckStates.Recount, CountRevealRules.Next(null, false));
        Assert.Equal(CountCheckStates.Final, CountRevealRules.Next(CountCheckStates.Recount, true));
        Assert.Equal(CountCheckStates.Final, CountRevealRules.Next(CountCheckStates.Recount, false));
        Assert.True(CountRevealRules.IsClosed(CountCheckStates.Match));
        Assert.True(CountRevealRules.IsClosed(CountCheckStates.Final));
        Assert.False(CountRevealRules.IsClosed(CountCheckStates.Recount));
        Assert.False(CountRevealRules.IsClosed(null));
    }

    [Fact]
    public void A_counter_can_only_save_the_verified_quantity_but_the_supervisor_can_change_it()
    {
        Assert.True(CountRevealRules.CanSaveChecked(null, 7m, false));
        Assert.True(CountRevealRules.CanSaveChecked(18m, 18m, false));
        Assert.False(CountRevealRules.CanSaveChecked(18m, 20m, false));
        Assert.True(CountRevealRules.CanSaveChecked(18m, 20m, true));
    }

    // ================================================================ servicio

    private static async Task<(CycleCountFixture F, CycleCountService Svc, CycleCountDetailDto Count, int LineId)> NewAsync(
        string mode = "MARKED", bool? userFlag = true, decimal tolerance = 0m, bool showsNumber = true, decimal onHand = 20m)
    {
        var f = await CycleCountFixture.CreateAsync();
        await f.AddUserAsync(Supervisor, "Supervisora");
        await f.AddUserAsync(Counter, "Contador");
        var t = await f.Db.Tenants.SingleAsync();
        t.CountExpectedReveal = mode;
        t.CountRecountTolerancePct = tolerance;
        t.CountRevealShowsNumber = showsNumber;
        f.Db.UserTenants.Add(new UserTenant { UserTenantId = 1, UserId = Counter, TenantId = CycleCountFixture.TenantId, StatusCodeId = 1, CountSeeExpected = userFlag });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, onHand);
        f.AsUser(Supervisor);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(ProductPublicIds: new[] { f.ProductNonePublicId }), default);
        f.AsRestrictedUser(Counter, PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCountCapture);
        return (f, svc, created, created.Lines.Single().Id);
    }

    [Fact]
    public async Task Mismatch_asks_for_a_recount_without_revealing_the_expected_and_the_recount_closes_the_line()
    {
        var (f, svc, count, lineId) = await NewAsync();
        await using var _ = f;

        var first = await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(18m), default);
        Assert.Equal(CountCheckStates.Recount, first.State);
        Assert.False(first.Matches);
        Assert.Null(first.ExpectedQty);

        var second = await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(19m), default);
        Assert.Equal(CountCheckStates.Final, second.State);
        Assert.False(second.Matches);
        Assert.Equal(20m, second.ExpectedQty);

        var line = await f.Db.Set<CycleCountLine>().AsNoTracking().SingleAsync(l => l.CycleCountLineId == lineId);
        Assert.Equal(CountCheckStates.Final, line.CheckState);
        Assert.Equal(18m, line.FirstCheckQty);
        Assert.Equal(19m, line.LastCheckQty);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(20m), default));
        Assert.Equal(CountRevealRules.LineLocked, ex.Message);
    }

    [Fact]
    public async Task A_match_closes_the_line_and_the_counter_cannot_save_another_quantity_after_seeing_the_result()
    {
        var (f, svc, count, lineId) = await NewAsync();
        await using var _ = f;

        var check = await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(20m), default);
        Assert.Equal(CountCheckStates.Match, check.State);
        Assert.True(check.Matches);
        Assert.Equal(20m, check.ExpectedQty);

        // cambiar la cifra después de ver el resultado no se permite…
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CaptureAsync(count.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(lineId, 25m) }), default));
        Assert.Equal(CountRevealRules.CheckedQtyMismatch, ex.Message);
        // …guardar la verificada sí
        var saved = await svc.CaptureAsync(count.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(lineId, 20m) }), default);
        Assert.Equal(20m, saved.Lines.Single().CountedQty);
    }

    [Fact]
    public async Task The_supervisor_can_still_change_a_checked_line()
    {
        var (f, svc, count, lineId) = await NewAsync();
        await using var _ = f;
        await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(20m), default);
        f.AsUser(Supervisor);
        var saved = await svc.CaptureAsync(count.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(lineId, 22m) }), default);
        Assert.Equal(22m, saved.Lines.Single().CountedQty);
    }

    [Fact]
    public async Task The_tolerance_lets_a_close_count_match()
    {
        var (f, svc, count, lineId) = await NewAsync(tolerance: 5m);
        await using var _ = f;
        var check = await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(19m), default); // 1 de 20 = 5 %
        Assert.Equal(CountCheckStates.Match, check.State);
        Assert.True(check.Matches);
    }

    [Fact]
    public async Task With_numbers_off_the_expected_is_never_returned()
    {
        var (f, svc, count, lineId) = await NewAsync(showsNumber: false);
        await using var _ = f;
        await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(18m), default);
        var second = await svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(19m), default);
        Assert.Equal(CountCheckStates.Final, second.State);
        Assert.False(second.Matches);
        Assert.Null(second.ExpectedQty);
    }

    [Theory]
    [InlineData("NONE", true)]      // la compañía lo cierra aunque el usuario esté marcado
    [InlineData("MARKED", null)]    // sin marcar
    [InlineData("MARKED", false)]
    [InlineData("ALL", false)]      // todos, salvo el marcado con No
    public async Task A_counter_who_may_not_see_the_expected_gets_403(string mode, bool? userFlag)
    {
        var (f, svc, count, lineId) = await NewAsync(mode, userFlag);
        await using var _ = f;
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(20m), default));
        Assert.Equal(CountRevealRules.NotAllowed, ex.Message);
        Assert.Equal(CountRevealViews.None, await svc.RevealViewAsync(default));
    }

    [Fact]
    public async Task Reveal_view_depends_on_the_permission_and_on_the_settings()
    {
        var (f, svc, _, _) = await NewAsync("ALL", null);
        await using var _f = f;
        Assert.Equal(CountRevealViews.AtCapture, await svc.RevealViewAsync(default));
        f.AsRestrictedUser(Supervisor, PermissionCatalog.InventoryView, PermissionCatalog.WarehouseCount);
        Assert.Equal(CountRevealViews.Full, await svc.RevealViewAsync(default));
    }

    [Fact]
    public async Task Bad_quantities_and_closed_counts_are_rejected()
    {
        var (f, svc, count, lineId) = await NewAsync();
        await using var _ = f;
        await Assert.ThrowsAsync<ValidationException>(() => svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(-1m), default));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(1.2345m), default));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CheckLineAsync(count.Count.Id, lineId, new CountLineCheckRequest(null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.CheckLineAsync(count.Count.Id, 9999, new CountLineCheckRequest(1m), default));
    }
}

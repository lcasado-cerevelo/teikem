using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Conteo de una posición hecho en el aparato SIN señal y aplicado al volver (2026-10-11, POST /cycle-counts/offline-bin): abre o retoma el conteo de la posición,
/// captura lo contado y, si es suyo, lo termina. Decisiones del dueño: si la posición ya tiene un conteo abierto de OTRO contador, las líneas se SUMAN a ese conteo
/// (no se termina); la existencia contra la que se compara es la del servidor al reconciliar y, si hubo movimientos entre contar y sincronizar, se avisa.
/// </summary>
public class CountOfflineTests
{
    private static CountBatchItem Line(CycleCountFixture f, int bin, Guid product, decimal qty) => new(BinId: bin, ProductPublicId: product, CountedQty: qty);

    private static async Task<string?[]> CommentsAsync(CycleCountFixture f, int countId)
    {
        var entity = f.LookupId(LookupDomains.EntityType, EntityTypes.CycleCount);
        return await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == entity && h.EntityId == countId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.Comment).ToArrayAsync();
    }

    [Fact]
    public async Task Without_an_open_count_it_opens_one_captures_what_was_counted_and_finishes_it()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 5m);
        var svc = f.Get<CycleCountService>();

        var result = await svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 5m) }), default);

        Assert.True(result.Created);
        Assert.False(result.Joined);
        Assert.True(result.Finished);
        Assert.False(result.MovedSinceCounted);
        Assert.Null(result.FinishMessage);
        Assert.Equal(CycleCountStatuses.Counted, result.Count.Count.StatusCode);
        Assert.Equal(5m, result.Count.Lines.Single().CountedQty);
        Assert.Equal(new[] { null, CycleCountRules.OfflineFinishComment }, await CommentsAsync(f, result.Count.Count.Id));
    }

    [Fact]
    public async Task An_empty_position_counted_with_something_found_opens_a_count_with_that_line()
    {
        await using var f = await CycleCountFixture.CreateAsync();   // el sistema cree que la posición está vacía
        var svc = f.Get<CycleCountService>();

        var result = await svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin2, Lines: new[] { Line(f, f.PickBin2, f.ProductNonePublicId, 3m) }), default);

        Assert.True(result.Created);
        Assert.True(result.Finished);
        var line = result.Count.Lines.Single();
        Assert.Equal(f.PickBin2, line.BinId);
        Assert.Equal(0m, line.SystemQty);
        Assert.Equal(3m, line.CountedQty);
    }

    [Fact]
    public async Task It_resumes_the_count_that_the_same_user_already_has_open_on_that_position()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var opened = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, AssignToMe: true), default);

        var result = await svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 7m) }), default);

        Assert.False(result.Created);
        Assert.False(result.Joined);
        Assert.Equal(opened.Count.Id, result.Count.Count.Id);
        Assert.True(result.Finished);
        Assert.Equal(1, await f.Db.CycleCounts.CountAsync());   // no abrió otro
    }

    [Fact]
    public async Task Another_users_open_count_gets_the_lines_added_and_is_left_open_for_its_owner()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var theirs = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, AssignToMe: true), default);
        await f.AssignCountTaskAsync(theirs.Count.Id, userId: 2, fullName: "Otro contador");   // lo tiene otro usuario; el que sincroniza es el 1

        var result = await svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 6m) }), default);

        Assert.False(result.Created);
        Assert.True(result.Joined);
        Assert.False(result.Finished);          // no se termina el conteo de otro
        Assert.Null(result.FinishMessage);
        Assert.Equal(theirs.Count.Id, result.Count.Count.Id);
        Assert.Equal(CycleCountStatuses.Open, result.Count.Count.StatusCode);
        Assert.Equal(6m, result.Count.Lines.Single().CountedQty);
        Assert.Equal(1, await f.Db.CycleCounts.CountAsync());
    }

    [Fact]
    public async Task When_the_count_cannot_be_finished_what_was_counted_is_kept_and_the_reason_comes_back()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        var lot = await f.AddLotAsync(f.ProductLotId, "L-1");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductLotId, f.PickBin1, 4m, lot);   // el servidor también tiene este producto en la posición…
        var svc = f.Get<CycleCountService>();

        // …pero el aparato (con datos viejos) solo contó el otro: la línea del lote queda sin contar y el conteo no se puede terminar.
        var result = await svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 8m) }), default);

        Assert.True(result.Created);
        Assert.False(result.Finished);
        Assert.Equal(CycleCountRules.CountIncomplete(1), result.FinishMessage);
        Assert.Equal(CycleCountStatuses.Open, result.Count.Count.StatusCode);
        Assert.Contains(result.Count.Lines, l => l.CountedQty == 8m);   // lo contado quedó guardado
    }

    [Fact]
    public async Task Movements_after_the_device_counted_are_flagged_in_the_result_and_in_the_history()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var countedAt = DateTime.UtcNow.AddMinutes(-5);
        await Task.Delay(20);
        await f.IssueAsync(f.ProductNoneId, f.PickBin1, 2m);        // alguien movió mercancía DESPUÉS de que el aparato contó

        var result = await f.Get<CycleCountService>().CaptureOfflineAsync(
            new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 8m) }, CountedAtUtc: countedAt), default);

        Assert.True(result.MovedSinceCounted);
        Assert.True(result.Finished);
        Assert.Equal(CycleCountRules.OfflineFinishMovedComment, (await CommentsAsync(f, result.Count.Count.Id)).Last());
    }

    [Fact]
    public async Task Without_movements_since_counting_there_is_no_flag_and_a_device_clock_ahead_is_clamped()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);

        var result = await f.Get<CycleCountService>().CaptureOfflineAsync(
            new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 8m) }, CountedAtUtc: DateTime.UtcNow.AddDays(3)), default);

        Assert.False(result.MovedSinceCounted);
        Assert.Equal(CycleCountRules.OfflineFinishComment, (await CommentsAsync(f, result.Count.Count.Id)).Last());
    }

    [Fact]
    public async Task Without_finish_the_count_stays_open_after_capturing()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);

        var result = await f.Get<CycleCountService>().CaptureOfflineAsync(
            new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 8m) }, Finish: false), default);

        Assert.False(result.Finished);
        Assert.Equal(CycleCountStatuses.Open, result.Count.Count.StatusCode);
        Assert.Equal(8m, result.Count.Lines.Single().CountedQty);
    }

    [Fact]
    public async Task Bad_requests_are_rejected_before_touching_anything()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();

        var noBin = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureOfflineAsync(new CountOfflineRequest(Lines: new[] { Line(f, f.PickBin1, f.ProductNonePublicId, 1m) }), default));
        Assert.Equal(CycleCountRules.BinRequired, noBin.Errors!["binId"].Single());

        var noLines = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: Array.Empty<CountBatchItem>()), default));
        Assert.Equal(CycleCountRules.CaptureRequired, noLines.Errors!["lines"].Single());

        var otherBin = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { Line(f, f.PickBin2, f.ProductNonePublicId, 1m) }), default));
        Assert.Equal(CycleCountRules.OfflineLineWrongBin, otherBin.Errors!["lines[0].binId"].Single());

        var byLineId = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureOfflineAsync(new CountOfflineRequest(BinId: f.PickBin1, Lines: new[] { new CountBatchItem(LineId: 5, CountedQty: 1m) }), default));
        Assert.Equal(CycleCountRules.OfflineLineWrongBin, byLineId.Errors!["lines[0].binId"].Single());

        Assert.Equal(0, await f.Db.CycleCounts.CountAsync());   // nada se abrió
    }
}

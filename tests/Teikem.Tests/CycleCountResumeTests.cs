using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-07 (app de almacén): escanear una posición que ya tiene un conteo abierto lo RETOMA (resumeOpen) en vez de abrir otro; si lo tiene otro
/// contador, 409 con su nombre. Sin resumeOpen (la web) el comportamiento de siempre.
/// </summary>
public sealed class CycleCountResumeTests
{
    private static CycleCountCreateRequest ForBin(int bin, bool resume) => new(BinIds: new[] { bin }, AssignToMe: true, ResumeOpen: resume);

    [Fact]
    public async Task Scanning_the_same_bin_again_resumes_the_open_count_with_what_was_already_counted()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductLotId, f.PickBin1, 3m, await f.AddLotAsync(f.ProductLotId, "L1"));
        var svc = f.Get<CycleCountService>();

        var first = await svc.CreateAsync(ForBin(f.PickBin1, true), default);
        Assert.False(first.Resumed);
        var line = first.Lines.First();
        await svc.CaptureAsync(first.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, 5m) }), default);

        var again = await svc.CreateAsync(ForBin(f.PickBin1, true), default);

        Assert.True(again.Resumed);
        Assert.Equal(first.Count.Id, again.Count.Id);
        Assert.Equal(5m, again.Lines.Single(l => l.Id == line.Id).CountedQty);
        Assert.Equal(1, again.Count.CountedLines);
        Assert.Single(f.Db.Set<CycleCount>().Where(c => c.IsActive).ToList());
    }

    [Fact]
    public async Task Without_resumeOpen_a_second_count_is_created_like_before()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var first = await svc.CreateAsync(ForBin(f.PickBin1, false), default);
        var second = await svc.CreateAsync(ForBin(f.PickBin1, false), default);
        Assert.NotEqual(first.Count.Id, second.Count.Id);
    }

    [Fact]
    public async Task A_bin_counted_by_someone_else_answers_409_with_their_name()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.AddUserAsync(7, "Ana Ruiz");
        var svc = f.Get<CycleCountService>();
        f.AsUser(7);
        var theirs = await svc.CreateAsync(ForBin(f.PickBin1, true), default);

        f.AsUser(1);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(ForBin(f.PickBin1, true), default));
        Assert.Equal($"Esa posición la está contando Ana Ruiz ({theirs.Count.Number}).", ex.Message);
    }

    [Fact]
    public async Task An_unassigned_open_count_is_taken_by_whoever_resumes_it()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);   // como lo crea la web: sin asignar
        Assert.Null(created.Count.AssignedToUserId);

        var resumed = await svc.CreateAsync(ForBin(f.PickBin1, true), default);

        Assert.True(resumed.Resumed);
        Assert.Equal(created.Count.Id, resumed.Count.Id);
        Assert.Equal(1, resumed.Count.AssignedToUserId);
    }

    [Fact]
    public async Task A_count_of_several_bins_or_already_finished_is_not_resumed()
    {
        await using var f = await CycleCountFixture.CreateAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();
        var multi = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1, f.PickBin2 }, AssignToMe: true), default);

        var single = await svc.CreateAsync(ForBin(f.PickBin1, true), default);
        Assert.False(single.Resumed);
        Assert.NotEqual(multi.Count.Id, single.Count.Id);

        // Terminado (todas las líneas contadas): el siguiente escaneo abre uno nuevo.
        await svc.CaptureAsync(single.Count.Id, new CountCaptureRequest(single.Lines.Select(l => new CountCaptureItem(l.Id, 1m)).ToList()), default);
        await svc.FinishAsync(single.Count.Id, null, default);
        var next = await svc.CreateAsync(ForBin(f.PickBin1, true), default);
        Assert.False(next.Resumed);
        Assert.NotEqual(single.Count.Id, next.Count.Id);
    }
}

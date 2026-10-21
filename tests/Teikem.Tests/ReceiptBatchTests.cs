using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 174 (C2) — recibo con tandas: reglas puras (el neto de tandas + cierre = lo recibido) y servicio sobre InMemory.</summary>
public sealed class ReceiptBatchTests
{
    private static decimal Net(IEnumerable<PlannedReceiptPosting> p) => p.Sum(x => x.SignedQuantity);

    [Theory]
    [InlineData(true, 10, new[] { 4, 3, 5 })]    // se recibe más de lo esperado
    [InlineData(true, 10, new[] { 4, 3 })]       // se recibe menos
    [InlineData(true, 10, new[] { 10 })]
    [InlineData(true, null, new[] { 2, 2 })]     // línea extra
    [InlineData(false, 10, new[] { 6, 1 })]      // ciego
    public void Batches_plus_close_net_equals_received(bool expects, int? expected, int[] deltas)
    {
        decimal? e = expected;
        decimal posted = 0m, net = 0m;
        foreach (var d in deltas)
        {
            net += Net(ReceiptBatchRules.PlanBatch(expects, e, posted, d));
            posted += d;
        }
        var received = posted;   // el cierre sin unidades nuevas
        net += Net(ReceiptBatchRules.PlanClose(expects, e, posted, received, TrackingTypes.None));
        Assert.Equal(received, net);
    }

    [Fact]
    public void Close_without_batches_is_exactly_the_legacy_plan()
    {
        var legacy = ReceiptPostingRules.Plan(true, 10m, 7m, TrackingTypes.None);
        var close = ReceiptBatchRules.PlanClose(true, 10m, 0m, 7m, TrackingTypes.None);
        Assert.Equal(legacy, close);
    }

    [Fact]
    public void Batch_over_expected_posts_the_excess_as_variance_and_the_shortage_is_never_posted_in_a_batch()
    {
        var first = ReceiptBatchRules.PlanBatch(true, 10m, 0m, 12m);
        Assert.Equal(2, first.Count);
        Assert.Equal((InventoryTxnTypes.Receipt, 10m), (first[0].TxnType, first[0].Quantity));
        Assert.Equal((InventoryTxnTypes.Adjustment, 2m), (first[1].TxnType, first[1].Quantity));
        var part = ReceiptBatchRules.PlanBatch(true, 10m, 0m, 4m);
        Assert.Equal(4m, Assert.Single(part).Quantity);
        Assert.Empty(ReceiptBatchRules.PlanBatch(true, 10m, 4m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptBatchRules.PlanBatch(true, 10m, 4m, -1m));
    }

    [Fact]
    public void Serial_damage_and_crossdock_lines_do_not_enter_a_batch()
    {
        Assert.Equal(ReceiptBatchRules.ReasonSerial, ReceiptBatchRules.NotBatchableReason(TrackingTypes.Serial, 0m, false));
        Assert.Equal(ReceiptBatchRules.ReasonDamage, ReceiptBatchRules.NotBatchableReason(TrackingTypes.None, 1m, false));
        Assert.Equal(ReceiptBatchRules.ReasonCrossDock, ReceiptBatchRules.NotBatchableReason(TrackingTypes.Lot, 0m, true));
        Assert.Null(ReceiptBatchRules.NotBatchableReason(TrackingTypes.Lot, 0m, false));
    }

    [Fact]
    public async Task Two_batches_then_the_final_confirmation_post_each_unit_once_and_keep_the_receipt_open_until_the_end()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 5m) }), default);
        var id = created.Header.PublicId;
        var lineId = created.Lines[0].Id;

        var one = await receipts.ConfirmBatchAsync(id, new ReceiptConfirmRequest("Primer camión"), default);
        Assert.True(one.Header.IsOpen);
        Assert.Equal(5m, one.Lines[0].PostedQty);
        var b1 = Assert.Single(one.Batches!);
        Assert.Equal((1, 1, 5m, "Primer camión"), (b1.Number, b1.LineCount, b1.UnitCount, b1.Comment));
        Assert.False(one.CanDelete);
        Assert.Equal(5m, (await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync()).Sum(t => t.Quantity));

        // sin unidades nuevas no hay tanda
        await Assert.ThrowsAsync<StatusRuleException>(() => receipts.ConfirmBatchAsync(id, null, default));
        // no se baja lo recibido por debajo de lo confirmado ni se quita la línea
        await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(id, lineId, new ReceiptLineUpdateRequest(ReceivedQty: 4m), default));
        await Assert.ThrowsAsync<ConflictException>(() => receipts.RemoveLineAsync(id, lineId, default));
        await Assert.ThrowsAsync<ConflictException>(() => receipts.DeleteAsync(id, default));

        await receipts.UpdateLineAsync(id, lineId, new ReceiptLineUpdateRequest(ReceivedQty: 8m), default);
        var two = await receipts.ConfirmBatchAsync(id, null, default);
        Assert.Equal(2, two.Batches!.Count);
        Assert.Equal(3m, two.Batches[1].UnitCount);
        Assert.Equal(8m, (await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync()).Sum(t => t.Quantity));

        // cierre: nada nuevo que asentar, el recibo queda confirmado y espera el acomodo de las dos tandas
        var done = await receipts.ConfirmAsync(id, null, default);
        Assert.False(done.Header.IsOpen);
        Assert.Equal(ReceiptStatuses.Received, done.Header.StatusCode);
        Assert.Equal(8m, (await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync()).Sum(t => t.Quantity));
        Assert.Equal(new[] { 5m, 3m }, done.PutawayTasks.Select(t => t.Quantity!.Value).ToArray());
    }

    [Fact]
    public async Task Final_confirmation_posts_only_the_rest_after_a_batch()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m) }), default);
        var id = created.Header.PublicId;
        await receipts.ConfirmBatchAsync(id, null, default);
        await receipts.UpdateLineAsync(id, created.Lines[0].Id, new ReceiptLineUpdateRequest(ReceivedQty: 6m), default);

        var done = await receipts.ConfirmAsync(id, null, default);

        Assert.Equal(ReceiptStatuses.Received, done.Header.StatusCode);
        Assert.Equal(6m, (await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync()).Sum(t => t.Quantity));
        Assert.Equal(new[] { 4m, 2m }, done.PutawayTasks.Select(t => t.Quantity!.Value).ToArray());
    }

    [Fact]
    public async Task Parts_from_devices_add_up_on_the_same_open_receipt_are_idempotent_and_the_final_confirmation_closes_it()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 10m) }), default);
        var receipts = f.Get<ReceiptService>();
        ReceiptPartRequest Part(decimal qty, Guid key) => new(AsnId: asn.Id, Lines: new[] { new ReceiptLineRequest(f.ProductClientPublicId, qty) }, ClientKey: key);
        decimal Stock() => f.Db.Set<InventoryTransaction>().AsNoTracking().ToList().Sum(t => t.Quantity);

        var k1 = Guid.NewGuid();
        var one = await receipts.ReceivePartAsync(Part(4m, k1), default);
        Assert.True(one.Header.IsOpen);
        Assert.Equal((4m, 4m), (one.Lines[0].ReceivedQty, one.Lines[0].PostedQty));
        Assert.Equal(4m, Stock());

        // el mismo envío otra vez (reintento de la cola) no suma de nuevo
        var again = await receipts.ReceivePartAsync(Part(4m, k1), default);
        Assert.Equal(one.Header.PublicId, again.Header.PublicId);
        Assert.Single(again.Batches!);
        Assert.Equal(4m, Stock());

        // otro aparato aporta otra parte al mismo recibo
        var two = await receipts.ReceivePartAsync(Part(3m, Guid.NewGuid()), default);
        Assert.Equal(one.Header.PublicId, two.Header.PublicId);
        Assert.Equal(2, two.Batches!.Count);
        Assert.Equal(7m, two.Lines[0].ReceivedQty);
        Assert.Equal(7m, Stock());

        // Lote 186: el aviso muestra lo ya recibido y queda «parcial» mientras no se cierre
        var partial = await f.Get<AsnService>().GetAsync(asn.Id, InventoryScope.Any, default);
        Assert.True(partial.PartiallyReceived);
        Assert.Equal(7m, partial.PostedQty);
        Assert.Equal(7m, partial.Lines[0].PostedQty);

        // sin series ni daño en una parte
        var serial = await Assert.ThrowsAsync<ValidationException>(() => receipts.ReceivePartAsync(
            new ReceiptPartRequest(AsnId: asn.Id, Lines: new[] { new ReceiptLineRequest(f.ProductClientPublicId, 1m, SerialNumbers: new[] { "S1" }) }), default));
        Assert.Contains(ReceiptBatchRules.PartNoSerials, serial.Errors!["lines[0].serialNumbers"]);
        var damage = await Assert.ThrowsAsync<ValidationException>(() => receipts.ReceivePartAsync(
            new ReceiptPartRequest(AsnId: asn.Id, Lines: new[] { new ReceiptLineRequest(f.ProductClientPublicId, 1m, DamagedQty: 1m) }), default));
        Assert.True(damage.Errors!.ContainsKey("lines[0].damagedQty"));

        // la confirmación final del aparato (POST /receipts con confirm) cierra ese recibo con lo que falta
        var done = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id,
            Lines: new[] { new ReceiptLineRequest(f.ProductClientPublicId, 3m) }, Confirm: true), default);
        Assert.Equal(one.Header.PublicId, done.Header.PublicId);
        Assert.Equal(ReceiptStatuses.Received, done.Header.StatusCode);
        Assert.Equal(10m, done.Lines[0].ReceivedQty);
        Assert.Equal(10m, Stock());
        Assert.Equal(new[] { 4m, 3m, 3m }, done.PutawayTasks.Select(t => t.Quantity!.Value).ToArray());
    }
}

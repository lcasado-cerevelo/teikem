using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 194 — al confirmar un recibo (completo o por tanda) se avisa a los ganchos de «entró mercancía» (reservar el pendiente de los avisos de salida).</summary>
public class StockArrivalHookTests
{
    private sealed class CountingHook : IStockArrivalHook
    {
        public int Calls;
        public Task OnStockArrivedAsync(CancellationToken ct) { Calls++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Confirming_a_receipt_and_a_batch_notifies_the_hooks()
    {
        var hook = new CountingHook();
        await using var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<IPurchaseOrderReceiving, ReceivingFakePurchaseOrders>();
            s.AddSingleton<IReceiptConfirmationParticipant, ReceivingFakeCrossDock>();
            s.AddSingleton<IStockArrivalHook>(hook);
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
            s.AddReceiptDamageDeps();
        });
        var w = await f.AddWarehouseAsync("W1");
        await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        var p = await f.AddProductAsync("SKU-1");
        var receipts = f.Get<ReceiptService>();

        var batchOne = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: w.PublicId, Type: ReceiptTypes.Blind, Lines: new[] { new ReceiptLineRequest(p.PublicId, 3m) }), default);
        Assert.Equal(0, hook.Calls);   // crear sin confirmar no mueve inventario
        await receipts.ConfirmBatchAsync(batchOne.Header.PublicId, null, default);
        Assert.Equal(1, hook.Calls);
        await receipts.ConfirmAsync(batchOne.Header.PublicId, null, default);
        Assert.Equal(2, hook.Calls);
        await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: w.PublicId, Type: ReceiptTypes.Blind, Lines: new[] { new ReceiptLineRequest(p.PublicId, 2m) }, Confirm: true), default);
        Assert.Equal(3, hook.Calls);
    }
}

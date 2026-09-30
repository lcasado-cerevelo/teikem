using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P2, D14) — InventoryReconciliationWorker sobre InMemory con el ledger y la conciliación reales (WmsFixture). Un
/// descuadre se fuerza escribiendo el saldo fuera del ledger (nunca en src/). Cubre: el worker abre el descuadre con origen
/// EVENT y el movimiento que lo disparó, como el tenant SIN usuario ("Sistema") y devolviendo el contexto como estaba; lo cierra
/// solo cuando vuelve a cuadrar; agrupa por tenant; un producto recién revisado espera MinRecheckSeconds (no se pierde); un
/// error de un tenant no corta el ciclo ni se ve en otro; y el ciclo real (ExecuteAsync) consume, se detiene y deja de aceptar.
/// </summary>
public class InventoryReconciliationWorkerTests
{
    /// <summary>Efecto de estatus de descuadres que falla mientras ShouldFail diga que sí (p. ej. solo para un tenant).</summary>
    private sealed class FailingEffect : IStatusTransitionEffect
    {
        public Func<bool>? ShouldFail { get; set; }
        public string StatusDomain => StatusDomains.InventoryDiscrepancyStatus;
        public Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
            => ShouldFail?.Invoke() == true ? throw new InvalidOperationException("Falla simulada de la revisión.") : Task.CompletedTask;
    }

    private sealed record World(WmsFixture F, InventoryReconciliationQueue Queue, InventoryReconciliationWorker Worker, FailingEffect Effect,
        Warehouse W, WarehouseBin B, Product P, Product Q);

    private static async Task<World> SeedAsync(InventoryReconciliationOptions? options = null)
    {
        var effect = new FailingEffect();
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<InventoryReconciler>();
            s.AddSingleton<InventoryReconciliationService>();
            s.AddSingleton<IStatusTransitionEffect>(effect);
        });
        var queue = new InventoryReconciliationQueue(options ?? new InventoryReconciliationOptions { DebounceMs = 0 });
        var worker = new InventoryReconciliationWorker(queue, f.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InventoryReconciliationWorker>.Instance);
        var w = await f.AddWarehouseAsync("ALM-01");
        var b = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "A-01");
        var p = await f.AddProductAsync("SKU-P");
        var q = await f.AddProductAsync("SKU-Q");
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, q.ProductId, 4m, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId));
        f.Changes.Clear();
        return new World(f, queue, worker, effect, w, b, p, q);
    }

    /// <summary>Escritura directa del saldo (fuera del ledger): así nace un descuadre.</summary>
    private static async Task SetOnHandAsync(WmsFixture f, int productId, int binId, decimal onHand)
    {
        var b = await f.Db.StockBalances.SingleAsync(x => x.ProductId == productId && x.WarehouseBinId == binId);
        b.QtyOnHand = onHand;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Task<List<InventoryDiscrepancy>> DiscrepanciesAsync(WmsFixture f)
        => f.Db.InventoryDiscrepancies.AsNoTracking().OrderBy(d => d.InventoryDiscrepancyId).ToListAsync();

    [Fact]
    public async Task The_worker_opens_the_discrepancy_as_the_tenant_without_user_and_closes_it_when_it_balances()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        w.Queue.StartConsuming();
        await SetOnHandAsync(w.F, w.P.ProductId, w.B.WarehouseBinId, 11m);   // Kárdex 10, saldo 11

        var t0 = DateTime.UtcNow;
        Assert.True(w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId, w.Q.ProductId }, 501)));
        Assert.Equal(2, w.Queue.Status(WmsFixture.TenantId).Pending);
        Assert.Equal(1, w.Worker.Drain(t0));
        Assert.Equal(2, w.Queue.Status(WmsFixture.TenantId).Pending);   // esperando en el worker
        Assert.Equal(2, await w.Worker.ProcessDueAsync(t0, default));

        var d = Assert.Single(await DiscrepanciesAsync(w.F));
        Assert.Equal(w.F.LookupId(LookupDomains.ReconciliationTrigger, ReconciliationTriggers.Event), d.TriggerLookupId);
        Assert.Equal((501L, 10m, 11m), (d.LastTxnId!.Value, d.LedgerQty, d.BalanceQty));
        Assert.Equal(w.F.StatusId(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Open), d.StatusCodeId);
        var typeId = w.F.LookupId(LookupDomains.EntityType, EntityTypes.InventoryDiscrepancy);
        var history = Assert.Single(await w.F.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == d.InventoryDiscrepancyId).ToListAsync());
        Assert.Null(history.ChangedBy);   // "Sistema": la revisión corre como el tenant sin usuario
        Assert.Equal((WmsFixture.TenantId, 1), (w.F.Tenant.TenantId!.Value, w.F.Tenant.UserId!.Value));   // contexto restaurado

        var status = w.Queue.Status(WmsFixture.TenantId);
        Assert.Equal((0, 2L, 0L), (status.Pending, status.Processed, status.Failed));
        Assert.NotNull(status.LastProcessedAtUtc);
        Assert.Null(status.LastError);

        // Vuelve a cuadrar y llega otro aviso: pasado MinRecheckSeconds se revisa y se cierra solo.
        await SetOnHandAsync(w.F, w.P.ProductId, w.B.WarehouseBinId, 10m);
        w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 502));
        w.Worker.Drain(t0.AddSeconds(11));
        Assert.Equal(1, await w.Worker.ProcessDueAsync(t0.AddSeconds(11), default));
        var closed = Assert.Single(await DiscrepanciesAsync(w.F));
        Assert.Equal(w.F.StatusId(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.SelfCorrected), closed.StatusCodeId);
        Assert.NotNull(closed.ClosedAtUtc);
    }

    [Fact]
    public async Task A_product_checked_moments_ago_waits_for_the_recheck_window_and_is_not_lost()
    {
        var w = await SeedAsync(new InventoryReconciliationOptions { DebounceMs = 0, MinRecheckSeconds = 10 });
        await using var _ = w.F;
        w.Queue.StartConsuming();
        var t0 = DateTime.UtcNow;
        w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 1));
        w.Worker.Drain(t0);
        Assert.Equal(1, await w.Worker.ProcessDueAsync(t0, default));
        Assert.Null(w.Worker.NextDueUtc());

        // Tres avisos más del mismo producto un segundo después: se juntan y esperan hasta t0 + 10 s.
        await SetOnHandAsync(w.F, w.P.ProductId, w.B.WarehouseBinId, 12m);
        for (var i = 0; i < 3; i++) w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 10 + i));
        Assert.Equal(3, w.Worker.Drain(t0.AddSeconds(1)));
        Assert.Equal(0, await w.Worker.ProcessDueAsync(t0.AddSeconds(1), default));
        Assert.Equal(t0.AddSeconds(10), w.Worker.NextDueUtc());
        Assert.Equal(1, w.Queue.Status(WmsFixture.TenantId).Pending);
        Assert.Empty(await DiscrepanciesAsync(w.F));

        Assert.Equal(1, await w.Worker.ProcessDueAsync(t0.AddSeconds(10), default));
        var d = Assert.Single(await DiscrepanciesAsync(w.F));
        Assert.Equal(12L, d.LastTxnId);   // el mayor movimiento de los avisos juntados
        Assert.Equal(0, w.Queue.Status(WmsFixture.TenantId).Pending);
    }

    [Fact]
    public async Task An_error_in_one_tenant_is_logged_and_the_cycle_goes_on()
    {
        var w = await SeedAsync(new InventoryReconciliationOptions { DebounceMs = 0, MinRecheckSeconds = 0 });
        await using var _ = w.F;
        w.Queue.StartConsuming();

        // Tenant 2 con su propio descuadre.
        int otherProduct, otherBin;
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var w2 = await w.F.AddWarehouseAsync("ALM-02", WmsFixture.OtherTenantId);
            var b2 = await w.F.AddBinAsync(await w.F.AddZoneAsync(w2, "PCK", ZoneTypes.Picking), "B-01");
            var p2 = await w.F.AddProductAsync("SKU-OTRO", tenantId: WmsFixture.OtherTenantId);
            await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p2.ProductId, 5m, ToWarehouseId: w2.WarehouseId, ToBinId: b2.WarehouseBinId));
            await SetOnHandAsync(w.F, p2.ProductId, b2.WarehouseBinId, 6m);
            (otherProduct, otherBin) = (p2.ProductId, b2.WarehouseBinId);
        }
        await SetOnHandAsync(w.F, w.P.ProductId, w.B.WarehouseBinId, 11m);

        // El tenant 1 (primero en el orden) falla; el 2 se revisa igual en la misma pasada.
        w.Effect.ShouldFail = () => w.F.Tenant.TenantId == WmsFixture.TenantId;
        w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 1));
        w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.OtherTenantId, new[] { otherProduct }, 2));
        var now = DateTime.UtcNow;
        Assert.Equal(2, w.Worker.Drain(now));
        Assert.Equal(2, await w.Worker.ProcessDueAsync(now, default));

        var s1 = w.Queue.Status(WmsFixture.TenantId);
        var s2 = w.Queue.Status(WmsFixture.OtherTenantId);
        Assert.Equal((1L, 0L, 0), (s1.Failed, s1.Processed, s1.Pending));
        Assert.Equal("Falla simulada de la revisión.", s1.LastError);
        Assert.Equal((0L, 1L, 0), (s2.Failed, s2.Processed, s2.Pending));
        Assert.Null(s2.LastError);   // el error de una compañía no se ve en otra
        Assert.Equal((WmsFixture.TenantId, 1), (w.F.Tenant.TenantId!.Value, w.F.Tenant.UserId!.Value));   // contexto restaurado
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = Assert.Single(await DiscrepanciesAsync(w.F));
            Assert.Equal((otherProduct, otherBin, 5m, 6m), (other.ProductId, other.WarehouseBinId!.Value, other.LedgerQty, other.BalanceQty));
        }

        // Sin la falla, el siguiente aviso del tenant 1 se revisa: un solo descuadre abierto para su producto.
        w.Effect.ShouldFail = null;
        w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 3));
        w.Worker.Drain(now.AddSeconds(1));
        Assert.Equal(1, await w.Worker.ProcessDueAsync(now.AddSeconds(1), default));
        var mine = Assert.Single(await DiscrepanciesAsync(w.F));
        Assert.Equal((w.P.ProductId, 11m), (mine.ProductId, mine.BalanceQty));
        Assert.Null(mine.ClosedAtUtc);
        Assert.Equal(1L, w.Queue.Status(WmsFixture.TenantId).Processed);
    }

    [Fact]
    public async Task The_real_loop_consumes_stops_and_then_rejects_notices()
    {
        var w = await SeedAsync(new InventoryReconciliationOptions { DebounceMs = 50, MinRecheckSeconds = 0 });
        await using var _ = w.F;
        await SetOnHandAsync(w.F, w.Q.ProductId, w.B.WarehouseBinId, 7m);   // Kárdex 4, saldo 7
        Assert.False(w.Queue.IsConsuming);

        await w.Worker.StartAsync(default);
        try
        {
            Assert.True(w.Queue.IsConsuming);
            Assert.True(w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.Q.ProductId }, 77)));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var s = w.Queue.Status(WmsFixture.TenantId);
                if (s.Pending == 0 && s.Processed + s.Failed > 0) break;
                await Task.Delay(25);
            }
            var status = w.Queue.Status(WmsFixture.TenantId);
            Assert.Equal((0, 1L, 0L), (status.Pending, status.Processed, status.Failed));
        }
        finally
        {
            await w.Worker.StopAsync(default);
        }

        Assert.False(w.Queue.IsConsuming);
        Assert.False(w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.Q.ProductId }, 78)));
        var d = Assert.Single(await DiscrepanciesAsync(w.F));
        Assert.Equal((w.Q.ProductId, 4m, 7m, 77L), (d.ProductId, d.LedgerQty, d.BalanceQty, d.LastTxnId!.Value));
    }

    [Fact]
    public async Task A_disabled_worker_never_consumes()
    {
        var w = await SeedAsync(new InventoryReconciliationOptions { Enabled = false });
        await using var _ = w.F;
        await w.Worker.StartAsync(default);
        await w.Worker.StopAsync(default);
        Assert.False(w.Queue.IsConsuming);
        Assert.False(w.Queue.TryEnqueue(new InventoryChangeNotice(WmsFixture.TenantId, new[] { w.P.ProductId }, 1)));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 249 — avisos anotados en memoria (las pruebas del motor leen qué se publicó y con qué clave).</summary>
internal sealed class RecordingPublisher : INotificationPublisher
{
    public List<(string Event, IReadOnlyDictionary<string, string?> Values, string? Key)> Published { get; } = new();

    public Task<bool> PublishAsync(string eventCode, IReadOnlyDictionary<string, string?>? values, string? dedupKey, CancellationToken ct)
    {
        if (dedupKey is not null && Published.Any(p => p.Event == eventCode && p.Key == dedupKey)) return Task.FromResult(false);
        Published.Add((eventCode, values ?? new Dictionary<string, string?>(), dedupKey));
        return Task.FromResult(true);
    }
}

/// <summary>
/// Lote 249 — mundo InMemory de las pruebas del motor de modificaciones (P3): almacén con una posición de picking, un cliente, tres productos con precio y
/// existencia, SalesOrderService y PickBatchService (con el gancho de la reversa) y un publicador que anota. <see cref="ApplyAsync"/> orquesta como lo hará
/// SalesOrderChangeService: carga, planea (reglas puras), deshace las recolecciones que escoge la reversa mínima y aplica.
/// </summary>
internal sealed class SalesChangeWorld : IAsyncDisposable
{
    public required WmsFixture F { get; init; }
    public required Warehouse W { get; init; }
    public required Teikem.Domain.Clients.Client C { get; init; }
    public required WarehouseBin Bin { get; init; }
    public required Product P1 { get; init; }
    public required Product P2 { get; init; }
    public required Product P3 { get; init; }
    public required RecordingPublisher Publisher { get; init; }
    public SalesOrderService Svc => F.Get<SalesOrderService>();
    public PickBatchService Batches => F.Get<PickBatchService>();

    public static async Task<SalesChangeWorld> CreateAsync(decimal stock = 20m, bool tax = false)
    {
        var publisher = new RecordingPublisher();
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<INotificationPublisher>(publisher);
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());
            s.AddSingleton(sp => new PickBatchService(
                sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<Teikem.Infrastructure.Orders.INumberSequenceService>(),
                sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!, sp.GetRequiredService<ISalesDispatchReversal>()));
        });
        f.SetModules(tax ? new[] { ModuleKeys.SalesOrders, ModuleKeys.Tax } : new[] { ModuleKeys.SalesOrders });
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        async Task<Product> Sellable(string sku, decimal price)
        {
            var p = await f.AddProductAsync(sku, purchaseCost: 1m);
            var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
            row.SalePrice = price;
            f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = stock, UpdatedAtUtc = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            return p;
        }
        var p1 = await Sellable("MED-1", 10m);
        var p2 = await Sellable("MED-2", 8m);
        var p3 = await Sellable("MED-3", 5m);
        return new SalesChangeWorld { F = f, W = w, C = c, Bin = bin, P1 = p1, P2 = p2, P3 = p3, Publisher = publisher };
    }

    public ValueTask DisposeAsync() => F.DisposeAsync();

    /// <summary>Crea y solicita una orden con entrega (DELIVERY) y las líneas dadas.</summary>
    public async Task<SalesOrderDto> RequestedAsync(params (Product P, decimal Qty)[] lines)
    {
        var draft = await Svc.CreateAsync(new SalesOrderRequest(W.PublicId, C.PublicId, Lines: lines.Select(l => new SalesOrderLineRequest(l.P.PublicId, l.Qty)).ToList(),
            DeliveryMode: SalesDeliveryModes.Delivery), default);
        return await Svc.RequestAsync(draft.PublicId, null, default);
    }

    public async Task<int> IdOfAsync(Guid publicId) => await F.Db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => o.SalesOrderId).SingleAsync();

    public async Task<SalesDispatchResultDto> DispatchAsync(Guid order, int lineId, decimal qty)
        => await Svc.DispatchAsync(order, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, qty) }), default);

    /// <summary>Como lo hará SalesOrderChangeService: carga, planea, deshace la reversa mínima y aplica. Un bloqueo del plan sale como 422.</summary>
    public async Task<SalesOrder> ApplyAsync(Guid orderPublicId, IReadOnlyList<ChangeOp> ops, int changeNo = 1, bool reopen = false,
        SalesChangeTermsInput? terms = null, string? notes = null, bool allowBackorder = false)
    {
        var orderId = await IdOfAsync(orderPublicId);
        var load = await Svc.LoadChangeStateAsync(orderId, default);
        var plan = SalesChangeRules.Plan(load.State, ops, reopen);
        if (plan.IsBlocked) throw new StatusRuleException(plan.Blockers[0]);
        foreach (var id in plan.ReverseBatchIds)
            await Batches.DeleteAsync(load.Batches.Single(b => b.BatchId == id).PublicId, new PickBatchDeleteRequest(SalesOrderService.ChangeHistoryComment(changeNo)), default);
        var applied = await Svc.ApplyOpsAsync(orderId, new SalesChangeApplyInput(ops, changeNo, terms, notes, allowBackorder), default);
        F.Db.ChangeTracker.Clear();
        return applied;
    }

    public async Task<(decimal OnHand, decimal Reserved)> BalanceOfAsync(Product p)
    {
        var b = await F.BalanceAsync(p.ProductId, Bin.WarehouseBinId);
        return (b?.QtyOnHand ?? 0m, b?.QtyReserved ?? 0m);
    }

    public async Task<decimal> ReservationRowsAsync(int lineId)
        => (await F.Db.Set<SalesOrderReservation>().AsNoTracking().Where(r => r.SalesOrderLineId == lineId).ToListAsync()).Sum(r => r.Quantity);

    /// <summary>Las invariantes de cada línea y de la cabecera se cumplen (con ON_DISPATCH).</summary>
    public async Task AssertCoherentAsync(Guid orderPublicId)
    {
        var order = await F.Db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.PublicId == orderPublicId);
        var inv = InvariantOrder.From(order);
        Assert.Empty(inv.Lines.SelectMany(l => SalesLineInvariants.Violations(l)));
        Assert.Empty(SalesLineInvariants.HeaderViolations(inv));
        foreach (var l in order.Lines.Where(l => l.Components.Count == 0))
            Assert.Equal(l.QtyReserved, await ReservationRowsAsync(l.SalesOrderLineId));
    }

    /// <summary>Siembra el dominio SalesOrderChangeStatus, el EntityType SALES_ORDER_CHANGE y el canal PHONE (como la sección «Lote 249» de update.sql).</summary>
    public async Task SeedChangeCatalogsAsync()
    {
        var pipe = F.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        var term = F.LookupId(LookupDomains.StageKind, StageKinds.Terminal);
        F.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = 9001, Entity = LookupDomains.EntityType, InternalCode = SalesChangeCodes.EntityType, LabelJson = "{\"es\":\"Modificación\"}" },
            new LookupCode { LookupCodeId = 9002, Entity = SalesChangeCodes.ChannelDomain, InternalCode = SalesChangeChannels.Phone, LabelJson = "{\"es\":\"Por teléfono\",\"en\":\"By phone\"}" });
        var sort = 1;
        foreach (var (code, kind, initial) in new[] { (SalesOrderChangeStatuses.PendingApproval, pipe, true), (SalesOrderChangeStatuses.Applied, term, false),
                     (SalesOrderChangeStatuses.Rejected, term, false), (SalesOrderChangeStatuses.Withdrawn, term, false) })
            F.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9100 + sort, Entity = SalesChangeCodes.StatusDomain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
                StageKindLookupId = kind, SortOrder = sort++, IsInitial = initial, IsActive = true });
        await F.Db.SaveChangesAsync();
        F.Lookups.Load(await F.Db.LookupCodes.AsNoTracking().ToListAsync());
        F.Db.ChangeTracker.Clear();
    }

    /// <summary>Una modificación PENDIENTE de aprobar en la orden (como la deja SalesOrderChangeService con segunda aprobación).</summary>
    public async Task<SalesOrderChange> AddPendingChangeAsync(int orderId)
    {
        var change = new SalesOrderChange
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SalesOrderId = orderId, ChangeNo = 1, Kind = SalesChangeKinds.Modify, StatusCodeId = 9101, IsPending = true,
            RegisteredBy = 1, RegisteredAtUtc = DateTime.UtcNow, AuthorizedByName = "María López", AuthorizationChannelLookupId = 9002, AuthorizedAtUtc = DateTime.UtcNow,
            Reason = "El cliente pidió más.", BaseFingerprint = new string('0', 64),
        };
        F.Db.Set<SalesOrderChange>().Add(change);
        await F.Db.SaveChangesAsync();
        F.Db.ChangeTracker.Clear();
        return change;
    }
}

// Lote 249 (P3): el motor de la modificación de la orden de venta dentro de SalesOrderService (reservar y liberar, agregar y quitar, reactivar, reversa mínima, estatus,
// totales exactos y el verificador de invariantes).
public sealed class SalesOrderChangeEngineTests
{
    [Fact]
    public async Task Requested_without_picks_raise_and_lower_reserve_and_release_and_totals_are_exact()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var l1 = o.Lines[0].Id;

        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: l1, Quantity: 5m) });
        var up = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((5m, 5m, 0m), (up.Lines[0].QtyOrdered, up.Lines[0].QtyReserved, up.Lines[0].QtyBackordered));
        Assert.Equal((20m, 5m), await x.BalanceOfAsync(x.P1));
        Assert.Equal((66m, 0m, 66m), (up.Subtotal, up.TaxAmount, up.Total));   // 5 × 10 + 2 × 8

        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: l1, Quantity: 1m) }, changeNo: 2);
        var down = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((1m, 1m), (down.Lines[0].QtyOrdered, down.Lines[0].QtyReserved));
        Assert.Equal((20m, 1m), await x.BalanceOfAsync(x.P1));
        Assert.Equal(26m, down.Total);
        Assert.Equal("REQUESTED", down.Status);
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task Adding_numbers_from_the_max_removing_keeps_the_line_closed_at_zero_and_adding_it_back_reactivates_the_same_line()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var l2 = o.Lines[1].Id;

        // agregar MED-3 y quitar MED-2 en la misma modificación
        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: x.P3.ProductId, Quantity: 4m), new ChangeOp(ChangeOpKinds.Remove, LineId: l2) });
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal(new[] { 1, 2, 3 }, after.Lines.Select(l => l.LineNo).ToArray());
        var removed = after.Lines.Single(l => l.Id == l2);
        Assert.True(removed.IsRemoved);
        Assert.Equal((0m, 0m, true), (removed.QtyOrdered, removed.QtyReserved, removed.IsClosed));
        var added = after.Lines.Single(l => l.LineNo == 3);
        Assert.Equal(("MED-3", 4m, 4m, 5m), (added.Sku, added.QtyOrdered, added.QtyReserved, added.UnitPrice));
        Assert.Equal((20m, 0m), await x.BalanceOfAsync(x.P2));   // la reserva de la quitada se liberó
        Assert.Equal((20m, 4m), await x.BalanceOfAsync(x.P3));
        Assert.Equal(50m, after.Total);   // 3 × 10 + 4 × 5
        var list = await x.Svc.ListAsync(null, null, null, false, null, null, 0, 10, default);
        Assert.Equal(2, list.Items.Single(i => i.PublicId == o.PublicId).LineCount);   // sin la quitada

        // volver a agregar MED-2: se reactiva su línea (mismo número), no se crea otra
        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: x.P2.ProductId, Quantity: 1m) }, changeNo: 2);
        var back = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal(3, back.Lines.Count);
        var again = back.Lines.Single(l => l.Id == l2);
        Assert.Equal((2, 1m, 1m, false, false), (again.LineNo, again.QtyOrdered, again.QtyReserved, again.IsClosed, again.IsRemoved));
        Assert.Equal(58m, back.Total);
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task Adding_a_product_already_in_the_order_or_removing_a_shipped_line_is_refused()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var orderId = await x.IdOfAsync(o.PublicId);
        var dup = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ApplyOpsAsync(orderId,
            new SalesChangeApplyInput(new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: x.P1.ProductId, Quantity: 1m) }, 1), default));
        Assert.Equal(SalesChangeRules.ProductRepeated("MED-1"), dup.Message);
        x.F.Db.ChangeTracker.Clear();
        await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 1m);
        var left = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.ApplyOpsAsync(orderId,
            new SalesChangeApplyInput(new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: o.Lines[0].Id, Quantity: 0.5m) }, 1), default));
        Assert.Equal(SalesChangeRules.BelowLeft("MED-1", 0.5m, 1m), left.Message);
    }

    [Fact]
    public async Task Lowering_below_what_was_picked_undoes_only_the_most_recent_needed_pick_and_reserves_it_again()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 10m));
        var line = o.Lines[0].Id;
        var a = await x.DispatchAsync(o.PublicId, line, 4m);   // A (vieja)
        var b = await x.DispatchAsync(o.PublicId, line, 2m);   // B (nueva)
        Assert.Equal((14m, 4m), await x.BalanceOfAsync(x.P1));

        // bajar a 5: hay que regresar 1 (6 despachadas) → solo B
        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: line, Quantity: 5m) });
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((5m, 4m, 1m), (after.Lines[0].QtyOrdered, after.Lines[0].QtyShipped, after.Lines[0].QtyReserved));
        Assert.Equal("PARTIAL", after.Status);
        var statuses = after.Dispatches!.ToDictionary(d => d.Number, d => d.Status);
        Assert.Equal(("COLLECTED", "CANCELLED"), (statuses[a.PickBatchNumber], statuses[b.PickBatchNumber]));
        // el Kárdex: B se revirtió (PICK_BATCH_REVERSAL) y A no
        var bId = await x.F.Db.Set<PickBatch>().AsNoTracking().Where(p => p.PublicId == b.PickBatchPublicId).Select(p => p.PickBatchId).SingleAsync();
        var aId = await x.F.Db.Set<PickBatch>().AsNoTracking().Where(p => p.PublicId == a.PickBatchPublicId).Select(p => p.PickBatchId).SingleAsync();
        Assert.All(await x.F.Db.Set<PickBatchLine>().AsNoTracking().Where(l => l.PickBatchId == bId).ToListAsync(), l => Assert.NotNull(l.ReversalTxnId));
        Assert.All(await x.F.Db.Set<PickBatchLine>().AsNoTracking().Where(l => l.PickBatchId == aId).ToListAsync(), l => Assert.Null(l.ReversalTxnId));
        Assert.Equal((16m, 1m), await x.BalanceOfAsync(x.P1));   // 20 − 4 salidas en A; 1 reservada
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task Adding_to_a_picked_order_leaves_the_pick_untouched_and_a_shipped_order_goes_back_to_partial()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 2m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 2m);
        Assert.Equal("SHIPPED", d.Order.Status);

        await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Add, ProductId: x.P2.ProductId, Quantity: 3m) });
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal("PARTIAL", after.Status);
        Assert.Equal("COLLECTED", Assert.Single(after.Dispatches!).Status);
        Assert.Equal((2m, 3m), (after.Lines[0].QtyShipped, after.Lines[1].QtyReserved));
        var orderId = await x.IdOfAsync(o.PublicId);
        var history = await x.F.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == orderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal(SalesOrderService.ChangeHistoryComment(1), history.Last().Comment);
        await x.AssertCoherentAsync(o.PublicId);
    }

    [Fact]
    public async Task Projection_writes_nothing_and_matches_what_is_applied()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var orderId = await x.IdOfAsync(o.PublicId);
        var ops = new[] { new ChangeOp(ChangeOpKinds.Qty, LineId: o.Lines[0].Id, Quantity: 4m), new ChangeOp(ChangeOpKinds.Add, ProductId: x.P3.ProductId, Quantity: 2m) };
        var load = await x.Svc.LoadChangeStateAsync(orderId, default);
        var projection = await x.Svc.ProjectAsync(load, new SalesChangeApplyInput(ops, 1), default);
        Assert.Equal((46m, 66m, 20m), (projection.TotalBefore, projection.TotalAfter, projection.TotalDelta));
        Assert.Single(projection.Diff.Added);
        Assert.Single(projection.Diff.Changed);
        x.F.Db.ChangeTracker.Clear();
        var still = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((3m, 2, 46m), (still.Lines[0].QtyOrdered, still.Lines.Count, still.Total));   // nada se escribió

        var applied = await x.ApplyAsync(o.PublicId, ops);
        Assert.Equal(projection.TotalAfter, SalesOrderExtraRules.Total(applied.Subtotal, applied.TaxAmount, applied.ShippingCharge, applied.ShippingTaxAmount));
    }

    [Fact]
    public async Task A_change_that_would_leave_a_line_incoherent_is_refused_and_nothing_is_applied()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 3m), (x.P2, 2m));
        var l2 = o.Lines[1].Id;
        // se pierden las filas de reserva de la línea 2 (la línea sigue diciendo 2 reservadas): quitarla no podría liberar nada
        x.F.Db.Set<SalesOrderReservation>().RemoveRange(await x.F.Db.Set<SalesOrderReservation>().AsTracking().Where(r => r.SalesOrderLineId == l2).ToListAsync());
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Remove, LineId: l2) }));
        Assert.Equal(SalesLineInvariants.IncoherentMessage(2), ex.Message);
        x.F.Db.ChangeTracker.Clear();
        var still = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((2m, 2m, false), (still.Lines[1].QtyOrdered, still.Lines[1].QtyReserved, still.Lines[1].IsClosed));
    }

    [Fact]
    public async Task The_change_state_classifies_picks_and_lists_line_positions()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 5m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 3m);
        var load = await x.Svc.LoadChangeStateAsync(await x.IdOfAsync(o.PublicId), default);
        var batch = Assert.Single(load.State.Batches);
        Assert.Equal((d.PickBatchNumber, PickBatchStatuses.Collected, 3m), (batch.Number, batch.StatusCode, batch.UnitsOf(o.Lines[0].Id)));
        Assert.Equal(ChangeBatchStages.Reversible, SalesChangeRules.ClassifyBatch(load.State, batch).Stage);
        Assert.Equal(d.PickBatchPublicId, Assert.Single(load.Batches).PublicId);
        var pos = Assert.Single(SalesChangeRules.LinePositions(load.State));
        Assert.Equal((0m, 3m, 0m), (pos.MinQuantity, pos.InCollected, pos.Out));
        Assert.Equal(SalesChangeApprovalModes.None, load.Settings.ApprovalMode);
        Assert.Equal("ON_DISPATCH", load.BillingTrigger);
        Assert.Empty(load.Invoices);
    }

    [Fact]
    public async Task Terms_and_notes_change_and_the_changed_notice_carries_the_warehouse_impact()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        await x.SeedChangeCatalogsAsync();
        var o = await x.RequestedAsync((x.P1, 2m));
        var applied = await x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Terms), new ChangeOp(ChangeOpKinds.Notes) },
            terms: new SalesChangeTermsInput(DeliveryMode: SalesDeliveryModes.Pickup), notes: "Recoge el cliente");
        Assert.Equal((SalesDeliveryModes.Pickup, "Recoge el cliente"), (applied.DeliveryMode, applied.Notes));
        await Assert.ThrowsAsync<ValidationException>(() => x.ApplyAsync(o.PublicId, new[] { new ChangeOp(ChangeOpKinds.Terms) }, changeNo: 2));

        var change = await x.AddPendingChangeAsync(await x.IdOfAsync(o.PublicId));
        var order = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(s => s.PublicId == o.PublicId);
        await x.Svc.NotifyChangedAsync(order, change, SalesOrderService.WarehouseImpactText(new[] { "EMP-00002" }, Array.Empty<string>()), null, default);
        var notice = Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.OrderChanged);
        Assert.Equal($"CHG-{change.PublicId:N}", notice.Key);
        Assert.Equal(("1", "Por teléfono", "Se deshizo la recolección EMP-00002 (el almacén vuelve a despachar)."),
            (notice.Values["changeNo"], notice.Values["channel"], notice.Values["warehouseImpact"]));
        Assert.Equal("Sin cambios en el almacén.", SalesOrderService.WarehouseImpactText(Array.Empty<string>(), Array.Empty<string>()));
    }
}

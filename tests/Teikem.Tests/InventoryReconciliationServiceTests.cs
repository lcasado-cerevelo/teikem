using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P1) — descuadres Kárdex ↔ saldo (D5) con InventoryReconciliationService e InventoryLedger.RebuildBalanceAsync, en
/// InMemory con el ledger real (WmsFixture). Un descuadre solo aparece por una escritura fuera del ledger: la prueba lo fuerza
/// escribiendo el saldo directamente (nunca en src/). Cubre: alta OPEN con historial y origen, segunda revisión sin duplicar,
/// cierre solo, aislamiento entre tenants, corregir (saldo = Kárdex sin movimiento nuevo), 409 de reservado y de Kárdex
/// negativo, 422 de cerrado y de total del producto, 400 de acción y nota, descartar (no se reabre con las mismas cifras),
/// corregir cuando ya cuadraba, barrido, "Ejecutar conciliación" (tope 200), lista, ficha y fuente de datos. Además, el
/// reconciliador extraído da exactamente lo mismo que el algoritmo anterior de TraceabilityService.
/// </summary>
public class InventoryReconciliationServiceTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B1, WarehouseBin B2, Product P, Product Q);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<InventoryReconciler>();
            s.AddSingleton<TraceabilityService>();
            s.AddSingleton<InventoryReconciliationService>();
            s.AddSingleton<InventoryDiscrepancyDataSource>();
        });
        var w = await f.AddWarehouseAsync("ALM-01");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var b1 = await f.AddBinAsync(z, "A-01");
        var b2 = await f.AddBinAsync(z, "A-02");
        var p = await f.AddProductAsync("SKU-P");
        var q = await f.AddProductAsync("SKU-Q");
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: b1.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, p.ProductId, 4m, FromWarehouseId: w.WarehouseId, FromBinId: b1.WarehouseBinId,
            ToWarehouseId: w.WarehouseId, ToBinId: b2.WarehouseBinId));
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, q.ProductId, 3m, ToWarehouseId: w.WarehouseId, ToBinId: b1.WarehouseBinId));
        return new World(f, w, b1, b2, p, q);
    }

    private static InventoryReconciliationService Svc(World w) => w.F.Get<InventoryReconciliationService>();

    /// <summary>Escritura directa del saldo (fuera del ledger): así nace un descuadre.</summary>
    private static async Task SetBalanceAsync(World w, Product p, WarehouseBin bin, decimal onHand, decimal? reserved = null)
    {
        var b = await w.F.Db.StockBalances.SingleAsync(x => x.ProductId == p.ProductId && x.WarehouseBinId == bin.WarehouseBinId);
        b.QtyOnHand = onHand;
        if (reserved is decimal r) b.QtyReserved = r;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
    }

    private static Task<List<InventoryDiscrepancy>> AllAsync(World w)
        => w.F.Db.InventoryDiscrepancies.AsNoTracking().OrderBy(d => d.InventoryDiscrepancyId).ToListAsync();

    [Fact]
    public async Task A_balance_off_the_ledger_opens_one_discrepancy_with_history_and_trigger()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        Assert.Equal(0, (await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default)).Opened);

        await SetBalanceAsync(w, w.P, w.B2, 5m);   // Kárdex 4, saldo 5
        var run = await Svc(w).CheckProductsAsync(new[] { w.P.ProductId }, ReconciliationTriggers.Event, 77, default);
        Assert.Equal((1, 0, 0), (run.Opened, run.StillOpen, run.SelfCorrected));
        var row = Assert.Single(run.Mismatches);
        Assert.Equal(("SKU-P", "ALM-01", "A-02", 4m, 5m), (row.Sku, row.WarehouseCode, row.BinCode, row.LedgerQty, row.BalanceQty));

        var d = Assert.Single(await AllAsync(w));
        Assert.Equal(w.F.StatusId(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Open), d.StatusCodeId);
        Assert.Equal(w.F.LookupId(LookupDomains.ReconciliationTrigger, ReconciliationTriggers.Event), d.TriggerLookupId);
        Assert.Equal(w.F.LookupId(LookupDomains.InventoryDiscrepancyKind, DiscrepancyKinds.Balance), d.KindLookupId);
        Assert.Equal((w.W.WarehouseId, w.B2.WarehouseBinId, (int?)null, 77L, 1), (d.WarehouseId!.Value, d.WarehouseBinId!.Value, d.LotId, d.LastTxnId!.Value, d.CheckCount));
        Assert.Null(d.ClosedAtUtc);
        Assert.Equal(new[] { InventoryDiscrepancyStatuses.Open }, await w.F.HistoryCodesAsync(EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId));

        // Segunda revisión: la misma fila, con una revisión más y las cifras nuevas.
        await SetBalanceAsync(w, w.P, w.B2, 6m);
        var again = await Svc(w).CheckProductsAsync(new[] { w.P.ProductId }, ReconciliationTriggers.Event, 78, default);
        Assert.Equal((0, 1, 0), (again.Opened, again.StillOpen, again.SelfCorrected));
        var same = Assert.Single(await AllAsync(w));
        Assert.Equal((2, 6m, 4m, 78L), (same.CheckCount, same.BalanceQty, same.LedgerQty, same.LastTxnId!.Value));
    }

    [Fact]
    public async Task A_balance_fixed_outside_closes_the_discrepancy_by_itself()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        await SetBalanceAsync(w, w.P, w.B2, 4m);
        var run = await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        Assert.Equal(1, run.SelfCorrected);
        Assert.Empty(run.Mismatches);
        var d = Assert.Single(await AllAsync(w));
        Assert.Equal(w.F.StatusId(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.SelfCorrected), d.StatusCodeId);
        Assert.NotNull(d.ClosedAtUtc);
        Assert.Null(d.ResolvedBy);
        Assert.Equal(new[] { InventoryDiscrepancyStatuses.Open, InventoryDiscrepancyStatuses.SelfCorrected },
            await w.F.HistoryCodesAsync(EntityTypes.InventoryDiscrepancy, d.InventoryDiscrepancyId));
    }

    [Fact]
    public async Task Tenants_are_isolated()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var mine = Assert.Single(await AllAsync(w));

        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var sweep = await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
            Assert.Equal(0, sweep.Opened);
            Assert.Empty(sweep.Mismatches);
            Assert.Equal(0, (await Svc(w).ListAsync(null, default)).Total);
            var ex = await Assert.ThrowsAsync<NotFoundException>(() => Svc(w).GetAsync(mine.PublicId, default));
            Assert.Equal("Descuadre no encontrado.", ex.Message);
            await Assert.ThrowsAsync<NotFoundException>(() =>
                Svc(w).ResolveAsync(mine.PublicId, new DiscrepancyResolveRequest("DISMISS", "no es mío", null), default));
        }
        Assert.Equal(1, (await Svc(w).ListAsync(null, default)).OpenCount);
    }

    [Fact]
    public async Task Rebuild_sets_the_balance_to_the_ledger_without_a_new_movement()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var open = Assert.Single(await AllAsync(w));
        var txnsBefore = (await w.F.TransactionsAsync()).Count;

        var detail = await Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("REBUILD_BALANCE", "  corregido  ", null), default);
        Assert.Equal(InventoryDiscrepancyStatuses.Resolved, detail.Discrepancy.StatusCode);
        Assert.Equal((5m, 4m), (detail.Discrepancy.CorrectedFromQty!.Value, detail.Discrepancy.CorrectedToQty!.Value));
        Assert.Equal("corregido", detail.Discrepancy.ResolutionNotes);
        Assert.NotNull(detail.Discrepancy.ClosedAtUtc);
        Assert.Equal(4m, await w.F.OnHandAsync(w.P.ProductId, w.B2.WarehouseBinId));
        Assert.Equal(txnsBefore, (await w.F.TransactionsAsync()).Count);   // el Kárdex manda: no se escribe movimiento
        Assert.Empty((await w.F.Get<TraceabilityService>().ReconcileAsync(null, default)).Mismatches);
        Assert.Equal(new[] { InventoryDiscrepancyStatuses.Open, InventoryDiscrepancyStatuses.Resolved }, detail.History.Select(h => h.ToCode).ToArray());
        Assert.Equal("corregido", detail.History[^1].Comment);
        var stored = Assert.Single(await AllAsync(w));
        Assert.Equal(1, stored.ResolvedBy);   // el usuario del contexto

        // Resolver de nuevo: cerrado → 422.
        var closed = await Assert.ThrowsAsync<StatusRuleException>(() =>
            Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("DISMISS", "otra vez", null), default));
        Assert.Equal("El descuadre ya está cerrado; solo se consulta.", closed.Message);
    }

    [Fact]
    public async Task Rebuild_below_reserved_or_negative_ledger_is_409_without_changes()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        // Kárdex 6 en A-01, saldo 9 con 7 reservado: corregir dejaría menos que lo reservado.
        await SetBalanceAsync(w, w.P, w.B1, 9m, reserved: 7m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var open = Assert.Single(await AllAsync(w));
        var below = await Assert.ThrowsAsync<ConflictException>(() =>
            Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("REBUILD_BALANCE", null, null), default));
        Assert.Equal("El Kárdex da 6 para SKU-P en A-01, menos que lo reservado (7); libere la reserva antes de corregir el saldo.", below.Message);
        Assert.Equal(9m, await w.F.OnHandAsync(w.P.ProductId, w.B1.WarehouseBinId));
        Assert.Null((await AllAsync(w)).Single().ClosedAtUtc);

        // Kárdex negativo (un movimiento escrito fuera del ledger).
        w.F.Db.InventoryTransactions.Add(new InventoryTransaction
        {
            TenantId = WmsFixture.TenantId, TxnTypeLookupId = w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Issue),
            ProductId = w.Q.ProductId, FromWarehouseId = w.W.WarehouseId, FromBinId = w.B1.WarehouseBinId, Quantity = -5m, CreatedAtUtc = DateTime.UtcNow,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        await Svc(w).CheckProductsAsync(new[] { w.Q.ProductId }, ReconciliationTriggers.Manual, null, default);
        var q = (await AllAsync(w)).Single(d => d.ProductId == w.Q.ProductId);
        var negative = await Assert.ThrowsAsync<ConflictException>(() =>
            Svc(w).ResolveAsync(q.PublicId, new DiscrepancyResolveRequest("REBUILD_BALANCE", null, null), default));
        Assert.Equal("El Kárdex da un saldo negativo (-2) para SKU-Q en A-01; revise los movimientos antes de corregir el saldo.", negative.Message);
    }

    [Fact]
    public async Task Resolve_validations_and_product_total()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var open = Assert.Single(await AllAsync(w));

        var noAction = await Assert.ThrowsAsync<ValidationException>(() => Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest(null, "x", null), default));
        Assert.Equal(ReconciliationRules.ActionRequired, noAction.Errors!["action"].Single());
        var noNote = await Assert.ThrowsAsync<ValidationException>(() => Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("DISMISS", " ", null), default));
        Assert.Equal("Escriba una nota que explique por qué se descarta el descuadre.", noNote.Errors!["notes"].Single());
        Assert.Equal("Escriba una nota que explique por qué se descarta el descuadre.", noNote.Message);   // también en el detalle del 400
        await Assert.ThrowsAsync<ValidationException>(() =>
            Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("DISMISS", new string('n', 501), null), default));
        var badVersion = await Assert.ThrowsAsync<ValidationException>(() =>
            Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("DISMISS", "nota", "no-es-base64!"), default));
        Assert.Contains("rowVersion", badVersion.Errors!.Keys);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(w).ResolveAsync(Guid.NewGuid(), new DiscrepancyResolveRequest("DISMISS", "nota", null), default));

        // Un descuadre del total del producto no se corrige por posición (422); sí se descarta.
        var total = new InventoryDiscrepancy
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, KindLookupId = w.F.LookupId(LookupDomains.InventoryDiscrepancyKind, DiscrepancyKinds.ProductTotal),
            TriggerLookupId = w.F.LookupId(LookupDomains.ReconciliationTrigger, ReconciliationTriggers.Migration), ProductId = w.Q.ProductId,
            LedgerQty = 3m, BalanceQty = 4m, DetectedAtUtc = DateTime.UtcNow, LastCheckedAtUtc = DateTime.UtcNow,
            StatusCodeId = w.F.StatusId(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Open),
        };
        w.F.Db.InventoryDiscrepancies.Add(total);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var pt = await Assert.ThrowsAsync<StatusRuleException>(() =>
            Svc(w).ResolveAsync(total.PublicId, new DiscrepancyResolveRequest("REBUILD_BALANCE", null, null), default));
        Assert.Equal(ReconciliationRules.ProductTotalNotRebuildable, pt.Message);
        var dismissed = await Svc(w).ResolveAsync(total.PublicId, new DiscrepancyResolveRequest("DISMISS", "revisado con contabilidad", null), default);
        Assert.Equal(InventoryDiscrepancyStatuses.Dismissed, dismissed.Discrepancy.StatusCode);
        Assert.Equal((DiscrepancyKinds.ProductTotal, (string?)null), (dismissed.Discrepancy.KindCode, dismissed.Discrepancy.WarehouseCode));
        Assert.Equal(0m, dismissed.CurrentReserved);   // total del producto: lo reservado de todo el producto
    }

    [Fact]
    public async Task A_dismissed_discrepancy_does_not_reopen_with_the_same_numbers_but_does_with_others()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var open = Assert.Single(await AllAsync(w));
        var dismissed = await Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("dismiss", "diferencia conocida", null), default);
        Assert.Equal(InventoryDiscrepancyStatuses.Dismissed, dismissed.Discrepancy.StatusCode);
        Assert.Equal(5m, await w.F.OnHandAsync(w.P.ProductId, w.B2.WarehouseBinId));   // descartar no mueve inventario

        var same = await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        Assert.Equal(0, same.Opened);
        Assert.Single(same.Mismatches);   // sigue descuadrado, pero no se reabre
        Assert.Single(await AllAsync(w));

        await SetBalanceAsync(w, w.P, w.B2, 7m);
        Assert.Equal(1, (await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default)).Opened);
        Assert.Equal(2, (await AllAsync(w)).Count);
    }

    [Fact]
    public async Task Rebuild_when_it_already_balances_closes_it_by_itself()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var open = Assert.Single(await AllAsync(w));
        await SetBalanceAsync(w, w.P, w.B2, 4m);   // alguien lo arregló por fuera antes de resolver

        var detail = await Svc(w).ResolveAsync(open.PublicId, new DiscrepancyResolveRequest("REBUILD_BALANCE", null, null), default);
        Assert.Equal(InventoryDiscrepancyStatuses.SelfCorrected, detail.Discrepancy.StatusCode);
        Assert.Null(detail.Discrepancy.CorrectedToQty);
        Assert.Equal(ReconciliationRules.AlreadyBalancedComment, detail.History[^1].Comment);
    }

    [Fact]
    public async Task Manual_run_limits_products_and_resolves_them_by_public_id()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var tooMany = await Assert.ThrowsAsync<ValidationException>(() =>
            Svc(w).RunAsync(new ReconciliationRunRequest(Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray()), default));
        Assert.Equal("La conciliación manual admite como máximo 200 productos a la vez.", tooMany.Errors!["productPublicIds"].Single());
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(w).RunAsync(new ReconciliationRunRequest(new[] { Guid.NewGuid() }), default));

        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await SetBalanceAsync(w, w.Q, w.B1, 1m);
        var onlyQ = await Svc(w).RunAsync(new ReconciliationRunRequest(new[] { w.Q.PublicId }), default);
        Assert.Equal((1, 1), (onlyQ.ProductsChecked, onlyQ.Opened));
        Assert.Equal(w.F.LookupId(LookupDomains.ReconciliationTrigger, ReconciliationTriggers.Manual), (await AllAsync(w)).Single().TriggerLookupId);
        var all = await Svc(w).RunAsync(null, default);
        Assert.Equal((2, 1, 1), (all.ProductsChecked, all.Opened, all.StillOpen));
        Assert.Equal(2, all.Mismatches.Count);
    }

    [Fact]
    public async Task List_detail_and_data_source()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await SetBalanceAsync(w, w.P, w.B2, 5m);
        await SetBalanceAsync(w, w.Q, w.B1, 1m);
        await Svc(w).SweepAsync(ReconciliationTriggers.Manual, default);
        var q = (await AllAsync(w)).Single(d => d.ProductId == w.Q.ProductId);
        await Svc(w).ResolveAsync(q.PublicId, new DiscrepancyResolveRequest("DISMISS", "ok", null), default);

        var all = await Svc(w).ListAsync(null, default);
        Assert.Equal((2, 1), (all.Total, all.OpenCount));
        Assert.Equal("SKU-P", all.Items[0].Sku);   // abiertos primero
        Assert.Equal(1m, all.Items[0].Difference);
        Assert.Equal(ReconciliationTriggers.Manual, all.Items[0].TriggerCode);
        var open = await Svc(w).ListAsync(new InventoryDiscrepancyQuery(Status: new[] { "open" }), default);
        Assert.Equal((1, 1), (open.Total, open.OpenCount));
        Assert.Equal(1, (await Svc(w).ListAsync(new InventoryDiscrepancyQuery(ProductPublicIds: new[] { w.Q.PublicId }), default)).Total);
        Assert.Equal(1, (await Svc(w).ListAsync(new InventoryDiscrepancyQuery(BinIds: new[] { w.B2.WarehouseBinId }), default)).Total);
        Assert.Equal(2, (await Svc(w).ListAsync(new InventoryDiscrepancyQuery(WarehousePublicIds: new[] { w.W.PublicId }, Kinds: new[] { "BALANCE" }), default)).Total);
        var today = TenantClock.Default.Today;
        Assert.Equal(2, (await Svc(w).ListAsync(new InventoryDiscrepancyQuery(From: today, To: today), default)).Total);
        Assert.Equal(0, (await Svc(w).ListAsync(new InventoryDiscrepancyQuery(To: today.AddDays(-1)), default)).Total);
        var badKind = await Assert.ThrowsAsync<ValidationException>(() => Svc(w).ListAsync(new InventoryDiscrepancyQuery(Kinds: new[] { "X" }), default));
        Assert.Equal("Tipo de descuadre desconocido: 'X'.", badKind.Errors!["kinds"].Single());

        var p = all.Items[0];
        var detail = await Svc(w).GetAsync(p.PublicId, default);
        Assert.Equal(0m, detail.CurrentReserved);
        Assert.Equal(new[] { 4m }, detail.RecentMovements.Select(m => m.SignedQuantity).ToArray());   // la transferencia que entró a A-02
        Assert.Equal(InventoryDiscrepancyStatuses.Open, Assert.Single(detail.History).ToCode);

        var rows = await w.F.Get<InventoryDiscrepancyDataSource>().LoadAsync(new DataQuery(), default);
        Assert.Equal(2, rows.Count);
        var row = rows.Single(r => (string?)r["Sku"] == "SKU-P");
        Assert.Equal(("A-02", 1m, true), ((string?)row["BinCode"], (decimal)row["Difference"]!, (bool)row["IsOpen"]!));
        Assert.Equal("DetectedAtUtc", w.F.Get<InventoryDiscrepancyDataSource>().DateField);
    }

    // ---------------------------------------------------------------- mismo resultado que antes (extracción del reconciliador)

    /// <summary>
    /// El algoritmo de TraceabilityService.ReconcileAsync ANTES del Lote 14, copiado aquí como referencia (agregados por lado,
    /// neto sin TRANSFER, comparación por clave y por producto solo sin descuadre por clave, mismo orden).
    /// </summary>
    private static List<(Guid Product, string Warehouse, int? Bin, int? Lot, decimal Ledger, decimal Balance)> LegacyReconcile(
        IReadOnlyList<InventoryTransaction> txns, IReadOnlyList<StockBalance> balances, int transferId, IReadOnlyDictionary<int, Guid> products,
        IReadOnlyDictionary<int, string> warehouses)
    {
        var rebuilt = new Dictionary<BalanceKey, decimal>();
        foreach (var g in txns.Where(t => t.ToWarehouseId != null).GroupBy(t => new BalanceKey(t.ProductId, t.ToWarehouseId!.Value, t.ToBinId, t.LotId)))
            rebuilt[g.Key] = rebuilt.GetValueOrDefault(g.Key) + g.Sum(t => Math.Abs(t.Quantity));
        foreach (var g in txns.Where(t => t.FromWarehouseId != null).GroupBy(t => new BalanceKey(t.ProductId, t.FromWarehouseId!.Value, t.FromBinId, t.LotId)))
            rebuilt[g.Key] = rebuilt.GetValueOrDefault(g.Key) - g.Sum(t => Math.Abs(t.Quantity));
        var net = txns.Where(t => t.TxnTypeLookupId != transferId).GroupBy(t => t.ProductId).ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity));
        var byKey = balances.GroupBy(b => new BalanceKey(b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId)).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        var mismatches = new List<(BalanceKey Key, decimal L, decimal B, bool Total)>();
        foreach (var k in rebuilt.Keys.Union(byKey.Keys))
            if (rebuilt.GetValueOrDefault(k) != byKey.GetValueOrDefault(k)) mismatches.Add((k, rebuilt.GetValueOrDefault(k), byKey.GetValueOrDefault(k), false));
        var withKey = mismatches.Select(m => m.Key.ProductId).ToHashSet();
        var onHand = balances.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        foreach (var p in net.Keys.Union(onHand.Keys))
        {
            if (withKey.Contains(p)) continue;
            if (net.GetValueOrDefault(p) != onHand.GetValueOrDefault(p)) mismatches.Add((new BalanceKey(p, 0, null, null), net.GetValueOrDefault(p), onHand.GetValueOrDefault(p), true));
        }
        return mismatches.OrderBy(m => m.Key.ProductId).ThenBy(m => m.Total).ThenBy(m => m.Key.WarehouseId).ThenBy(m => m.Key.BinId).ThenBy(m => m.Key.LotId)
            .Select(m => (products[m.Key.ProductId], m.Total ? TraceabilityService.ProductTotalMarker : warehouses[m.Key.WarehouseId], m.Key.BinId, m.Key.LotId, m.L, m.B))
            .ToList();
    }

    [Fact]
    public async Task The_extracted_reconciler_gives_exactly_the_previous_result()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var lot = await w.F.AddLotAsync(w.P, "L-1");
        await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, w.P.ProductId, 8m, LotId: lot.LotId, ToWarehouseId: w.W.WarehouseId,
            ToBinId: w.B2.WarehouseBinId));
        // Varios descuadres de distinto tipo: saldo de más, saldo sin movimientos, movimiento sin saldo, y un producto limpio.
        await SetBalanceAsync(w, w.P, w.B1, 7m);
        var r = await w.F.AddProductAsync("SKU-R");
        w.F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = r.ProductId, WarehouseId = w.W.WarehouseId,
            WarehouseBinId = w.B2.WarehouseBinId, QtyOnHand = 2m, UpdatedAtUtc = DateTime.UtcNow });
        w.F.Db.InventoryTransactions.Add(new InventoryTransaction { TenantId = WmsFixture.TenantId,
            TxnTypeLookupId = w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt), ProductId = w.Q.ProductId,
            ToWarehouseId = w.W.WarehouseId, ToBinId = w.B2.WarehouseBinId, Quantity = 1m, CreatedAtUtc = DateTime.UtcNow });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var txns = await w.F.Db.InventoryTransactions.AsNoTracking().ToListAsync();
        var balances = await w.F.Db.StockBalances.AsNoTracking().ToListAsync();
        var products = await w.F.Db.Products.AsNoTracking().ToDictionaryAsync(p => p.ProductId, p => p.PublicId);
        var warehouses = await w.F.Db.Warehouses.AsNoTracking().ToDictionaryAsync(x => x.WarehouseId, x => x.Code);
        var expected = LegacyReconcile(txns, balances, w.F.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), products, warehouses);
        Assert.Equal(3, expected.Count);

        var actual = await w.F.Get<TraceabilityService>().ReconcileAsync(null, default);
        var bins = await w.F.Db.WarehouseBins.AsNoTracking().ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code);
        Assert.Equal(expected.Select(e => (e.Product, e.Warehouse, e.Bin is int b ? bins[b] : null, e.Lot is int ? "L-1" : null, e.Ledger, e.Balance)).ToArray(),
            actual.Mismatches.Select(m => (m.ProductPublicId, m.WarehouseCode, m.BinCode, m.LotNumber, m.LedgerQty, m.BalanceQty)).ToArray());
        Assert.Equal(balances.Count, actual.BalancesChecked);

        // Por producto, el mismo resultado filtrado.
        var onlyP = await w.F.Get<TraceabilityService>().ReconcileAsync(w.P.PublicId, default);
        Assert.Equal(expected.Count(e => e.Product == w.P.PublicId), onlyP.Mismatches.Count);
    }
}

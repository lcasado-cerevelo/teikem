using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-12 — Ajuste múltiple AJM-###### (docs/ajuste-multiple-diseno.md), InMemory con el InventoryLedger REAL: solo las líneas con diferencia crean
/// movimientos (motivo PHYSICAL_COUNT, origen MULTI_ADJUSTMENT), todo o nada, saldo que cambió → 409 stale_system_qty, nunca por debajo de lo reservado,
/// sin series, nota obligatoria, numeración AJM-######, y el Kárdex lee el origen.
/// </summary>
public class MultiAdjustmentTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B1, WarehouseBin B2, Product P1, Product P2);

    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new MultiAdjustmentService(
        sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<InventoryLedger>(),
        sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<ITenantContext>()))
        .AddSingleton(sp => new InventoryReadService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ILookupCache>(), TenantClock.Default)));

    private static async Task<World> WorldAsync(decimal p1b1 = 10m, decimal p1b2 = 4m, decimal p2b1 = 7m)
    {
        var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var b1 = await f.AddBinAsync(z, "A-01");
        var b2 = await f.AddBinAsync(z, "A-02");
        var p1 = await f.AddProductAsync("P1");
        var p2 = await f.AddProductAsync("P2");
        foreach (var (p, b, q) in new[] { (p1, b1, p1b1), (p1, b2, p1b2), (p2, b1, p2b1) })
            if (q > 0) await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, q, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId));
        return new World(f, w, b1, b2, p1, p2);
    }

    private static MultiAdjustmentLineInput Line(Product p, WarehouseBin b, decimal? system, decimal? counted, int? lotId = null)
        => new(p.PublicId, b.WarehouseBinId, lotId, system, counted);

    private static MultiAdjustmentRequest Req(World x, params MultiAdjustmentLineInput[] lines) => new(x.W.PublicId, "Inventario semanal", lines);

    private static async Task<decimal> OnHandAsync(World x, Product p, WarehouseBin b)
    {
        x.F.Db.ChangeTracker.Clear();
        return await x.F.Db.Set<StockBalance>().AsNoTracking().Where(s => s.ProductId == p.ProductId && s.WarehouseBinId == b.WarehouseBinId).SumAsync(s => s.QtyOnHand);
    }

    private static Task<int> DocCountAsync(World x) => x.F.Db.Set<MultiAdjustment>().AsNoTracking().CountAsync();

    // ================================================================ aplicar

    [Fact]
    public async Task Only_lines_with_a_difference_become_movements_with_the_multi_adjustment_as_origin()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var txnsBefore = await x.F.Db.Set<InventoryTransaction>().CountAsync();

        var result = await svc.CreateAsync(Req(x,
            Line(x.P1, x.B1, 10m, 7m),      // baja 3
            Line(x.P1, x.B2, 4m, 9m),       // sube 5
            Line(x.P2, x.B1, 7m, 7m),       // igual: no se toca
            Line(x.P2, x.B2, 0m, null)),    // vacío: no se toca
            default);

        Assert.Equal("AJM-000001", result.Header.Number);
        Assert.Equal(2, result.Header.LineCount);
        Assert.Equal(1, result.Header.ProductCount);
        Assert.Equal(5m, result.Header.UnitsUp);
        Assert.Equal(3m, result.Header.UnitsDown);
        Assert.Equal("Inventario semanal", result.Header.Notes);
        Assert.Equal(2, result.Lines.Count);
        var down = result.Lines.Single(l => l.Difference < 0);
        Assert.Equal((10m, 7m, -3m), (down.SystemQty, down.CountedQty, down.Difference));
        Assert.NotNull(down.InventoryTransactionId);

        Assert.Equal(7m, await OnHandAsync(x, x.P1, x.B1));
        Assert.Equal(9m, await OnHandAsync(x, x.P1, x.B2));
        Assert.Equal(7m, await OnHandAsync(x, x.P2, x.B1));
        Assert.Equal(txnsBefore + 2, await x.F.Db.Set<InventoryTransaction>().CountAsync());

        var kardex = await x.F.Get<InventoryReadService>().KardexAsync(new KardexQuery(), InventoryScope.Any, default);
        var rows = kardex.Items.Where(r => r.ReasonCode == AdjustmentReasons.PhysicalCount).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("ADJUSTMENT", r.TypeCode);
            Assert.Equal(EntityTypes.MultiAdjustment, r.RefEntityCode);
            Assert.Equal(result.Header.Id, r.RefId);
            Assert.Equal("Ajuste múltiple AJM-000001", r.RefLabel);
            Assert.Equal("Inventario semanal", r.Notes);
        });
        Assert.Equal(new[] { -3m, 5m }, rows.Select(r => r.SignedQuantity).OrderBy(q => q));
    }

    [Fact]
    public async Task The_kardex_detail_shows_the_multi_adjustment_as_the_source_document()
    {
        var x = await WorldAsync();
        var doc = await x.F.Get<MultiAdjustmentService>().CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 6m)), default);
        var txnId = doc.Lines.Single().InventoryTransactionId!.Value;
        var detail = await x.F.Get<InventoryReadService>().TransactionDetailAsync(txnId, InventoryScope.Any, default);
        Assert.Equal("AJM-000001", detail.Document?.Number);
        Assert.Equal(EntityTypes.MultiAdjustment, detail.Document?.EntityCode);
        Assert.Equal(doc.Header.PublicId, detail.Document?.PublicId);
    }

    [Fact]
    public async Task A_second_document_takes_the_next_number()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        await svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 11m)), default);
        var second = await svc.CreateAsync(Req(x, Line(x.P1, x.B1, 11m, 12m)), default);
        Assert.Equal("AJM-000002", second.Header.Number);
    }

    [Fact]
    public async Task Nothing_with_a_difference_creates_no_document()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 10m), Line(x.P1, x.B2, 4m, null)), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, await DocCountAsync(x));

        var empty = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, null)), default));
        Assert.Equal(400, empty.StatusCode);
        Assert.Contains("lines", empty.Errors!.Keys);
    }

    [Fact]
    public async Task A_system_quantity_that_changed_is_409_and_nothing_is_applied()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        // Mientras se contaba salieron 2 de P1 en A-01: la pantalla todavía cree que hay 10.
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, x.P1.ProductId, 2m, FromWarehouseId: x.W.WarehouseId, FromBinId: x.B1.WarehouseBinId));
        var txns = await x.F.Db.Set<InventoryTransaction>().CountAsync();

        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x,
            Line(x.P2, x.B1, 7m, 9m),       // esta línea sí está bien
            Line(x.P1, x.B1, 10m, 6m)),     // esta no
            default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(MultiAdjustmentService.StaleCode, ex.Code);
        Assert.Equal(new[] { "lines[1]" }, ex.Errors!.Keys);
        Assert.Contains("el sistema tiene 8", ex.Errors["lines[1]"][0]);
        // todo o nada: ni la línea buena se aplicó, ni hay documento, ni se gastó el número
        Assert.Equal(7m, await OnHandAsync(x, x.P2, x.B1));
        Assert.Equal(8m, await OnHandAsync(x, x.P1, x.B1));
        Assert.Equal(txns, await x.F.Db.Set<InventoryTransaction>().CountAsync());
        Assert.Equal(0, await DocCountAsync(x));
    }

    [Fact]
    public async Task The_final_quantity_cannot_be_below_what_is_reserved()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var balance = await x.F.Db.Set<StockBalance>().AsTracking().SingleAsync(s => s.ProductId == x.P1.ProductId && s.WarehouseBinId == x.B1.WarehouseBinId);
        balance.QtyReserved = 6m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 5m)), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("insufficient_stock", ex.Code);
        Assert.Equal(new[] { "lines[0]" }, ex.Errors!.Keys);
        Assert.Equal(10m, await OnHandAsync(x, x.P1, x.B1));

        // a lo reservado sí se puede bajar
        var ok = await svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 6m)), default);
        Assert.Equal(1, ok.Header.LineCount);
    }

    [Fact]
    public async Task A_position_without_stock_can_receive_what_was_found()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var result = await svc.CreateAsync(Req(x, Line(x.P2, x.B2, 0m, 3m)), default);
        Assert.Equal(3m, result.Header.UnitsUp);
        Assert.Equal(3m, await OnHandAsync(x, x.P2, x.B2));
    }

    // ================================================================ validaciones

    [Fact]
    public async Task The_note_is_required_and_counts_are_not_negative()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var noNote = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(new MultiAdjustmentRequest(x.W.PublicId, "  ", new[] { Line(x.P1, x.B1, 10m, 5m) }), default));
        Assert.Contains("notes", noNote.Errors!.Keys);
        var negative = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, -1m)), default));
        Assert.Contains("lines[0].countedQty", negative.Errors!.Keys);
        var missingSystem = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, null, 5m)), default));
        Assert.Contains("lines[0].systemQty", missingSystem.Errors!.Keys);
        var dup = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 5m), Line(x.P1, x.B1, 10m, 6m)), default));
        Assert.Contains("lines[1]", dup.Errors!.Keys);
        Assert.Equal(0, await DocCountAsync(x));
    }

    [Fact]
    public async Task Serial_products_are_not_adjusted_here_and_lot_products_need_their_lot()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var serial = await x.F.AddProductAsync("PS", TrackingTypes.Serial);
        var lotProduct = await x.F.AddProductAsync("PL", TrackingTypes.Lot);
        var plain = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(serial, x.B1, 0m, 2m)), default));
        Assert.Equal(MultiAdjustmentRules.SerialNotSupported, plain.Errors!["lines[0].productPublicId"][0]);
        var noLot = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(lotProduct, x.B1, 0m, 2m)), default));
        Assert.Equal(MultiAdjustmentRules.LotRequired, noLot.Errors!["lines[0].lotId"][0]);
        var lotOnPlain = await x.F.AddLotAsync(lotProduct, "L-1");
        var notAllowed = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 5m, lotOnPlain.LotId)), default));
        Assert.Contains("lines[0].lotId", notAllowed.Errors!.Keys);
    }

    [Fact]
    public async Task A_lot_product_is_adjusted_per_lot()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var lotProduct = await x.F.AddProductAsync("PL", TrackingTypes.Lot);
        var lot = await x.F.AddLotAsync(lotProduct, "L-1");
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, lotProduct.ProductId, 5m, LotId: lot.LotId, ToWarehouseId: x.W.WarehouseId, ToBinId: x.B1.WarehouseBinId));

        var result = await svc.CreateAsync(Req(x, Line(lotProduct, x.B1, 5m, 8m, lot.LotId)), default);
        var line = Assert.Single(result.Lines);
        Assert.Equal(lot.LotId, line.LotId);
        Assert.Equal("L-1", line.LotNumber);
        Assert.Equal(3m, line.Difference);
    }

    [Fact]
    public async Task Positions_outside_the_warehouse_or_the_tenant_are_404()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var other = await x.F.AddWarehouseAsync("W2");
        var otherBin = await x.F.AddBinAsync(await x.F.AddZoneAsync(other, "PCK", ZoneTypes.Picking), "B-01");
        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, otherBin, 0m, 2m)), default));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Quarantine_positions_are_not_adjusted_here()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var quarantine = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "QUA", ZoneTypes.Quarantine), "Q-01");
        var ex = await Assert.ThrowsAnyAsync<TeikemException>(() => svc.CreateAsync(Req(x, Line(x.P1, quarantine, 0m, 2m)), default));
        Assert.Equal(422, ex.StatusCode);
    }

    // ================================================================ lectura

    [Fact]
    public async Task Stock_lists_the_positions_with_stock_and_marks_the_tracking()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var serial = await x.F.AddProductAsync("PS", TrackingTypes.Serial);
        var result = await svc.StockAsync(x.W.PublicId, new[] { x.P1.PublicId, serial.PublicId }, default);
        Assert.Equal(2, result.Count);
        var p1 = result.Single(p => p.Sku == "P1");
        Assert.Equal(new[] { "A-01", "A-02" }, p1.Positions.Select(p => p.BinCode));
        Assert.Equal(new[] { 10m, 4m }, p1.Positions.Select(p => p.QtyOnHand));
        Assert.Equal(TrackingTypes.Serial, result.Single(p => p.Sku == "PS").TrackingTypeCode);
        Assert.Empty(await svc.StockAsync(x.W.PublicId, Array.Empty<Guid>(), default));
    }

    [Fact]
    public async Task History_lists_documents_and_the_detail_keeps_what_was_seen_and_counted()
    {
        var x = await WorldAsync();
        var svc = x.F.Get<MultiAdjustmentService>();
        var first = await svc.CreateAsync(Req(x, Line(x.P1, x.B1, 10m, 12m)), default);
        await svc.CreateAsync(Req(x, Line(x.P2, x.B1, 7m, 1m)), default);

        var page = await svc.ListAsync(new MultiAdjustmentQuery(), default);
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { "AJM-000002", "AJM-000001" }, page.Items.Select(i => i.Number));
        Assert.Empty((await svc.ListAsync(new MultiAdjustmentQuery(UserIds: new[] { 99999 }), default)).Items);

        var detail = await svc.GetAsync(first.Header.PublicId, default);
        var line = Assert.Single(detail.Lines);
        Assert.Equal((10m, 12m, 2m), (line.SystemQty, line.CountedQty, line.Difference));
        Assert.Equal("P1", line.Sku);
        Assert.Equal("A-01", line.BinCode);
        Assert.Equal(first.Header.Id, (await svc.GetByIdAsync(first.Header.Id, default)).Header.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(Guid.NewGuid(), default));
    }

    // ================================================================ reglas

    [Fact]
    public void The_normal_adjustment_rejects_the_system_reason_and_the_activity_still_counts_it()
    {
        Assert.Contains(AdjustmentReasons.PhysicalCount, AdjustmentReasons.SystemAssigned);
        var (code, error) = AdjustmentRules.ValidateReason("physical_count", new[] { AdjustmentReasons.PhysicalCount, AdjustmentReasons.Found });
        Assert.Null(code);
        Assert.NotNull(error);
        Assert.Equal(NumberKinds.MultiAdjust, "MULTIADJUST");
        Assert.Equal("AJM-000123", WmsNumbering.Format(NumberKinds.MultiAdjust, 123));
    }

    [Fact]
    public void Summary_counts_only_lines_with_a_difference()
    {
        var s = MultiAdjustmentRules.Summarize(new[] { (1, 5m), (1, -2m), (2, 0m), (3, -4m) });
        Assert.Equal((3, 2, 5m, 6m), s);
        Assert.Equal(-3m, MultiAdjustmentRules.Difference(7m, 10m));
    }
}

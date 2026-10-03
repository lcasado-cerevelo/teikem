using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 21 — conteo cíclico por producto (servidor): evidencia de captura y corrección, vista previa de la reconciliación
/// (paridad con la reconciliación real), lista "Por revisar", cierre en bloque de los que cuadran y posición provisional.
/// InMemory con StatusService, InventoryLedger y WarehouseTaskWriter reales (ver CycleCountFixture): se prueba la
/// orquestación, no la concurrencia de SQL Server (eso lo cubre el smoke).
/// </summary>
public sealed class CycleCountByProductTests
{
    private const int Ana = 1;
    private const int Beto = 2;

    private static async Task<CycleCountFixture> NewAsync()
    {
        var f = await CycleCountFixture.CreateAsync();
        await f.AddUserAsync(Ana, "Ana Pérez");
        await f.AddUserAsync(Beto, "Beto Ruiz");
        f.AsUser(Ana);
        return f;
    }

    private static async Task<CycleCountDetailDto> ByProductAsync(CycleCountFixture f, Guid productPublicId)
        => await f.Get<CycleCountService>().CreateAsync(new CycleCountCreateRequest(ProductPublicIds: new[] { productPublicId }), default);

    private static CountCaptureItem Cap(CycleCountLineDto line, decimal qty) => new(line.Id, qty);

    private static async Task<CycleCountLineDto> LineAsync(CycleCountFixture f, int countId, int binId, Guid? productPublicId = null)
    {
        var detail = await f.Get<CycleCountService>().GetAsync(countId, null, default);
        return detail.Lines.Single(l => l.BinId == binId && (productPublicId is null || l.ProductPublicId == productPublicId));
    }

    // ================================================================ origen y alta por producto

    [Fact]
    public async Task Create_by_product_returns_all_positions_with_origin_product_and_manual_when_bins_are_given()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();

        var byProduct = await ByProductAsync(f, f.ProductNonePublicId);
        Assert.Equal(CycleCountOrigins.Product, byProduct.Count.OriginCode);
        Assert.Equal(new[] { f.PickBin1, f.PickBin2 }, byProduct.Lines.Select(l => l.BinId).OrderBy(x => x).ToArray());
        Assert.All(byProduct.Lines, l => Assert.Equal(f.ProductNonePublicId, l.ProductPublicId));
        Assert.All(byProduct.Lines, l => Assert.Equal(TrackingTypes.None, l.TrackingTypeCode));

        var withBins = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, ProductPublicIds: new[] { f.ProductNonePublicId }), default);
        Assert.Equal(CycleCountOrigins.Manual, withBins.Count.OriginCode);

        var page = await svc.ListPageAsync(new CycleCountQuery(Origins: new[] { CycleCountOrigins.Product }), blind: false, default);
        Assert.Equal(byProduct.Count.Id, Assert.Single(page.Items).Id);
    }

    // ================================================================ captura original y corrección

    [Fact]
    public async Task Same_user_recaptures_while_open_without_a_correction()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var line = created.Lines.Single();

        var first = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 3m) }), default)).Lines.Single();
        Assert.Equal((3m, 3m, Ana, "Ana Pérez", false), (first.CountedQty, first.CapturedQty, first.CapturedByUserId, first.CapturedByName, first.WasCorrected));
        Assert.NotNull(first.CapturedAtUtc);
        Assert.Null(first.CorrectedByName);

        var again = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 4m) }), default)).Lines.Single();
        Assert.Equal((4m, 4m, false, (string?)null), (again.CountedQty, again.CapturedQty, again.WasCorrected, again.CorrectedByName));
    }

    [Fact]
    public async Task Other_user_correction_keeps_the_original_and_returning_to_it_clears_the_correction()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var line = created.Lines.Single();
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 4m) }), default);

        f.AsUser(Beto);
        var corrected = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 6m) }), default)).Lines.Single();
        Assert.Equal(6m, corrected.CountedQty);                       // el valor vigente es el que se reconcilia
        Assert.Equal(4m, corrected.CapturedQty);                      // la captura original se conserva
        Assert.Equal("Ana Pérez", corrected.CapturedByName);
        Assert.Equal("Beto Ruiz", corrected.CorrectedByName);
        Assert.Equal(Beto, corrected.CorrectedByUserId);
        Assert.NotNull(corrected.CorrectedAtUtc);
        Assert.True(corrected.WasCorrected);
        Assert.Equal(1, (await svc.GetAsync(created.Count.Id, null, default)).Count.CorrectedLines);

        // Reenviar lo mismo no estrena otra corrección (idempotente).
        var same = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 6m) }), default)).Lines.Single();
        Assert.Equal((Beto, corrected.CorrectedAtUtc), (same.CorrectedByUserId, same.CorrectedAtUtc));

        // Volver al valor capturado limpia la corrección.
        var back = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 4m) }), default)).Lines.Single();
        Assert.Equal((4m, 4m, false, (string?)null, (DateTime?)null), (back.CountedQty, back.CapturedQty, back.WasCorrected, back.CorrectedByName, back.CorrectedAtUtc));
        Assert.Equal(0, (await svc.GetAsync(created.Count.Id, null, default)).Count.CorrectedLines);
    }

    [Fact]
    public async Task Any_edit_after_the_count_is_finished_is_a_correction_even_by_the_same_user()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var line = created.Lines.Single();
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 3m) }), default);
        await svc.FinishAsync(created.Count.Id, null, default);

        var edited = (await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 8m) }), default)).Lines.Single();
        Assert.Equal((8m, 3m, true, "Ana Pérez"), (edited.CountedQty, edited.CapturedQty, edited.WasCorrected, edited.CorrectedByName));
    }

    [Fact]
    public async Task A_correction_is_recorded_in_the_ledger_reason_and_moves_no_inventory_by_itself()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var l1 = created.Lines.Single(l => l.BinId == f.PickBin1);
        var l2 = created.Lines.Single(l => l.BinId == f.PickBin2);
        var before = await f.TxnCountAsync();
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l1, 9m), Cap(l2, 4m) }), default);
        f.AsUser(Beto);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l1, 10m) }), default);
        Assert.Equal(before, await f.TxnCountAsync());                 // corregir no mueve inventario

        var done = await svc.ReconcileAsync(created.Count.Id, null, default);
        var corrected = await f.TxnAsync(done.Lines.Single(l => l.BinId == f.PickBin1).AdjustmentTxnId!.Value);
        Assert.Equal(2m, corrected.Quantity);                           // contado vigente 10 − saldo 8
        Assert.Contains("Conteo CC-00001", corrected.Notes);
        Assert.Contains("contó 9 (Ana Pérez", corrected.Notes);
        Assert.Contains("corregido de 9 a 10 por Beto Ruiz", corrected.Notes);
        var plain = await f.TxnAsync(done.Lines.Single(l => l.BinId == f.PickBin2).AdjustmentTxnId!.Value);
        Assert.Equal("Conteo CC-00001", plain.Notes);                   // sin corrección, el motivo de siempre
        Assert.Equal(Beto, (await f.Db.Set<CycleCount>().AsNoTracking().SingleAsync()).ReconciledBy);
    }

    [Fact]
    public async Task Blind_view_keeps_the_capture_evidence_but_never_the_system_quantities()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var line = created.Lines.Single();
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 4m) }), default);
        f.AsUser(Beto);
        var detail = await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(line, 6m) }), default);

        var blind = CycleCountService.Blind(detail).Lines.Single();
        Assert.Equal((4m, 6m, true, "Beto Ruiz"), (blind.CapturedQty, blind.CountedQty, blind.WasCorrected, blind.CorrectedByName));
        Assert.Null(blind.SystemQty);
        Assert.Null(blind.CurrentQty);
        Assert.Null(blind.VarianceQty);
        Assert.Null(blind.AdjustedQty);
        // La ficha a ciegas por el servicio tampoco filtra lo esperado.
        var viaService = (await svc.GetAsync(created.Count.Id, null, blind: true, default)).Lines.Single();
        Assert.Null(viaService.SystemQty);
        Assert.Equal(4m, viaService.CapturedQty);
    }

    // ================================================================ vista previa (paridad con reconciliar)

    [Fact]
    public async Task Preview_matches_the_real_reconciliation_including_reserved_moved_lines_and_lots()
    {
        await using var f = await NewAsync();
        var lot = await f.AddLotAsync(f.ProductLotId, "L-1");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        await f.ReceiveAsync(f.ProductLotId, f.PickBin1, 4m, lot);
        await f.ReserveAsync(f.ProductNoneId, f.PickBin1, 2m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(), default);
        var pn1 = created.Lines.Single(l => l.BinId == f.PickBin1 && l.ProductPublicId == f.ProductNonePublicId);
        var pn2 = created.Lines.Single(l => l.BinId == f.PickBin2);
        var pl1 = created.Lines.Single(l => l.LotId == lot);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(pn1, 9m), Cap(pn2, 3m), Cap(pl1, 4m) }), default);
        await f.IssueAsync(f.ProductNoneId, f.PickBin2, 1m);          // la línea se movió desde la foto: hoy hay 4

        var txnsBefore = await f.TxnCountAsync();
        var preview = await svc.PreviewReconcileAsync(created.Count.Id, default);
        Assert.Equal(txnsBefore, await f.TxnCountAsync());             // la vista previa no escribe
        Assert.Equal(CycleCountStatuses.Open, (await svc.GetAsync(created.Count.Id, null, default)).Count.StatusCode);
        Assert.Null((await svc.GetAsync(created.Count.Id, null, default)).Lines.First().ReconciledSystemQty);

        var p1 = preview.Lines.Single(l => l.LineId == pn1.Id);
        Assert.Equal((8m, 2m, 9m, 1m, 9m, 1), (p1.CurrentQty, p1.ReservedQty, p1.CountedQty, p1.AdjustmentQty, p1.ResultingQty, p1.Movements));
        var p2 = preview.Lines.Single(l => l.LineId == pn2.Id);
        Assert.Equal((5m, 4m, 3m, -1m, 3m), (p2.SystemQty, p2.CurrentQty, p2.CountedQty, p2.AdjustmentQty, p2.ResultingQty));
        Assert.True(p2.SystemQtyChanged);                              // la existencia se movió desde la foto
        var pLot = preview.Lines.Single(l => l.LineId == pl1.Id);
        Assert.Equal(("L-1", 0m, 0), (pLot.LotNumber, pLot.AdjustmentQty, pLot.Movements));
        Assert.Equal(new ReconcilePreviewTotalsDto(3, 0, 2, 2, 0, false, CycleCountStatuses.ReconciledVariance), preview.Totals);

        var done = await svc.ReconcileAsync(created.Count.Id, null, default);
        foreach (var l in done.Lines)
        {
            var p = preview.Lines.Single(x => x.LineId == l.Id);
            Assert.Equal(p.AdjustmentQty, l.AdjustedQty ?? 0m);                  // mismos ajustes
            Assert.Equal(p.CurrentQty, l.ReconciledSystemQty);                    // misma base (existencia actual)
        }
        Assert.Equal(preview.Totals.Movements, await f.TxnCountAsync() - txnsBefore);
        Assert.Equal(preview.Totals.ResultStatusCode, done.Count.StatusCode);
    }

    [Fact]
    public async Task Preview_reports_the_reserved_error_per_line_exactly_like_the_reconciliation_and_pending_lines_are_data_not_errors()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        await f.ReserveAsync(f.ProductNoneId, f.PickBin1, 3m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var l1 = created.Lines.Single(l => l.BinId == f.PickBin1);
        var l2 = created.Lines.Single(l => l.BinId == f.PickBin2);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l1, 2m) }), default);   // 2 < 3 reservados

        var partial = await svc.PreviewReconcileAsync(created.Count.Id, default);
        Assert.Equal(1, partial.Totals.PendingLines);
        Assert.True(partial.Lines.Single(l => l.LineId == l2.Id).IsPending);
        Assert.Null(partial.Lines.Single(l => l.LineId == l2.Id).Error);
        Assert.False(partial.Totals.Matches);
        Assert.Null(partial.Totals.ResultStatusCode);

        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l2, 5m) }), default);
        var preview = await svc.PreviewReconcileAsync(created.Count.Id, default);
        var error = preview.Lines.Single(l => l.LineId == l1.Id).Error;
        Assert.Equal(CycleCountRules.ReservedAboveCount("PN", "A01-R01-N1-P01", 2m, 3m), error);
        Assert.Equal((1, false), (preview.Totals.ErrorLines, preview.Totals.Matches));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal(error, ex.Message);
        Assert.Equal(error, ex.Errors!["lines[0]"][0]);
    }

    [Fact]
    public async Task Preview_of_serial_lines_summarizes_removals_additions_and_transfers_like_the_reconciliation()
    {
        await using var f = await NewAsync();
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S1");
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin1, "S2");
        await f.ReceiveSerialAsync(f.ProductSerialId, f.PickBin2, "S3");
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, ProductPublicIds: new[] { f.ProductSerialPublicId }), default);
        var line = created.Lines.Single();
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { new CountCaptureItem(line.Id, null, new[] { "S2", "S3", "S9" }) }), default);

        var before = await f.TxnCountAsync();
        var preview = await svc.PreviewReconcileAsync(created.Count.Id, default);
        var p = Assert.Single(preview.Lines);
        Assert.Equal(new[] { "S1" }, p.Serials!.Removals);
        Assert.Equal(new[] { "S9" }, p.Serials.Additions);
        Assert.Equal(new[] { "S3" }, p.Serials.Transfers);
        Assert.Equal((3, 1m), (p.Movements, p.AdjustmentQty));          // −1 baja, +1 alta, +1 traslado hacia la posición
        Assert.Equal(before, await f.TxnCountAsync());

        await svc.ReconcileAsync(created.Count.Id, null, default);
        Assert.Equal(p.Movements, await f.TxnCountAsync() - before);
    }

    [Fact]
    public async Task Preview_of_a_reconciled_count_is_422_and_of_another_company_is_404()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(created.Lines.Single(), 8m) }), default);

        using (f.Tenant.As(2))
            await Assert.ThrowsAsync<NotFoundException>(() => svc.PreviewReconcileAsync(created.Count.Id, default));

        await svc.ReconcileAsync(created.Count.Id, null, default);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.PreviewReconcileAsync(created.Count.Id, default));
        Assert.Equal(CycleCountRules.CountNotOpen, ex.Message);
    }

    // ================================================================ cierre en bloque

    [Fact]
    public async Task Bulk_close_closes_only_the_matching_counts_against_the_current_stock_and_reports_why_the_others_stay()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();

        async Task<int> CountAsync(int binId, decimal qty, bool finish = true)
        {
            var c = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { binId }, ProductPublicIds: new[] { f.ProductNonePublicId }), default);
            await svc.CaptureAsync(c.Count.Id, new CountCaptureRequest(new[] { Cap(c.Lines.Single(), qty) }), default);
            if (finish) await svc.FinishAsync(c.Count.Id, null, default);
            return c.Count.Id;
        }

        var matches = await CountAsync(f.PickBin1, 8m);          // cuadra
        var differs = await CountAsync(f.PickBin1, 9m);          // asentaría +1
        var movedAway = await CountAsync(f.PickBin2, 5m);        // cuadraba con la foto pero el saldo se movió: hoy asentaría −1
        var movedBack = await CountAsync(f.PickBin2, 4m);        // la foto era 5 pero hoy hay 4: cuadra contra la existencia actual
        var stillOpen = await CountAsync(f.PickBin1, 8m, finish: false);
        await f.IssueAsync(f.ProductNoneId, f.PickBin2, 1m);

        var txnsBefore = await f.TxnCountAsync();
        var result = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(Comment: "Cierre rápido"), default);

        Assert.Equal(new[] { matches, movedBack }, result.Closed.Select(c => c.Id).OrderBy(x => x).ToArray());
        Assert.All(result.Closed, c => Assert.Equal(CycleCountStatuses.Reconciled, c.StatusCode));
        var wouldPost = result.Skipped.Where(s => s.ReasonCode == CycleCountService.SkipWouldPost).ToDictionary(s => s.Id);
        Assert.Equal(new[] { differs, movedAway }, wouldPost.Keys.OrderBy(x => x).ToArray());
        Assert.All(wouldPost.Values, s => Assert.Equal(1, s.Count));
        Assert.Equal(4, result.Examined);                              // el Pendiente sin terminar no es candidato
        Assert.DoesNotContain(result.Skipped, s => s.Id == stillOpen);
        Assert.False(result.Truncated);

        Assert.Equal(txnsBefore, await f.TxnCountAsync());             // los que cuadran no asientan nada
        Assert.Equal(new[] { CycleCountStatuses.Open, CycleCountStatuses.Counted, CycleCountStatuses.Reconciled }, await f.HistoryAsync(matches));
        Assert.Equal(WarehouseTaskStatuses.Done, await f.CountTaskStatusAsync(matches));
        var back = await svc.GetAsync(movedBack, null, default);
        Assert.True(back.Lines.Single().SystemQtyChanged);             // la marca de foto vieja se asienta igual
        Assert.Equal(CycleCountStatuses.Counted, (await svc.GetAsync(differs, null, default)).Count.StatusCode);
        Assert.Equal(CycleCountStatuses.Counted, (await svc.GetAsync(movedAway, null, default)).Count.StatusCode);
        Assert.Equal(CycleCountStatuses.Open, (await svc.GetAsync(stillOpen, null, default)).Count.StatusCode);

        // Segunda pasada con ids: lo ya reconciliado y lo inexistente se informan; includeOpen cierra el Pendiente capturado.
        var again = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(Ids: new[] { matches, 9999, stillOpen }, IncludeOpen: true), default);
        Assert.Equal(new[] { stillOpen }, again.Closed.Select(c => c.Id).ToArray());
        Assert.Equal(CycleCountService.SkipAlreadyReconciled, again.Skipped.Single(s => s.Id == matches).ReasonCode);
        Assert.Equal(CycleCountService.SkipNotFound, again.Skipped.Single(s => s.Id == 9999).ReasonCode);
        var notCounted = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(Ids: new[] { await CountAsync(f.PickBin1, 8m, finish: false) }), default);
        Assert.Equal(CycleCountService.SkipNotCounted, Assert.Single(notCounted.Skipped).ReasonCode);
        Assert.Empty(notCounted.Closed);
    }

    [Fact]
    public async Task Bulk_close_reports_pending_lines_and_reserved_errors_and_filters_by_warehouse()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        await f.ReserveAsync(f.ProductNoneId, f.PickBin1, 3m);
        var svc = f.Get<CycleCountService>();

        // Con error de reservado: contado 8 (cuadra) pero el reservado… 8 ≥ 3 → cuadra; para el error se cuenta 2.
        var errorCount = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        await svc.CaptureAsync(errorCount.Count.Id, new CountCaptureRequest(new[] { Cap(errorCount.Lines.Single(), 2m) }), default);
        await svc.FinishAsync(errorCount.Count.Id, null, default);

        // Contado con una línea que se vuelve a pendiente por "Refrescar" (la foto quedó vieja).
        var pending = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin2 }), default);
        await svc.CaptureAsync(pending.Count.Id, new CountCaptureRequest(new[] { Cap(pending.Lines.Single(), 5m) }), default);
        await svc.FinishAsync(pending.Count.Id, null, default);
        await f.IssueAsync(f.ProductNoneId, f.PickBin2, 1m);
        await svc.RefreshAsync(pending.Count.Id, default);

        await f.AddOtherWarehouseAsync();      // con dos almacenes activos hay que indicar el almacén al crear; los conteos ya existen
        var result = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(), default);
        Assert.Empty(result.Closed);
        var err = result.Skipped.Single(s => s.Id == errorCount.Count.Id);
        Assert.Equal((CycleCountService.SkipErrors, 1), (err.ReasonCode, err.Count));
        Assert.Equal(CycleCountRules.ReservedAboveCount("PN", "A01-R01-N1-P01", 2m, 3m), err.Reason);
        var pend = result.Skipped.Single(s => s.Id == pending.Count.Id);
        Assert.Equal((CycleCountService.SkipPending, 1, CycleCountRules.CountIncomplete(1)), (pend.ReasonCode, pend.Count, pend.Reason));

        // Acotar por almacén: el segundo no tiene conteos → nada examinado.
        var wh2 = await f.Db.Set<Warehouse>().AsNoTracking().SingleAsync(w => w.WarehouseId == 20);
        var none = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(WarehousePublicId: wh2.PublicId), default);
        Assert.Equal(0, none.Examined);
        Assert.Empty(none.Closed);
    }

    [Fact]
    public async Task Bulk_close_never_touches_another_companys_counts()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(created.Lines.Single(), 8m) }), default);
        await svc.FinishAsync(created.Count.Id, null, default);

        using (f.Tenant.As(2))
        {
            var byIds = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(Ids: new[] { created.Count.Id }), default);
            Assert.Empty(byIds.Closed);
            Assert.Equal(CycleCountService.SkipNotFound, Assert.Single(byIds.Skipped).ReasonCode);
            var sweep = await svc.ReconcileMatchingAsync(new CountReconcileMatchingRequest(), default);
            Assert.Equal(0, sweep.Examined);
            Assert.Empty((await svc.ReviewAsync(new CycleCountReviewQuery(), default)).Items);
        }
        Assert.Equal(CycleCountStatuses.Counted, (await svc.GetAsync(created.Count.Id, null, default)).Count.StatusCode);
    }

    [Fact]
    public async Task Bulk_close_with_more_than_200_ids_is_400()
    {
        await using var f = await NewAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            f.Get<CycleCountService>().ReconcileMatchingAsync(new CountReconcileMatchingRequest(Ids: Enumerable.Range(1, 201).ToArray()), default));
        Assert.Equal(CycleCountRules.BulkTooMany, ex.Errors!["ids"][0]);
    }

    // ================================================================ lista "Por revisar"

    [Fact]
    public async Task Review_lists_counted_counts_with_who_counted_product_summary_differences_and_matches()
    {
        await using var f = await NewAsync();
        var lot = await f.AddLotAsync(f.ProductLotId, "L-9");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        await f.ReceiveAsync(f.ProductLotId, f.PickBin1, 4m, lot);
        var svc = f.Get<CycleCountService>();

        // Conteo 1 (Ana): dos productos en una posición; uno difiere. Conteo 2 (Beto): un producto en dos posiciones, cuadra.
        var c1 = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default);
        var pn = c1.Lines.Single(l => l.Sku == "PN");
        var pl = c1.Lines.Single(l => l.Sku == "PL");
        await svc.CaptureAsync(c1.Count.Id, new CountCaptureRequest(new[] { Cap(pn, 7m), Cap(pl, 4m) }), default);
        await svc.FinishAsync(c1.Count.Id, null, default);

        f.AsUser(Beto);
        var c2 = await svc.CreateAsync(new CycleCountCreateRequest(ProductPublicIds: new[] { f.ProductNonePublicId }, BinIds: new[] { f.PickBin2 }), default);
        await svc.CaptureAsync(c2.Count.Id, new CountCaptureRequest(new[] { Cap(c2.Lines.Single(), 5m) }), default);
        await svc.FinishAsync(c2.Count.Id, null, default);
        var openOnly = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin2 }), default);   // sin capturar: no aparece

        var page = await svc.ReviewAsync(new CycleCountReviewQuery(), default);
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { c2.Count.Id, c1.Count.Id }, page.Items.Select(i => i.Count.Id).ToArray());   // más reciente primero
        var i1 = page.Items.Single(i => i.Count.Id == c1.Count.Id);
        Assert.Equal((Ana, "Ana Pérez", 1), (i1.CountedByUserId, i1.CountedByName, i1.CountedByCount));
        Assert.Equal(("PL", 1, 1, 2), (i1.FirstProductSku, i1.OtherProducts, i1.Positions, i1.Lines));       // primero por SKU
        Assert.Equal((1, 1, false), (i1.DifferingLines, i1.Movements, i1.Matches));
        var i2 = page.Items.Single(i => i.Count.Id == c2.Count.Id);
        Assert.Equal((Beto, "PN", 0, 1, 0, true), (i2.CountedByUserId, i2.FirstProductSku, i2.OtherProducts, i2.Lines, i2.DifferingLines, i2.Matches));

        Assert.Equal(c1.Count.Id, Assert.Single((await svc.ReviewAsync(new CycleCountReviewQuery(CountedByUserId: Ana), default)).Items).Count.Id);
        Assert.Empty((await svc.ReviewAsync(new CycleCountReviewQuery(CountedByUserId: 77), default)).Items);
        Assert.Single((await svc.ReviewAsync(new CycleCountReviewQuery(Search: "PL"), default)).Items);
        Assert.Equal(1, (await svc.ReviewAsync(new CycleCountReviewQuery(Skip: 1, Take: 1), default)).Items.Count);

        // Un Pendiente solo cuenta con includeOpen y cuando ya tiene todas sus líneas capturadas.
        var openLine = openOnly.Lines.Single(l => l.Sku == "PN");
        await svc.CaptureAsync(openOnly.Count.Id, new CountCaptureRequest(new[] { Cap(openLine, 5m) }), default);
        Assert.Equal(2, (await svc.ReviewAsync(new CycleCountReviewQuery(), default)).Total);
        var withOpen = await svc.ReviewAsync(new CycleCountReviewQuery(IncludeOpen: true), default);
        Assert.Equal(3, withOpen.Total);
        Assert.True(withOpen.Items.Single(i => i.Count.Id == openOnly.Count.Id).Matches);
    }

    // ================================================================ posición provisional

    [Fact]
    public async Task Provisional_bin_is_created_from_an_open_count_used_as_a_new_line_and_confirmed_by_the_supervisor()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var layout = f.Get<WarehouseLayoutService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);

        var bin = await layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "PROV-01"), default);
        Assert.True(bin.IsProvisional);
        Assert.Equal(("PROV-01", created.Count.Id), (bin.Code, bin.ProvisionalCycleCountId));
        Assert.NotNull(bin.ProvisionalCreatedAtUtc);
        var stored = await f.Db.Set<WarehouseBin>().AsNoTracking().SingleAsync(b => b.WarehouseBinId == bin.Id);
        Assert.Equal((Ana, true), (stored.ProvisionalCreatedBy, stored.IsActive));

        // Se usa de inmediato como línea nueva del conteo (lo hallado donde el sistema no tenía nada).
        var detail = await svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: created.Lines.Single().Id, CountedQty: 8m),
            new CountBatchItem(BinId: bin.Id, ProductPublicId: f.ProductNonePublicId, CountedQty: 2m),
        }), default);
        var extra = detail.Lines.Single(l => l.BinId == bin.Id);
        Assert.True(extra.BinIsProvisional);
        Assert.Equal((0m, 2m, Ana), (extra.SystemQty, extra.CountedQty, extra.CapturedByUserId));
        Assert.False(detail.Lines.Single(l => l.BinId == f.PickBin1).BinIsProvisional);
        var done = await svc.ReconcileAsync(created.Count.Id, null, default);
        Assert.Equal(2m, await f.OnHandAsync(f.ProductNoneId, bin.Id));                           // ya existe en el inventario
        Assert.Equal(CycleCountStatuses.ReconciledVariance, done.Count.StatusCode);

        // El listado la marca y el filtro la encuentra; el supervisor la confirma una sola vez.
        var wh = await f.Db.Set<Warehouse>().AsNoTracking().SingleAsync(w => w.WarehouseId == f.WarehouseId);
        var pending = await layout.ListBinsAsync(wh.PublicId, new WarehouseBinQuery(IsProvisional: true), default);
        Assert.Equal(bin.Id, Assert.Single(pending.Items).Id);
        Assert.Equal(2, (await layout.ListBinsAsync(wh.PublicId, new WarehouseBinQuery(IsProvisional: false), default)).Total);
        var confirmed = await layout.ConfirmProvisionalBinAsync(wh.PublicId, bin.Id, default);
        Assert.False(confirmed.IsProvisional);
        Assert.Equal(created.Count.Id, confirmed.ProvisionalCycleCountId);                        // el rastro de origen se conserva
        Assert.Empty((await layout.ListBinsAsync(wh.PublicId, new WarehouseBinQuery(IsProvisional: true), default)).Items);
        var again = await Assert.ThrowsAsync<ConflictException>(() => layout.ConfirmProvisionalBinAsync(wh.PublicId, bin.Id, default));
        Assert.Equal(WarehouseRules.BinNotProvisional, again.Message);
    }

    [Fact]
    public async Task Provisional_bin_rules_duplicate_code_composed_code_closed_count_other_warehouse_zone_and_other_company()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var layout = f.Get<WarehouseLayoutService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var other = await f.AddOtherWarehouseAsync();

        // Código compuesto por pasillo-rack-nivel-posición, como el alta de posición.
        var composed = await layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, null, "B1", "R2", "N3", "P4"), default);
        Assert.Equal(("B1-R2-N3-P4", true), (composed.Code, composed.IsProvisional));

        // Único por almacén → 409 con el mensaje del alta; también contra una posición definitiva.
        var dup = await Assert.ThrowsAsync<ConflictException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "B1-R2-N3-P4"), default));
        Assert.Equal(WarehouseRules.DuplicateBinMessage, dup.Message);
        await Assert.ThrowsAsync<ConflictException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "A01-R01-N1-P01"), default));

        // Datos incompletos → 400; zona de otro almacén → 404 (no se mezcla con el almacén del conteo).
        await Assert.ThrowsAsync<ValidationException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(null, "X-1"), default));
        await Assert.ThrowsAsync<ValidationException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11), default));
        await Assert.ThrowsAsync<NotFoundException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(other.ZoneId, "X-2"), default));
        Assert.False(await f.Db.Set<WarehouseBin>().AnyAsync(b => b.Code == "X-2"));

        // Conteo inexistente o de otra compañía → 404.
        await Assert.ThrowsAsync<NotFoundException>(() => layout.CreateProvisionalBinAsync(9999, new WarehouseBinRequest(11, "X-3"), default));
        using (f.Tenant.As(2))
            await Assert.ThrowsAsync<NotFoundException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "X-4"), default));

        // Conteo ya reconciliado → 422; ni Pendiente ni Contado pueden estar cerrados.
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(created.Lines.Single(), 8m) }), default);
        await svc.ReconcileAsync(created.Count.Id, null, default);
        var closed = await Assert.ThrowsAsync<StatusRuleException>(() => layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "X-5"), default));
        Assert.Equal(CycleCountRules.ProvisionalCountClosed, closed.Message);
    }

    [Fact]
    public async Task Provisional_flag_reaches_the_bin_search_and_the_sync_contract_shape()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var layout = f.Get<WarehouseLayoutService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        await layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "PROV-9"), default);

        var found = await layout.SearchBinsAsync("PROV", null, false, 20, default);
        Assert.True(Assert.Single(found).IsProvisional);
        Assert.False((await layout.SearchBinsAsync("A01-R01-N1-P01", null, false, 20, default)).Single().IsProvisional);
    }
}

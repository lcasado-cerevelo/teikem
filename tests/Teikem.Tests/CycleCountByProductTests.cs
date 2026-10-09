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

    // ---- Cambio 2 (2026-10-03): la corrección del supervisor se protege de la recaptura del operario

    private const int Carla = 3;
    private const string Locked = "La línea ya fue corregida por el supervisor; no se puede volver a capturar.";

    /// <summary>Ana captura 4 y 5 en dos posiciones; Beto (warehouse.count) corrige la primera a 6. Devuelve el conteo y sus líneas.</summary>
    private static async Task<(CycleCountFixture F, int CountId, CycleCountLineDto Corrected, CycleCountLineDto Plain)> CorrectedAsync()
    {
        var f = await NewAsync();
        await f.AddUserAsync(Carla, "Carla Soto");
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();
        var created = await ByProductAsync(f, f.ProductNonePublicId);
        var l1 = created.Lines.Single(l => l.BinId == f.PickBin1);
        var l2 = created.Lines.Single(l => l.BinId == f.PickBin2);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l1, 4m), Cap(l2, 5m) }), default);
        f.AsRestrictedUser(Beto, PermissionCatalog.WarehouseCount, PermissionCatalog.WarehouseCountCapture);
        await svc.CaptureAsync(created.Count.Id, new CountCaptureRequest(new[] { Cap(l1, 6m) }), default);
        return (f, created.Count.Id, l1, l2);
    }

    [Fact]
    public async Task Operator_cannot_recapture_a_line_the_supervisor_corrected_and_the_correction_survives()
    {
        var (f, id, l1, _) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l1, 3m) }), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(Locked, ex.Message);
        var after = (await svc.GetAsync(id, null, default)).Lines.Single(l => l.Id == l1.Id);
        Assert.Equal((6m, 4m, Beto, true), (after.CountedQty, after.CapturedQty, after.CorrectedByUserId, after.WasCorrected));

        // Reenviar el valor vigente (reintento de la cola de salida) no cambia nada y no se rechaza.
        await svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l1, 6m) }), default);
    }

    [Fact]
    public async Task Supervisor_corrects_again_and_a_third_user_with_count_permission_too_but_not_one_with_only_capture()
    {
        var (f, id, l1, _) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();

        // Quien corrigió, aunque ahora solo tenga capturar, vuelve a corregir.
        f.AsRestrictedUser(Beto, PermissionCatalog.WarehouseCountCapture);
        var again = (await svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l1, 7m) }), default)).Lines.Single(l => l.Id == l1.Id);
        Assert.Equal((7m, 4m, Beto, true), (again.CountedQty, again.CapturedQty, again.CorrectedByUserId, again.WasCorrected));

        // Otra persona con solo capturar: rechazada.
        f.AsRestrictedUser(Carla, PermissionCatalog.WarehouseCountCapture);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l1, 1m) }), default));
        Assert.Equal(Locked, ex.Message);

        // Otra persona con warehouse.count: corrige (sigue siendo una corrección y actualiza Corrected*).
        f.AsRestrictedUser(Carla, PermissionCatalog.WarehouseCount, PermissionCatalog.WarehouseCountCapture);
        var third = (await svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l1, 8m) }), default)).Lines.Single(l => l.Id == l1.Id);
        Assert.Equal((8m, 4m, Carla, true), (third.CountedQty, third.CapturedQty, third.CorrectedByUserId, third.WasCorrected));
    }

    [Fact]
    public async Task Operator_can_still_recapture_a_line_without_correction()
    {
        var (f, id, _, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var line = (await svc.CaptureAsync(id, new CountCaptureRequest(new[] { Cap(l2, 2m) }), default)).Lines.Single(l => l.Id == l2.Id);
        Assert.Equal((2m, 2m, false), (line.CountedQty, line.CapturedQty, line.WasCorrected));
    }

    // ---- Segundo bloque (2026-10-03): un lote con línea corregida guarda las libres y omite la bloqueada

    [Fact]
    public async Task Mixed_batch_saves_the_free_lines_and_skips_the_corrected_one_with_the_exact_detail()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var detail = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l2.Id, CountedQty: 1m),      // libre: se guarda
            new CountBatchItem(LineId: l1.Id, CountedQty: 3m),      // corregida: se omite
        }), default);

        Assert.Equal(1m, detail.Lines.Single(l => l.Id == l2.Id).CountedQty);
        Assert.Equal(6m, detail.Lines.Single(l => l.Id == l1.Id).CountedQty);   // la corrección sobrevive
        var skipped = Assert.Single(detail.SkippedLines!);
        Assert.Equal(new CountSkippedLineDto(l1.Id, l1.BinCode, l1.Sku, null, 3m, 6m, "CORRECTED_BY_SUPERVISOR", Locked), skipped);
        // Lo guardado también está en la base (no solo en la respuesta).
        Assert.Equal(1m, (await svc.GetAsync(id, null, default)).Lines.Single(l => l.Id == l2.Id).CountedQty);

        // Un lote sin líneas corregidas no trae skippedLines.
        var ok = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[] { new CountBatchItem(LineId: l2.Id, CountedQty: 2m) }), default);
        Assert.Null(ok.SkippedLines);
    }

    [Fact]
    public async Task Batch_with_all_lines_corrected_is_409_with_the_usual_message_and_saves_nothing()
    {
        var (f, id, l1, _) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l1.Id, CountedQty: 3m),
        }), default));
        Assert.Equal(409, ex.StatusCode);
        Assert.StartsWith(Locked, ex.Message);
        Assert.Contains("Renglón(es) del lote: 1 (", ex.Message);
        Assert.EndsWith("No se guardó nada.", ex.Message);
        Assert.Equal(6m, (await svc.GetAsync(id, null, default)).Lines.Single(l => l.Id == l1.Id).CountedQty);
    }

    [Fact]
    public async Task Supervisor_and_the_user_who_corrected_still_capture_the_corrected_line_in_a_batch()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();

        f.AsRestrictedUser(Beto, PermissionCatalog.WarehouseCountCapture);
        var byCorrector = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[] { new CountBatchItem(LineId: l1.Id, CountedQty: 9m) }), default);
        Assert.Null(byCorrector.SkippedLines);
        Assert.Equal(9m, byCorrector.Lines.Single(l => l.Id == l1.Id).CountedQty);

        f.AsRestrictedUser(Carla, PermissionCatalog.WarehouseCount, PermissionCatalog.WarehouseCountCapture);
        var bySupervisor = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l1.Id, CountedQty: 8m), new CountBatchItem(LineId: l2.Id, CountedQty: 2m),
        }), default);
        Assert.Null(bySupervisor.SkippedLines);
        Assert.Equal(8m, bySupervisor.Lines.Single(l => l.Id == l1.Id).CountedQty);
    }

    [Fact]
    public async Task Skipped_lines_survive_the_blind_mapping_without_leaking_the_expected_quantity()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);
        var detail = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l2.Id, CountedQty: 1m), new CountBatchItem(LineId: l1.Id, CountedQty: 3m),
        }), default);

        var blind = CycleCountService.Blind(detail);
        Assert.True(blind.IsBlind);
        var skipped = Assert.Single(blind.SkippedLines!);
        Assert.Equal((l1.Id, 3m, 6m), (skipped.LineId, skipped.SentQty, skipped.CurrentQty));   // la corregida se muestra también a ciegas
        Assert.All(blind.Lines, l => Assert.Null(l.SystemQty));
        // El DTO de la línea omitida no tiene ningún campo de lo esperado (8 = saldo de la posición, 5 = foto de la otra).
        var json = System.Text.Json.JsonSerializer.Serialize(skipped);
        Assert.DoesNotContain("systemQty", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expected", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resending_the_current_value_of_a_corrected_line_is_not_skipped_and_a_batch_of_only_that_passes()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var detail = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l1.Id, CountedQty: 6m), new CountBatchItem(LineId: l2.Id, CountedQty: 2m),
        }), default);
        Assert.Null(detail.SkippedLines);
        Assert.Equal((6m, 2m), (detail.Lines.Single(l => l.Id == l1.Id).CountedQty, detail.Lines.Single(l => l.Id == l2.Id).CountedQty));
        var only = await svc.CaptureBatchAsync(id, new CountBatchRequest(new[] { new CountBatchItem(LineId: l1.Id, CountedQty: 6m) }), default);
        Assert.Null(only.SkippedLines);
    }

    [Fact]
    public async Task Validation_error_in_a_free_line_saves_nothing_even_with_a_skipped_line()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
        {
            new CountBatchItem(LineId: l1.Id, CountedQty: 3m),
            new CountBatchItem(LineId: l2.Id, CountedQty: -1m),     // libre pero inválida
        }), default));
        Assert.Contains(ex.Errors.Keys, k => k.StartsWith("lines[1]"));
        var lines = (await svc.GetAsync(id, null, default)).Lines;
        Assert.Equal(5m, lines.Single(l => l.Id == l2.Id).CountedQty);
        Assert.Equal(6m, lines.Single(l => l.Id == l1.Id).CountedQty);
    }

    [Fact]
    public async Task Batch_with_a_corrected_line_of_another_company_is_404()
    {
        var (f, id, l1, l2) = await CorrectedAsync();
        await using var _f = f;
        var svc = f.Get<CycleCountService>();
        f.AsRestrictedUser(Ana, PermissionCatalog.WarehouseCountCapture);
        using (f.Tenant.As(2))
            await Assert.ThrowsAsync<NotFoundException>(() => svc.CaptureBatchAsync(id, new CountBatchRequest(new[]
            {
                new CountBatchItem(LineId: l2.Id, CountedQty: 1m), new CountBatchItem(LineId: l1.Id, CountedQty: 3m),
            }), default));
        Assert.Equal(5m, (await svc.GetAsync(id, null, default)).Lines.Single(l => l.Id == l2.Id).CountedQty);
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
    // ================================================================ conteo vacío (adenda: allowEmpty)

    private static Task<CycleCountDetailDto> EmptyByProductAsync(CycleCountFixture f, Guid productPublicId)
        => f.Get<CycleCountService>().CreateAsync(new CycleCountCreateRequest(ProductPublicIds: new[] { productPublicId }, AllowEmpty: true), default);

    [Fact]
    public async Task Without_allow_empty_a_product_without_stock_is_still_400()
    {
        await using var f = await NewAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => ByProductAsync(f, f.ProductNonePublicId));
        Assert.Contains(CycleCountRules.NothingSelected, ex.Message + string.Join(" ", ex.Errors!.SelectMany(e => e.Value)));
        Assert.False(await f.Db.Set<CycleCount>().AnyAsync());
    }

    [Fact]
    public async Task Allow_empty_creates_an_empty_open_count_with_origin_product_and_its_task()
    {
        await using var f = await NewAsync();
        var created = await EmptyByProductAsync(f, f.ProductNonePublicId);
        Assert.Equal((CycleCountOrigins.Product, CycleCountStatuses.Open, 0, 0), (created.Count.OriginCode, created.Count.StatusCode, created.Count.LineCount, created.Lines.Count));
        Assert.NotNull(created.Count.TaskId);

        // Con existencia, allowEmpty no cambia nada: una línea por posición, como siempre.
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var withStock = await EmptyByProductAsync(f, f.ProductNonePublicId);
        Assert.Single(withStock.Lines);
        Assert.Equal(CycleCountOrigins.Product, withStock.Count.OriginCode);
    }

    [Fact]
    public async Task Allow_empty_with_one_bin_opens_that_bin_even_if_the_system_thinks_it_is_empty()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        // sin AllowEmpty: la posición vacía sigue rechazada
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }), default));
        // con AllowEmpty: conteo vacío de origen manual (por posición), con su tarea
        var created = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, AllowEmpty: true), default);
        Assert.Equal((CycleCountOrigins.Manual, CycleCountStatuses.Open, 0), (created.Count.OriginCode, created.Count.StatusCode, created.Lines.Count));
        Assert.NotNull(created.Count.TaskId);
        // con existencia: sus líneas de siempre
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var withStock = await svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1 }, AllowEmpty: true), default);
        Assert.Single(withStock.Lines);
        // dos posiciones o una posición con producto siguen siendo 400
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new CycleCountCreateRequest(BinIds: new[] { f.PickBin1, f.PickBin1 + 1 }, AllowEmpty: true), default));
    }

    [Fact]
    public async Task Allow_empty_unknown_product_is_404_and_incompatible_filters_are_400()
    {
        await using var f = await NewAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => EmptyByProductAsync(f, Guid.NewGuid()));
        var svc = f.Get<CycleCountService>();
        var p = new[] { f.ProductNonePublicId };
        foreach (var req in new[]
        {
            new CycleCountCreateRequest(ProductPublicIds: p, BinIds: new[] { f.PickBin1 }, AllowEmpty: true),
            new CycleCountCreateRequest(ProductPublicIds: p, ZoneIds: new[] { 11 }, AllowEmpty: true),
            new CycleCountCreateRequest(ProductPublicIds: p, CategoryIds: new[] { 1 }, AllowEmpty: true),
            new CycleCountCreateRequest(ProductPublicIds: new[] { f.ProductNonePublicId, f.ProductLotPublicId }, AllowEmpty: true),
        })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(req, default));
            Assert.Contains(CycleCountRules.AllowEmptyOnlyOneProduct, string.Join(" ", ex.Errors!.SelectMany(e => e.Value)));
        }
        Assert.False(await f.Db.Set<CycleCount>().AnyAsync());
    }

    [Fact]
    public async Task Empty_count_accepts_a_new_line_in_a_provisional_bin_and_finishes_and_reconciles()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        var layout = f.Get<WarehouseLayoutService>();
        var created = await EmptyByProductAsync(f, f.ProductNonePublicId);

        // Mientras siga vacío no se puede terminar ni aparece en "Por revisar".
        var empty = await Assert.ThrowsAsync<StatusRuleException>(() => svc.FinishAsync(created.Count.Id, null, default));
        Assert.Equal(CycleCountRules.NoLines, empty.Message);
        Assert.Empty((await svc.ReviewAsync(new CycleCountReviewQuery(IncludeOpen: true), default)).Items);

        var bin = await layout.CreateProvisionalBinAsync(created.Count.Id, new WarehouseBinRequest(11, "PROV-E1"), default);
        var detail = await svc.CaptureBatchAsync(created.Count.Id, new CountBatchRequest(new[]
        {
            new CountBatchItem(BinId: bin.Id, ProductPublicId: f.ProductNonePublicId, CountedQty: 3m),
        }), default);
        var line = Assert.Single(detail.Lines);
        Assert.Equal((0m, 3m, true), (line.SystemQty, line.CountedQty, line.BinIsProvisional));
        Assert.Single((await svc.ReviewAsync(new CycleCountReviewQuery(IncludeOpen: true), default)).Items);

        var finished = await svc.FinishAsync(created.Count.Id, null, default);
        Assert.Equal(CycleCountStatuses.Counted, finished.Count.StatusCode);
        var done = await svc.ReconcileAsync(created.Count.Id, null, default);
        Assert.Equal(CycleCountStatuses.ReconciledVariance, done.Count.StatusCode);
        Assert.Equal(3m, await f.OnHandAsync(f.ProductNoneId, bin.Id));
    }

    [Fact]
    public async Task Empty_count_accepts_a_line_through_add_line_and_reconcile_empty_fails()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        var created = await EmptyByProductAsync(f, f.ProductNonePublicId);
        var noLines = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ReconcileAsync(created.Count.Id, null, default));
        Assert.Equal(CycleCountRules.NoLines, noLines.Message);

        var added = await svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(f.PickBin1, f.ProductNonePublicId, CountedQty: 2m), default);
        Assert.Equal(2m, Assert.Single(added.Lines).CountedQty);
        Assert.Equal(CycleCountStatuses.Counted, (await svc.FinishAsync(created.Count.Id, null, default)).Count.StatusCode);
    }

    [Fact]
    public async Task Empty_count_can_be_deleted_while_open_and_other_company_cannot_see_or_touch_it()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        var created = await EmptyByProductAsync(f, f.ProductNonePublicId);

        using (f.Tenant.As(2))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => svc.DeleteAsync(created.Count.Id, default));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(f.PickBin1, f.ProductNonePublicId, CountedQty: 1m), default));
        }

        await svc.DeleteAsync(created.Count.Id, default);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync(created.Count.Id, null, default));
        var cc = await f.Db.Set<CycleCount>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.CycleCountId == created.Count.Id);
        Assert.False(cc.IsActive);
    }

    // ================================================================ Lote 24: conteo abierto con varios productos y posición opcional

    [Fact]
    public async Task Allow_empty_without_products_opens_an_empty_count_not_the_whole_warehouse()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);   // hay existencia: aun así el conteo nace vacío
        var created = await f.Get<CycleCountService>().CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);
        Assert.Equal((CycleCountOrigins.Product, CycleCountStatuses.Open, 0), (created.Count.OriginCode, created.Count.StatusCode, created.Lines.Count));
        Assert.NotNull(created.Count.TaskId);
    }

    [Fact]
    public async Task Product_bins_lists_positions_with_stock_without_quantities_and_marks_lines_already_in_the_count()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var svc = f.Get<CycleCountService>();
        var count = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);

        var before = await svc.ProductBinsAsync(count.Count.Id, f.ProductNonePublicId, default);
        Assert.Equal(new[] { f.PickBin1, f.PickBin2 }, before.Bins.Select(b => b.BinId).OrderBy(x => x).ToArray());
        Assert.All(before.Bins, b => Assert.Null(b.LineId));
        Assert.Equal((TrackingTypes.None, true), (before.TrackingTypeCode, before.IsActive));

        await svc.AddLineAsync(count.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductNonePublicId, CountedQty: 4m), default);
        var after = await svc.ProductBinsAsync(count.Count.Id, f.ProductNonePublicId, default);
        Assert.Null(after.Bins.Single(b => b.BinId == f.PickBin1).LineId);
        Assert.NotNull(after.Bins.Single(b => b.BinId == f.PickBin2).LineId);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ProductBinsAsync(count.Count.Id, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ProductBinsAsync(999_999, f.ProductNonePublicId, default));
    }

    [Fact]
    public async Task Add_line_without_a_bin_uses_the_only_position_with_stock_and_asks_when_there_are_none_or_several()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        var count = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);

        // Sin existencia en ninguna posición: hay que decir dónde lo encontró (400 en binId, mensaje exacto).
        var none = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddLineAsync(count.Count.Id, new CountAddLineRequest(null, f.ProductNonePublicId, CountedQty: 2m), default));
        Assert.Equal(CycleCountRules.BinRequiredNoStock("PN"), Assert.Single(none.Errors!["binId"]));
        Assert.Equal("El producto PN no tiene existencia en ninguna posición del almacén; indique la posición donde lo encontró.", CycleCountRules.BinRequiredNoStock("PN"));

        // Una sola posición con existencia: se usa sin pedirla (la línea nace con lo contado y la existencia esperada de la foto).
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var one = await svc.AddLineAsync(count.Count.Id, new CountAddLineRequest(null, f.ProductNonePublicId, CountedQty: 2m), default);
        var line = Assert.Single(one.Lines);
        Assert.Equal((f.PickBin1, 8m, 2m), (line.BinId, line.SystemQty, line.CountedQty));

        // Varias posiciones: hay que elegir (400 con los códigos) y con la posición dada entra normal.
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin2, 5m);
        var count2 = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);
        var many = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.AddLineAsync(count2.Count.Id, new CountAddLineRequest(null, f.ProductNonePublicId, CountedQty: 2m), default));
        var msg = Assert.Single(many.Errors!["binId"]);
        Assert.StartsWith("El producto PN está en varias posiciones (", msg);
        Assert.EndsWith("); indique en cuál lo contó.", msg);
        var picked = await svc.AddLineAsync(count2.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductNonePublicId, CountedQty: 2m), default);
        Assert.Equal(f.PickBin2, Assert.Single(picked.Lines).BinId);

        // Repetir el mismo producto en la misma posición sigue siendo 409 (la pantalla abre la línea existente en vez de duplicar).
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.AddLineAsync(count2.Count.Id, new CountAddLineRequest(f.PickBin2, f.ProductNonePublicId, CountedQty: 1m), default));
    }

    [Fact]
    public void Single_bin_rule_only_returns_a_bin_when_there_is_exactly_one()
    {
        Assert.Null(CycleCountRules.SingleBin(Array.Empty<int>()));
        Assert.Equal(7, CycleCountRules.SingleBin(new[] { 7, 7 }));   // dos lotes en la misma posición = una posición
        Assert.Null(CycleCountRules.SingleBin(new[] { 7, 8 }));
    }

    [Fact]
    public async Task Count_list_says_who_opened_it_and_who_captured_lines_without_quantities()
    {
        await using var f = await NewAsync();
        await f.ReceiveAsync(f.ProductNoneId, f.PickBin1, 8m);
        var svc = f.Get<CycleCountService>();
        var created = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);
        Assert.Equal(("Ana Pérez", 0), (created.Count.CreatedByName, created.Count.CapturedByNames!.Count));

        f.AsUser(Beto);
        await svc.AddLineAsync(created.Count.Id, new CountAddLineRequest(null, f.ProductNonePublicId, CountedQty: 3m), default);
        f.AsUser(Ana);
        var page = await svc.ListPageAsync(new CycleCountQuery(), blind: true, default);
        var item = Assert.Single(page.Items);
        Assert.Equal("Ana Pérez", item.CreatedByName);
        Assert.Equal(new[] { "Beto Ruiz" }, item.CapturedByNames);
    }

    [Fact]
    public async Task Count_opened_with_assign_to_me_is_assigned_to_its_creator_and_without_it_is_not()
    {
        await using var f = await NewAsync();
        var svc = f.Get<CycleCountService>();
        var mine = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true, AssignToMe: true), default);
        Assert.Equal((Ana, "Ana Pérez"), (mine.Count.AssignedToUserId, mine.Count.AssignedToName));
        var plain = await svc.CreateAsync(new CycleCountCreateRequest(AllowEmpty: true), default);
        Assert.Null(plain.Count.AssignedToUserId);
    }
}

using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 13 (Lote 3 del plan de cambios) — servicio del Recibo sobre InMemory (ReceivingFixture):
/// - ciclo EXPECTED → RECEIVING ↔ DISCREPANCY → RECEIVED / RECEIVED_VARIANCE → PUTAWAY, siempre por StatusService (con
///   historial por paso), sincronizado con cada cambio de líneas;
/// - recibo ciego o de devolución solo con encabezado; esperado capturable sin documento; cambio de producto;
/// - PATCH del encabezado (tipo, almacén, posición, muelle, transporte y referencia);
/// - filtros phase/variance de la lista (en memoria y su traducción a SQL Server) y filtros nuevos de los avisos.
/// </summary>
public sealed class ReceiptLifecycleTests
{
    private static async Task<List<string>> HistoryAsync(ReceivingFixture f, int receiptId)
    {
        var typeId = f.LookupId(LookupDomains.EntityType, EntityTypes.Receipt);
        var toIds = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == receiptId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        var codes = await f.Db.StatusCodes.AsNoTracking().ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode);
        return toIds.Select(i => codes[i]).ToList();
    }

    // ================================================================ ciclo de estatus

    [Fact]
    public async Task Blind_receipt_walks_expected_receiving_discrepancy_and_back_with_history()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();

        // Solo encabezado: nace EXPECTED (antes daba 400 'Indique al menos una línea.').
        var header = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind, Carrier: " DHL ", Reference: "BL-7"), default);
        Assert.Equal(ReceiptStatuses.Expected, header.Header.StatusCode);
        Assert.Empty(header.Lines);
        Assert.True(header.Header.IsOpen);
        Assert.True(header.CanDelete);
        Assert.Equal(("DHL", "BL-7"), (header.Header.Carrier, header.Header.Reference));
        var id = header.Header.PublicId;

        // Línea con esperado = recibido → RECEIVING.
        var same = await receipts.AddLineAsync(id, new ReceiptLineRequest(f.ProductNonePublicId, 5m, ExpectedQty: 5m), default);
        Assert.Equal(ReceiptStatuses.Receiving, same.Header.StatusCode);
        var line = Assert.Single(same.Lines);
        Assert.Equal((5m, 5m, 0m), (line.ExpectedQty!.Value, line.ReceivedQty, line.VarianceQty));

        // Distinto → DISCREPANCY; de vuelta igual → RECEIVING.
        var diff = await receipts.UpdateLineAsync(id, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 3m), default);
        Assert.Equal(ReceiptStatuses.Discrepancy, diff.Header.StatusCode);
        Assert.Equal(-2m, diff.Lines[0].VarianceQty);
        Assert.True(diff.Header.HasVariance);
        var back = await receipts.UpdateLineAsync(id, line.Id, new ReceiptLineUpdateRequest(ExpectedQty: 3m), default);
        Assert.Equal(ReceiptStatuses.Receiving, back.Header.StatusCode);

        var history = await HistoryAsync(f, back.Header.Id);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.Receiving }, history);

        // Quitar el esperado (ciego sin esperado = sin diferencia) y quitar todas las líneas: se queda RECEIVING.
        var cleared = await receipts.UpdateLineAsync(id, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 9m, ClearExpected: true), default);
        Assert.Null(cleared.Lines[0].ExpectedQty);
        Assert.Equal(ReceiptStatuses.Receiving, cleared.Header.StatusCode);
        var empty = await receipts.RemoveLineAsync(id, line.Id, default);
        Assert.Equal(ReceiptStatuses.Receiving, empty.Header.StatusCode);   // nunca vuelve a EXPECTED
        var none = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.ConfirmAsync(id, null, default));
        Assert.Equal(ReceiptRules.NoLinesToConfirm, none.Message);
    }

    [Fact]
    public async Task Confirming_from_discrepancy_ends_completed_with_variance_and_posts_what_was_received()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 3m, ExpectedQty: 5m) }), default);
        Assert.Equal(ReceiptStatuses.Discrepancy, created.Header.StatusCode);

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, new ReceiptConfirmRequest("Faltaron dos."), default);

        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, confirmed.Header.StatusCode);
        Assert.False(confirmed.CanDelete);
        Assert.Equal(1, confirmed.Header.PendingPutawayCount);
        // Decisión 3: en el Kárdex entra lo recibido (3), sin ajuste por la diferencia.
        var txn = Assert.Single(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());
        Assert.Equal((f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt), 3m), (txn.TxnTypeLookupId, txn.Quantity));
        Assert.Null(confirmed.Lines[0].AdjustmentTxnId);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.ReceivedWithVariance },
            await HistoryAsync(f, confirmed.Header.Id));
        var receiptType = f.LookupId(LookupDomains.EntityType, EntityTypes.Receipt);
        var comment = await f.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == receiptType && h.EntityId == confirmed.Header.Id)
            .OrderByDescending(h => h.EntityStatusHistoryId).Select(h => h.Comment).FirstAsync();
        Assert.Equal("Faltaron dos.", comment);

        // Confirmado: se lista en la fase PENDING_PUTAWAY.
        var pending = await receipts.ListAsync(new ReceiptQuery(Phase: ReceiptStatusRules.PhasePendingPutaway), default);
        Assert.Equal(confirmed.Header.Id, Assert.Single(pending.Items).Id);
    }

    [Fact]
    public async Task Completed_with_variance_goes_straight_to_putaway_when_nothing_is_left_to_store()
    {
        // RECEIVED_VARIANCE → PUTAWAY por la entrada lateral sembrada (todo lo tomó el cruce de muelle).
        await using var f = await ReceivingFixture.CreateAsync();
        f.CrossDock.Take = l => l.ReceivedQty;
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m) }), default);
        Assert.Equal(ReceiptStatuses.Discrepancy, created.Header.StatusCode);

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Putaway },
            await HistoryAsync(f, confirmed.Header.Id));
    }

    [Fact]
    public async Task Receipts_against_a_document_are_born_receiving_and_confirm_without_variance_as_received()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId, "ASN-REF", new DateOnly(2026, 10, 5),
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m) }), default);
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        Assert.Equal(ReceiptStatuses.Receiving, created.Header.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 5), created.Header.ExpectedDate);   // llegada esperada del aviso

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Received }, await HistoryAsync(f, confirmed.Header.Id));
    }

    [Fact]
    public async Task Confirm_true_without_lines_is_still_400_and_expected_receipts_can_be_deleted()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(new ReceiptCreateRequest(Confirm: true), default));
        Assert.Equal(ReceiptRules.LinesRequired, Assert.Single(ex.Errors!["lines"]));
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());

        var header = await receipts.CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Return), default);
        Assert.Equal((ReceiptTypes.Return, ReceiptStatuses.Expected), (header.Header.TypeCode, header.Header.StatusCode));
        await receipts.DeleteAsync(header.Header.PublicId, default);
        Assert.False((await f.Db.Set<ReceiptHeader>().AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Header_staging_bin_is_saved_and_used_by_new_lines()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        f.Db.Set<WarehouseBin>().Add(new WarehouseBin { WarehouseBinId = 102, WarehouseZoneId = 11, WarehouseId = f.WarehouseId, Code = "STG-02", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var receipts = f.Get<ReceiptService>();

        var header = await receipts.CreateAsync(new ReceiptCreateRequest(StagingBinId: 102), default);
        Assert.Equal((102, "STG-02"), (header.Header.DefaultStagingBinId!.Value, header.Header.DefaultStagingBinCode));
        var withLine = await receipts.AddLineAsync(header.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default);
        Assert.Equal(102, withLine.Lines[0].StagingBinId);
        // La de la solicitud manda sobre la del encabezado.
        var other = await receipts.AddLineAsync(header.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m, StagingBinId: f.StagingBinId), default);
        Assert.Equal(f.StagingBinId, other.Lines[1].StagingBinId);
    }

    // ================================================================ esperado y producto (400/409/422)

    [Fact]
    public async Task Expected_quantity_is_only_captured_without_a_document()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var po = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);

        var add = await Assert.ThrowsAsync<ValidationException>(() => receipts.AddLineAsync(po.Header.PublicId,
            new ReceiptLineRequest(f.ProductNonePublicId, 1m, ExpectedQty: 1m), default));
        Assert.Equal(ReceiptRules.ExpectedOnlyWithoutDocument, Assert.Single(add.Errors!["line.expectedQty"]));
        var put = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(po.Header.PublicId, po.Lines[0].Id,
            new ReceiptLineUpdateRequest(ExpectedQty: 4m), default));
        Assert.Equal(ReceiptRules.ExpectedOnlyWithoutDocument, Assert.Single(put.Errors!["expectedQty"]));
        var clear = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(po.Header.PublicId, po.Lines[0].Id,
            new ReceiptLineUpdateRequest(ClearExpected: true), default));
        Assert.Equal(ReceiptRules.ExpectedOnlyWithoutDocument, Assert.Single(clear.Errors!["expectedQty"]));
        // Cola del aparato contra OC con esperado en la línea → mismo 400 con la clave lines[i].
        await receipts.DeleteAsync(po.Header.PublicId, default);
        var atomic = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m, ExpectedQty: 1m) }, Confirm: true), default));
        Assert.Equal(ReceiptRules.ExpectedOnlyWithoutDocument, Assert.Single(atomic.Errors!["lines[0].expectedQty"]));

        var blind = await receipts.CreateAsync(new ReceiptCreateRequest(), default);
        var negative = await Assert.ThrowsAsync<ValidationException>(() => receipts.AddLineAsync(blind.Header.PublicId,
            new ReceiptLineRequest(f.ProductNonePublicId, 1m, ExpectedQty: -1m), default));
        Assert.Equal(ReceiptRules.ExpectedQtyNegative, Assert.Single(negative.Errors!["line.expectedQty"]));
        var withLine = await receipts.AddLineAsync(blind.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default);
        var decimals = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(blind.Header.PublicId, withLine.Lines[0].Id,
            new ReceiptLineUpdateRequest(ExpectedQty: 1.2345m), default));
        Assert.Equal(ReceiptRules.QtyDecimals, Assert.Single(decimals.Errors!["expectedQty"]));
    }

    [Fact]
    public async Task Product_of_an_extra_line_can_change_but_not_one_of_the_document()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var po = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        var fixedLine = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(po.Header.PublicId, po.Lines[0].Id,
            new ReceiptLineUpdateRequest(ProductPublicId: f.ProductLotPublicId), default));
        Assert.Equal(ReceiptRules.DocumentLineProductFixed, Assert.Single(fixedLine.Errors!["productPublicId"]));
        // El mismo producto no es un cambio.
        await receipts.UpdateLineAsync(po.Header.PublicId, po.Lines[0].Id, new ReceiptLineUpdateRequest(ProductPublicId: f.ProductNonePublicId), default);

        // Línea extra de una OC: solo productos propios (mismo 400 que el alta de línea).
        var extra = (await receipts.AddLineAsync(po.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default)).Lines.Single(l => l.AsnLineId is null);
        var owner = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateLineAsync(po.Header.PublicId, extra.Id,
            new ReceiptLineUpdateRequest(ProductPublicId: f.ProductClientPublicId), default));
        Assert.Equal(ReceiptRules.OwnProductsOnly("PC"), Assert.Single(owner.Errors!["productPublicId"]));

        // Ciego: cambia el producto; el lote del anterior se limpia.
        var blind = await receipts.CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductLotPublicId, 2m, Lot: new LotInput("L-9")) }), default);
        Assert.Equal("L-9", blind.Lines[0].LotNumber);
        var changed = await receipts.UpdateLineAsync(blind.Header.PublicId, blind.Lines[0].Id,
            new ReceiptLineUpdateRequest(ProductPublicId: f.ProductNonePublicId), default);
        Assert.Equal((f.ProductNonePublicId, (string?)null, 2m), (changed.Lines[0].ProductPublicId, changed.Lines[0].LotNumber, changed.Lines[0].ReceivedQty));

        // Con cruce de muelle asignado → 409.
        f.Db.Set<CrossDockAllocation>().Add(new CrossDockAllocation
        {
            CrossDockPlanId = 1, ReceiptLineId = blind.Lines[0].Id, TransportOrderId = 1, AllocatedQty = 1m,
            StatusCodeId = f.StatusId(StatusDomains.AllocationStatus, AllocationStatuses.Planned), CreatedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var xd = await Assert.ThrowsAsync<ConflictException>(() => receipts.UpdateLineAsync(blind.Header.PublicId, blind.Lines[0].Id,
            new ReceiptLineUpdateRequest(ProductPublicId: f.ProductLotPublicId), default));
        Assert.Equal(ReceiptRules.LineHasCrossDockProductChange, xd.Message);
        Assert.False((await receipts.GetAsync(blind.Header.PublicId, default)).CanDelete);
    }

    // ================================================================ PATCH del encabezado

    [Fact]
    public async Task Patch_updates_the_header_of_an_open_receipt()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        f.Db.Set<WarehouseDock>().Add(new WarehouseDock { WarehouseDockId = 71, WarehouseId = f.WarehouseId, Code = "D-1", DockTypeLookupId = 1, StatusCodeId = 1, IsActive = true });
        // Segundo almacén con zona de recepción.
        f.Db.Set<Warehouse>().Add(new Warehouse
        {
            WarehouseId = 20, PublicId = Guid.NewGuid(), TenantId = ReceivingFixture.TenantId, Code = "W2", Name = "Almacén 2",
            CountryLookupId = f.LookupId(LookupDomains.Country, "PR"), StatusCodeId = f.StatusId(StatusDomains.WarehouseStatus, WarehouseStatuses.Active), IsActive = true,
        });
        f.Db.Set<WarehouseZone>().Add(new WarehouseZone { WarehouseZoneId = 21, WarehouseId = 20, Code = "STG", Name = "Recepción", ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.Staging), IsActive = true });
        f.Db.Set<WarehouseBin>().Add(new WarehouseBin { WarehouseBinId = 221, WarehouseZoneId = 21, WarehouseId = 20, Code = "W2-STG", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var w2 = await f.Db.Set<Warehouse>().AsNoTracking().SingleAsync(w => w.WarehouseId == 20);
        var receipts = f.Get<ReceiptService>();

        var header = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: (await f.Db.Set<Warehouse>().AsNoTracking().SingleAsync(w => w.WarehouseId == f.WarehouseId)).PublicId,
            StagingBinId: f.StagingBinId, DockId: 71), default);
        var id = header.Header.PublicId;

        var patched = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(Type: "return", Carrier: "UPS", Reference: "R-1", ClearDock: true), default);
        Assert.Equal((ReceiptTypes.Return, "UPS", "R-1", (int?)null, f.StagingBinId), (patched.Header.TypeCode, patched.Header.Carrier, patched.Header.Reference,
            patched.Header.DockId, patched.Header.DefaultStagingBinId!.Value));
        // null = no cambiar; '' = borrar.
        var cleared = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(Reference: "", DockId: 71), default);
        Assert.Equal(("UPS", (string?)null, 71), (cleared.Header.Carrier, cleared.Header.Reference, cleared.Header.DockId!.Value));

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateHeaderAsync(id,
            new ReceiptHeaderUpdateRequest(Carrier: new string('c', 81), Reference: new string('r', 81)), default));
        Assert.Equal(ReceiptRules.CarrierTooLong, Assert.Single(tooLong.Errors!["carrier"]));
        Assert.Equal(ReceiptRules.ReferenceTooLong, Assert.Single(tooLong.Errors!["reference"]));

        // Almacén sin líneas: cambia y limpia posición y muelle.
        var moved = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(WarehousePublicId: w2.PublicId), default);
        Assert.Equal(("W2", (int?)null, (int?)null), (moved.Header.WarehouseCode, moved.Header.DefaultStagingBinId, moved.Header.DockId));
        var staged = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(StagingBinId: 221), default);
        Assert.Equal("W2-STG", staged.Header.DefaultStagingBinCode);
        // Una posición de otro almacén → 404.
        await Assert.ThrowsAsync<NotFoundException>(() => receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(StagingBinId: f.StagingBinId), default));
        var unstaged = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(ClearStagingBin: true), default);
        Assert.Null(unstaged.Header.DefaultStagingBinId);

        // Con líneas, el almacén ya no cambia.
        await receipts.AddLineAsync(id, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default);
        var warehouse = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateHeaderAsync(id,
            new ReceiptHeaderUpdateRequest(WarehousePublicId: header.Header.WarehousePublicId), default));
        Assert.Equal(ReceiptRules.WarehouseFixed, Assert.Single(warehouse.Errors!["warehousePublicId"]));
    }

    [Fact]
    public async Task Patch_rejects_type_and_warehouse_changes_with_a_document_and_any_change_once_confirmed()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var po = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        var id = po.Header.PublicId;

        var type = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(Type: ReceiptTypes.Blind), default));
        Assert.Equal(ReceiptRules.TypeFixedWithDocument, Assert.Single(type.Errors!["type"]));
        // El mismo tipo no es un cambio.
        var same = await receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(Type: "asn", Carrier: "FedEx"), default);
        Assert.Equal("FedEx", same.Header.Carrier);

        await receipts.ConfirmAsync(id, null, default);
        var frozen = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.UpdateHeaderAsync(id, new ReceiptHeaderUpdateRequest(Carrier: "UPS"), default));
        Assert.Equal(ReceiptRules.ReceiptNotOpen(po.Header.Number), frozen.Message);
    }

    // ================================================================ filtros

    [Fact]
    public async Task List_filters_by_phase_and_variance()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var shortR = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 3m, ExpectedQty: 5m) }), default);
        var overR = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m, ExpectedQty: 2m) }), default);
        var noneR = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) }), default);
        var expectedR = await receipts.CreateAsync(new ReceiptCreateRequest(), default);
        var doneR = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 2m) }), default);
        f.CrossDock.Take = l => l.ReceivedQty;
        await receipts.ConfirmAsync(doneR.Header.PublicId, null, default);   // todo a cruce de muelle → PUTAWAY

        async Task<int[]> Ids(ReceiptQuery q) => (await receipts.ListAsync(q, default)).Items.Select(i => i.Id).OrderBy(i => i).ToArray();
        int[] Of(params ReceiptDetailDto[] r) => r.Select(x => x.Header.Id).OrderBy(i => i).ToArray();

        Assert.Equal(Of(shortR, overR, noneR, expectedR), await Ids(new ReceiptQuery(Phase: "open")));
        Assert.Equal(Of(doneR), await Ids(new ReceiptQuery(Phase: ReceiptStatusRules.PhaseDone)));
        Assert.Empty(await Ids(new ReceiptQuery(Phase: ReceiptStatusRules.PhasePendingPutaway)));
        Assert.Equal(Of(shortR), await Ids(new ReceiptQuery(Variance: new[] { "SHORT" })));
        Assert.Equal(Of(overR), await Ids(new ReceiptQuery(Variance: new[] { "over" })));
        Assert.Equal(Of(noneR, expectedR, doneR), await Ids(new ReceiptQuery(Variance: new[] { "NONE" })));
        Assert.Equal(Of(shortR, noneR, expectedR), await Ids(new ReceiptQuery(Phase: "OPEN", Variance: new[] { "SHORT", "NONE" })));
        Assert.Equal(Of(shortR), await Ids(new ReceiptQuery(Status: new[] { ReceiptStatuses.Discrepancy }, Variance: new[] { "SHORT" })));

        var phase = await Assert.ThrowsAsync<ValidationException>(() => receipts.ListAsync(new ReceiptQuery(Phase: "LATE"), default));
        Assert.Equal(ReceiptStatusRules.UnknownPhase("LATE"), Assert.Single(phase.Errors!["phase"]));
        var variance = await Assert.ThrowsAsync<ValidationException>(() => receipts.ListAsync(new ReceiptQuery(Variance: new[] { "X" }), default));
        Assert.Equal(ReceiptStatusRules.UnknownVariance("X"), Assert.Single(variance.Errors!["variance"]));
    }

    [Fact]
    public void Phase_and_variance_predicates_translate_to_sql_server()
    {
        // Misma forma que ReceiptService.ListAsync (fase por ids de estatus; diferencia con O entre SHORT/OVER/NONE).
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var phaseIds = new List<int> { 1, 2, 3 };
        var asnTypeId = 7;
        var wantShort = true;
        var wantOver = false;
        var wantNone = true;
        var sql = db.Set<ReceiptHeader>().AsNoTracking()
            .Where(r => phaseIds.Contains(r.StatusCodeId))
            .Where(r =>
                (wantShort && db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId && l.ExpectedQty != null && l.ReceivedQty < l.ExpectedQty))
                || (wantOver && db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                    && ((l.ExpectedQty != null && l.ReceivedQty > l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty > 0))))
                || (wantNone && !db.Set<ReceiptLine>().Any(l => l.ReceiptHeaderId == r.ReceiptHeaderId
                    && ((l.ExpectedQty != null && l.ReceivedQty != l.ExpectedQty) || (l.ExpectedQty == null && r.ReceiptTypeLookupId == asnTypeId && l.ReceivedQty != 0)))))
            .OrderByDescending(r => r.CreatedAtUtc).Take(10)
            .ToQueryString();
        Assert.Contains("[ReceiptLine]", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("NOT EXISTS", sql);
    }

    [Fact]
    public async Task Asn_list_filters_by_reference_and_expected_arrival()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asns = f.Get<AsnService>();
        AsnCreateRequest A(string reference, DateOnly? date) => new(null, f.ClientPublicId, reference, date, new[] { new AsnLineRequest(f.ProductClientPublicId, 1m) });
        var a = await asns.CreateAsync(A("CONT-100", new DateOnly(2026, 10, 1)), default);
        var b = await asns.CreateAsync(A("CONT-200", new DateOnly(2026, 10, 10)), default);
        var c = await asns.CreateAsync(A("OTRO", null), default);

        async Task<int[]> Ids(AsnQuery q) => (await asns.ListAsync(q, InventoryScope.Any, default)).Select(x => x.Id).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { a.Id, b.Id }, await Ids(new AsnQuery(Reference: " cont- ")));
        Assert.Equal(new[] { a.Id }, await Ids(new AsnQuery(ExpectedTo: new DateOnly(2026, 10, 1))));
        Assert.Equal(new[] { b.Id }, await Ids(new AsnQuery(ExpectedFrom: new DateOnly(2026, 10, 2))));
        Assert.Equal(new[] { a.Id, b.Id }, await Ids(new AsnQuery(ExpectedFrom: new DateOnly(2026, 10, 1), ExpectedTo: new DateOnly(2026, 10, 10))));
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, await Ids(new AsnQuery()));
    }

    // ================================================================ modelo y SQL de estructura

    [Fact]
    public void Header_columns_are_mapped_and_declared_in_the_structure_script()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(typeof(ReceiptHeader))!;
        foreach (var name in new[] { nameof(ReceiptHeader.Carrier), nameof(ReceiptHeader.Reference) })
        {
            var prop = entity.FindProperty(name)!;
            Assert.Equal(80, prop.GetMaxLength());
            Assert.True(prop.IsNullable);
        }
        Assert.True(entity.FindProperty(nameof(ReceiptHeader.DefaultStagingBinId))!.IsNullable);
        Assert.Contains(entity.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(ReceiptHeader.DefaultStagingBinId)
                                                       && fk.PrincipalEntityType.ClrType == typeof(WarehouseBin));

        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains("IF OBJECT_ID('dbo.ReceiptHeader') IS NULL", sql);
        Assert.Contains("DefaultStagingBinId INT NULL,", sql);
        Assert.Contains("Carrier      NVARCHAR(80) NULL,", sql);
        Assert.Contains("Reference    NVARCHAR(80) NULL,", sql);
        Assert.Contains("CONSTRAINT FK_Receipt_StagingBin FOREIGN KEY (DefaultStagingBinId, WarehouseId) REFERENCES dbo.WarehouseBin(WarehouseBinId, WarehouseId)", sql);
        // Aditivo e idempotente sobre una base existente.
        Assert.Contains("IF COL_LENGTH('dbo.ReceiptHeader', 'DefaultStagingBinId') IS NULL", sql);
        Assert.Contains("ALTER TABLE dbo.ReceiptHeader ADD Carrier NVARCHAR(80) NULL;", sql);
        Assert.Contains("ALTER TABLE dbo.ReceiptHeader ADD Reference NVARCHAR(80) NULL;", sql);
        Assert.Contains("IF OBJECT_ID('dbo.FK_Receipt_StagingBin', 'F') IS NULL", sql);
        // La FK compuesta va después de su destino (UQ_WarehouseBin_IdWh).
        Assert.True(sql.IndexOf("UQ_WarehouseBin_IdWh", StringComparison.Ordinal) < sql.IndexOf("FK_Receipt_StagingBin", StringComparison.Ordinal));
    }
}

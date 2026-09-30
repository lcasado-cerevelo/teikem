using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 16 — recibo directo a posición (pruebas de SERVICIO sobre InMemory con el ReceivingFixture): alta sin zona STAGING,
/// RECEIPT a la posición destino sin tareas y Completado → Acomodado en la misma transacción con historial (D7), orden de
/// compra con ajuste en el destino, LOT y SERIAL, errores 400/422/404, targetBinCode con confirm, compatibilidad de la app
/// anterior (D9), copia del modo en el recibo (D2) y cambio de modo en el encabezado (D1), directo → acomodo con el destino
/// en la tarea, y línea con cruce de muelle (D11).
/// </summary>
public sealed class ReceiptDirectModeTests
{
    private const int QuarantineBinId = 203;
    private const int CrossDockBinId = 204;
    private const int Reserve2BinId = 202;

    /// <summary>
    /// W1 del fixture (STG-01 101, R-01 201) + R-02 (202, zona RSV), Q-01 (203, zona QUA) y X-01 (204, zona XD de cruce).
    /// directMode = el almacén recibe directo; withoutStaging = la zona STG queda inactiva (el almacén no tiene recepción).
    /// </summary>
    private static async Task<ReceivingFixture> FixtureAsync(bool directMode = true, bool withoutStaging = false)
    {
        var f = await ReceivingFixture.CreateAsync();
        f.Db.Set<WarehouseZone>().AddRange(
            new WarehouseZone { WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "QUA", Name = "Cuarentena", ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.Quarantine), IsActive = true },
            new WarehouseZone { WarehouseZoneId = 14, WarehouseId = f.WarehouseId, Code = "XD", Name = "Cruce", ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.CrossDock), IsActive = true });
        f.Db.Set<WarehouseBin>().AddRange(
            new WarehouseBin { WarehouseBinId = Reserve2BinId, WarehouseZoneId = 12, WarehouseId = f.WarehouseId, Code = "R-02", IsActive = true },
            new WarehouseBin { WarehouseBinId = QuarantineBinId, WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "Q-01", IsActive = true },
            new WarehouseBin { WarehouseBinId = CrossDockBinId, WarehouseZoneId = 14, WarehouseId = f.WarehouseId, Code = "X-01", IsActive = true });
        await f.Db.SaveChangesAsync();
        var w = await f.Db.Set<Warehouse>().SingleAsync(x => x.WarehouseId == f.WarehouseId);
        w.ReceivingModeLookupId = f.LookupId(LookupDomains.ReceivingMode, directMode ? ReceivingModes.Direct : ReceivingModes.Putaway);
        if (withoutStaging) (await f.Db.Set<WarehouseZone>().SingleAsync(z => z.WarehouseZoneId == 11)).IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static ReceiptCreateRequest Blind(Guid product, decimal qty, int? targetBinId = null, string? targetCode = null, bool confirm = false,
        string? mode = null, string type = ReceiptTypes.Blind)
        => new(Type: type, Lines: new[] { new ReceiptLineRequest(product, qty, TargetBinId: targetBinId, TargetBinCode: targetCode) }, Confirm: confirm,
            ReceivingMode: mode);

    private static async Task<List<string>> HistoryAsync(ReceivingFixture f, int receiptId)
    {
        var typeId = f.LookupId(LookupDomains.EntityType, EntityTypes.Receipt);
        var ids = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == receiptId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        var codes = new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.Received,
            ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Putaway };
        return ids.Select(id => codes.Single(c => f.StatusId(StatusDomains.ReceiptStatus, c) == id)).ToList();
    }

    private static async Task<decimal> OnHandAsync(ReceivingFixture f, int productId, int binId)
        => await f.Db.Set<StockBalance>().AsNoTracking().Where(b => b.ProductId == productId && b.WarehouseBinId == binId).SumAsync(b => (decimal?)b.QtyOnHand) ?? 0m;

    [Fact]
    public async Task Direct_receipt_opens_without_staging_needs_a_target_and_posts_there_without_tasks()
    {
        await using var f = await FixtureAsync(withoutStaging: true);
        var receipts = f.Get<ReceiptService>();

        // Sin zona STAGING un recibo con acomodo no se abre (422); directo sí.
        var noStaging = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 8m, mode: ReceivingModes.Putaway), default));
        Assert.Equal(ReceiptRules.NoStagingBin, noStaging.Message);
        var created = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 8m), default);
        Assert.Equal(ReceivingModes.Direct, created.Header.ReceivingModeCode);
        Assert.Equal(ReceiptStatuses.Receiving, created.Header.StatusCode);
        var line = Assert.Single(created.Lines);
        Assert.Null(line.StagingBinId);
        Assert.Null(line.TargetBinId);

        // Sin destino no confirma: 400 con la clave de la línea y el mensaje exacto.
        var missing = await Assert.ThrowsAsync<ValidationException>(() => receipts.ConfirmAsync(created.Header.PublicId, null, default));
        Assert.Equal(new[] { ReceivingModeRules.TargetRequired("PN") }, missing.Errors!["lines[0].targetBinId"]);

        var withTarget = await receipts.UpdateLineAsync(created.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(TargetBinId: f.ReserveBinId), default);
        var target = Assert.Single(withTarget.Lines);
        Assert.Equal(f.ReserveBinId, target.TargetBinId);
        Assert.Equal("R-01", target.TargetBinCode);
        Assert.Equal(ZoneTypes.Reserve, target.TargetZoneTypeCode);
        Assert.Null(target.TargetFreeQty);   // sin cupo

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Empty(confirmed.PutawayTasks);
        Assert.Equal(0, confirmed.Header.PendingPutawayCount);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Received, ReceiptStatuses.Putaway },
            await HistoryAsync(f, confirmed.Header.Id));   // D7: los dos pasos con historial
        var txn = Assert.Single(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt), txn.TxnTypeLookupId);
        Assert.Equal(8m, txn.Quantity);
        Assert.Equal(f.ReserveBinId, txn.ToBinId);
        Assert.Equal(8m, await OnHandAsync(f, f.ProductNoneId, f.ReserveBinId));
        Assert.Empty(await f.Db.Set<WarehouseTask>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Purchase_order_receipt_posts_receipt_and_variance_in_the_target()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 7m, TargetBinCode: "r-02") }), default);
        Assert.Equal(ReceivingModes.Direct, created.Header.ReceivingModeCode);
        var line = Assert.Single(created.Lines);
        Assert.Equal(Reserve2BinId, line.TargetBinId);   // por código, sin distinguir mayúsculas

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Equal(new[] { ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Putaway },
            await HistoryAsync(f, confirmed.Header.Id));
        var txns = await f.Db.Set<InventoryTransaction>().AsNoTracking().OrderBy(t => t.InventoryTransactionId).ToListAsync();
        Assert.Equal(2, txns.Count);
        Assert.Equal((10m, (int?)Reserve2BinId), (txns[0].Quantity, txns[0].ToBinId));        // RECEIPT por lo esperado en el destino
        Assert.Equal((-3m, (int?)Reserve2BinId), (txns[1].Quantity, txns[1].FromBinId));      // RECEIPT_VARIANCE en el mismo destino
        Assert.Equal(txns[1].InventoryTransactionId, Assert.Single(confirmed.Lines).AdjustmentTxnId);
        Assert.Equal(7m, await OnHandAsync(f, f.ProductNoneId, Reserve2BinId));
        Assert.Equal(0m, await OnHandAsync(f, f.ProductNoneId, f.StagingBinId));
        Assert.Equal(new PurchaseOrderReceiptQty(f.PoLineId, 7m), Assert.Single(Assert.Single(f.PurchaseOrders.Applied).Quantities));
    }

    [Fact]
    public async Task Lot_and_serial_products_enter_their_target()
    {
        await using var f = await FixtureAsync();
        var serialPublicId = Guid.NewGuid();
        f.Db.Set<Product>().Add(new Product
        {
            ProductId = 310, PublicId = serialPublicId, TenantId = ReceivingFixture.TenantId, Sku = "PS", Name = "Serie",
            BaseUomLookupId = f.LookupId(LookupDomains.UnitOfMeasure, "UN"), TrackingTypeLookupId = f.LookupId(LookupDomains.TrackingType, TrackingTypes.Serial), IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var confirmed = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(Type: ReceiptTypes.Blind, Confirm: true, Lines: new[]
        {
            new ReceiptLineRequest(f.ProductLotPublicId, 4m, new LotInput("L-16", null, null), TargetBinId: f.ReserveBinId),
            new ReceiptLineRequest(serialPublicId, null, SerialNumbers: new[] { "S1", "S2" }, TargetBinId: Reserve2BinId),
        }), default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Equal(4m, await OnHandAsync(f, 302, f.ReserveBinId));
        Assert.Equal(2m, await OnHandAsync(f, 310, Reserve2BinId));
        var serials = await f.Db.Set<InventorySerial>().AsNoTracking().Where(s => s.ProductId == 310).ToListAsync();
        Assert.Equal(2, serials.Count);
        Assert.All(serials, s => Assert.Equal(Reserve2BinId, s.CurrentBinId));
    }

    [Fact]
    public async Task Target_errors_400_404_and_422()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();

        var zone = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, targetBinId: CrossDockBinId), default));
        Assert.Equal(new[] { ReceivingModeRules.TargetZoneNotAllowed("X-01", ZoneTypes.CrossDock) }, zone.Errors!["lines[0].targetBinId"]);
        var staging = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, targetCode: "STG-01"), default));
        Assert.Equal(new[] { ReceivingModeRules.TargetZoneNotAllowed("STG-01", ZoneTypes.Staging) }, staging.Errors!["lines[0].targetBinCode"]);
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, targetCode: "ZZ", confirm: true), default));
        Assert.Equal(new[] { ReceivingModeRules.TargetCodeNotFound("ZZ") }, unknown.Errors!["lines[0].targetBinCode"]);
        var both = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, targetBinId: f.ReserveBinId, targetCode: "R-01"), default));
        Assert.Equal(new[] { ReceivingModeRules.TargetIdAndCode }, both.Errors!["lines[0].targetBinId"]);
        var mode = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, mode: "HALF"), default));
        Assert.Equal(new[] { ReceivingModeRules.UnknownMode("HALF") }, mode.Errors!["receivingMode"]);
        // Posición de otro almacén (o que no existe): 404 sin oráculo.
        var other = await Assert.ThrowsAsync<NotFoundException>(() => receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m, targetBinId: 99999), default));
        Assert.Equal("Posición no encontrada.", other.Message);

        // Posición destino desactivada después de elegirla → 422 al confirmar (y al elegirla).
        var created = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 2m, targetBinId: Reserve2BinId), default);
        (await f.Db.Set<WarehouseBin>().SingleAsync(b => b.WarehouseBinId == Reserve2BinId)).IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var inactive = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.ConfirmAsync(created.Header.PublicId, null, default));
        Assert.Equal(ReceivingModeRules.TargetBinInactive("R-02"), inactive.Message);
        var pick = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id,
            new ReceiptLineUpdateRequest(TargetBinId: Reserve2BinId), default));
        Assert.Equal(ReceivingModeRules.TargetBinInactive("R-02"), pick.Message);
        // Zona inactiva → 422 con su mensaje.
        (await f.Db.Set<WarehouseZone>().SingleAsync(z => z.WarehouseZoneId == 13)).IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var zoneOff = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id,
            new ReceiptLineUpdateRequest(TargetBinId: QuarantineBinId), default));
        Assert.Equal(ReceivingModeRules.TargetZoneInactive("Q-01"), zoneOff.Message);
        // Quitar el destino (ClearTargetBin) y confirmar → 400 de destino.
        var cleared = await receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id, new ReceiptLineUpdateRequest(ClearTargetBin: true), default);
        Assert.Null(Assert.Single(cleared.Lines).TargetBinId);
    }

    [Fact]
    public async Task Atomic_receipt_with_target_codes_and_old_app_compatibility()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();

        // App nueva: confirm con targetBinCode por línea → directo y Acomodado sin tareas.
        var direct = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 3m, targetCode: "Q-01", confirm: true), default);
        Assert.Equal(ReceivingModes.Direct, direct.Header.ReceivingModeCode);
        Assert.Equal(ReceiptStatuses.Putaway, direct.Header.StatusCode);
        Assert.Equal(3m, await OnHandAsync(f, f.ProductNoneId, QuarantineBinId));

        // D9: app anterior (confirm sin modo y sin destinos) en un almacén directo → con acomodo: staging + tarea.
        var old = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 2m, confirm: true), default);
        Assert.Equal(ReceivingModes.Putaway, old.Header.ReceivingModeCode);
        Assert.Equal(ReceiptStatuses.Received, old.Header.StatusCode);
        var task = Assert.Single(old.PutawayTasks);
        Assert.Equal(f.StagingBinId, task.FromBinId);
        Assert.Equal(2m, await OnHandAsync(f, f.ProductNoneId, f.StagingBinId));
    }

    [Fact]
    public async Task Receipt_keeps_its_mode_and_the_header_changes_it_only_for_that_receipt()
    {
        await using var f = await FixtureAsync(directMode: false);
        var receipts = f.Get<ReceiptService>();
        var open = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 5m, targetBinId: Reserve2BinId), default);
        Assert.Equal(ReceivingModes.Putaway, open.Header.ReceivingModeCode);

        // El almacén pasa a directo: el recibo abierto conserva su modo (D2).
        var w = await f.Db.Set<Warehouse>().SingleAsync(x => x.WarehouseId == f.WarehouseId);
        w.ReceivingModeLookupId = f.LookupId(LookupDomains.ReceivingMode, ReceivingModes.Direct);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal(ReceivingModes.Putaway, (await receipts.GetAsync(open.Header.PublicId, default)).Header.ReceivingModeCode);

        // Con acomodo y destino: la tarea va a la posición destino de la línea (no a la sugerida).
        var confirmed = await receipts.ConfirmAsync(open.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
        var task = Assert.Single(confirmed.PutawayTasks);
        Assert.Equal(Reserve2BinId, task.ToBinId);
        Assert.Equal(5m, await OnHandAsync(f, f.ProductNoneId, f.StagingBinId));

        // D1: el encabezado cambia el modo SOLO de este recibo; desconocido → 400.
        var second = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m), default);
        Assert.Equal(ReceivingModes.Direct, second.Header.ReceivingModeCode);
        var toPutaway = await receipts.UpdateHeaderAsync(second.Header.PublicId, new ReceiptHeaderUpdateRequest(ReceivingMode: "putaway"), default);
        Assert.Equal(ReceivingModes.Putaway, toPutaway.Header.ReceivingModeCode);
        var bad = await Assert.ThrowsAsync<ValidationException>(() => receipts.UpdateHeaderAsync(second.Header.PublicId, new ReceiptHeaderUpdateRequest(ReceivingMode: "X"), default));
        Assert.Equal(new[] { ReceivingModeRules.UnknownMode("X") }, bad.Errors!["receivingMode"]);
        Assert.Equal(ReceivingModes.Direct, (await receipts.UpdateHeaderAsync(second.Header.PublicId, new ReceiptHeaderUpdateRequest(ReceivingMode: "DIRECT"), default)).Header.ReceivingModeCode);
        // Confirmado: el modo ya no se cambia (422 del recibo cerrado).
        await Assert.ThrowsAsync<StatusRuleException>(() => receipts.UpdateHeaderAsync(open.Header.PublicId, new ReceiptHeaderUpdateRequest(ReceivingMode: "DIRECT"), default));
    }

    [Fact]
    public async Task Switching_to_putaway_without_staging_is_422()
    {
        await using var f = await FixtureAsync(withoutStaging: true);
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 1m), default);
        var e = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.UpdateHeaderAsync(created.Header.PublicId,
            new ReceiptHeaderUpdateRequest(ReceivingMode: ReceivingModes.Putaway), default));
        Assert.Equal(ReceiptRules.NoStagingBin, e.Message);
    }

    [Fact]
    public async Task Document_line_enters_a_single_target()
    {
        await using var f = await FixtureAsync();
        var e = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[]
            {
                new ReceiptLineRequest(f.ProductNonePublicId, 4m, TargetBinId: f.ReserveBinId),
                new ReceiptLineRequest(f.ProductNonePublicId, 3m, TargetBinId: Reserve2BinId),
            }), default));
        Assert.Equal(new[] { ReceivingModeRules.SameProductOtherTarget("PN", "R-01") }, e.Errors!["lines[1].targetBinCode"]);
    }

    [Fact]
    public async Task Cross_dock_line_enters_staging_and_its_remainder_goes_to_the_target_with_a_task()
    {
        await using var f = await FixtureAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(Blind(f.ProductNonePublicId, 10m, targetBinId: f.ReserveBinId), default);
        var line = Assert.Single(created.Lines);
        Assert.Equal(f.StagingBinId, line.StagingBinId);   // la posición de recepción queda para el cruce (D11)
        f.Db.Set<CrossDockAllocation>().Add(new CrossDockAllocation
        {
            CrossDockAllocationId = 900, CrossDockPlanId = 1, ReceiptLineId = line.Id, TransportOrderId = 1, AllocatedQty = 4m,
            StatusCodeId = f.StatusId(StatusDomains.AllocationStatus, AllocationStatuses.Planned), CreatedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.CrossDock.Take = l => l.ReceiptLineId == line.Id ? 4m : 0m;

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);   // queda Completado hasta cerrar la tarea
        Assert.Equal(10m, await OnHandAsync(f, f.ProductNoneId, f.StagingBinId));
        var task = Assert.Single(confirmed.PutawayTasks);
        Assert.Equal(6m, task.Quantity);
        Assert.Equal(f.StagingBinId, task.FromBinId);
        Assert.Equal(f.ReserveBinId, task.ToBinId);
    }
}

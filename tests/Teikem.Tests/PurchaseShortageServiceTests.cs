using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P8) — pruebas de SERVICIO de la resolución de faltantes de compra (maestro L305 y L762), InMemory con WmsFixture y
/// StatusService, PermissionService, ModuleService, InventoryLedger y PurchaseOrderService reales:
/// - CLOSE no mueve inventario; REORDER crea una orden DRAFT al mismo proveedor y almacén por el pendiente al costo congelado;
///   MANUAL_ADJUSTMENT asienta un ADJUSTMENT + con Ref PURCHASE_ORDER y guarda su movimiento en la resolución;
/// - cada resolución descuenta del pendiente; sin pendientes la orden PARTIAL pasa a RECEIVED con historial;
/// - una segunda resolución sin pendiente → 409; una orden cancelada → 422 y desaparece de la lista de faltantes.
/// </summary>
public sealed class PurchaseShortageServiceTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<PurchaseOrderService>();
        s.AddSingleton<PurchaseShortageService>();
    });

    private sealed record Po(PurchaseOrder Header, List<PurchaseOrderLine> Lines);

    /// <summary>Orden con un recibo confirmado (ASN de la orden + recibo RECEIVED) y líneas (pedido, recibido, costo).</summary>
    private static async Task<Po> AddPurchaseOrderAsync(WmsFixture f, Warehouse w, Supplier supplier, string number, string status,
        params (Product Product, decimal Ordered, decimal Received, decimal Cost)[] lines)
    {
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = number,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, status), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        var poLines = lines.Select(l => new PurchaseOrderLine
        {
            PurchaseOrderId = po.PurchaseOrderId, ProductId = l.Product.ProductId, QtyOrdered = l.Ordered, QtyReceived = l.Received, UnitCost = l.Cost,
        }).ToList();
        f.Db.Set<PurchaseOrderLine>().AddRange(poLines);
        var asn = new Asn
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, PurchaseOrderId = po.PurchaseOrderId,
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Received), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Asn>().Add(asn);
        await f.Db.SaveChangesAsync();
        f.Db.Set<ReceiptHeader>().Add(new ReceiptHeader
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, AsnId = asn.AsnId,
            ReceiptTypeLookupId = f.LookupId(LookupDomains.ReceiptType, ReceiptTypes.Asn), Number = "REC-" + number,
            StatusCodeId = f.StatusId(StatusDomains.ReceiptStatus, ReceiptStatuses.Received), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new Po(po, poLines);
    }

    private static async Task<Supplier> AddSupplierAsync(WmsFixture f)
    {
        var s = new Supplier { TenantId = WmsFixture.TenantId, Name = "Proveedor Uno", IsActive = true };
        f.Db.Set<Supplier>().Add(s);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return s;
    }

    private static async Task<List<PurchaseOrderShortageResolution>> ResolutionsAsync(WmsFixture f, int lineId)
        => await f.Db.Set<PurchaseOrderShortageResolution>().AsNoTracking().Where(r => r.PurchaseOrderLineId == lineId)
            .OrderBy(r => r.PurchaseOrderShortageResolutionId).ToListAsync();

    [Fact]
    public async Task Close_reorder_and_manual_adjustment_each_discount_the_pending_and_the_last_one_receives_the_order()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var pb = await f.AddProductAsync("PB");
        var pc = await f.AddProductAsync("PC");
        var po = await AddPurchaseOrderAsync(f, w, supplier, "PO-00001", PurchaseOrderStatuses.Partial,
            (pa, 10m, 8m, 2.5m), (pb, 5m, 3m, 3m), (pc, 4m, 2m, 1.25m));
        var (lineA, lineB, lineC) = (po.Lines[0], po.Lines[1], po.Lines[2]);
        var svc = f.Get<PurchaseShortageService>();

        // (a) CLOSE: resuelve el pendiente completo (2) sin movimiento de inventario.
        var closed = await svc.ResolveAsync(po.Header.PublicId, lineA.PurchaseOrderLineId, new ShortageResolveRequest(ShortageActions.Close), default);
        Assert.Empty(await f.TransactionsAsync());
        Assert.Equal(2m, Assert.Single(await ResolutionsAsync(f, lineA.PurchaseOrderLineId)).Quantity);
        Assert.Equal(0m, closed.Line.QtyPending);
        Assert.Equal(PurchaseOrderStatuses.Partial, closed.PurchaseOrder.StatusCode);

        // (b) REORDER: orden DRAFT nueva, mismo proveedor y almacén, una línea por el pendiente (2) al mismo costo.
        var reordered = await svc.ResolveAsync(po.Header.PublicId, lineB.PurchaseOrderLineId, new ShortageResolveRequest(ShortageActions.Reorder), default);
        var reorder = Assert.IsType<PurchaseOrderDto>(reordered.Reorder);
        Assert.Equal(PurchaseOrderStatuses.Draft, reorder.StatusCode);
        Assert.Equal(supplier.SupplierId, reorder.SupplierId);
        Assert.Equal(w.PublicId, reorder.WarehousePublicId);
        var reorderLine = Assert.Single(reorder.Lines);
        Assert.Equal(pb.PublicId, reorderLine.ProductPublicId);
        Assert.Equal(2m, reorderLine.QtyOrdered);
        Assert.Equal(3m, reorderLine.UnitCost);
        var reorderRow = Assert.Single(await ResolutionsAsync(f, lineB.PurchaseOrderLineId));
        Assert.Equal(reorder.Id, reorderRow.ReorderPurchaseOrderId);
        Assert.Empty(await f.TransactionsAsync());

        // (c) MANUAL_ADJUSTMENT de 1: ADJUSTMENT +1, Ref PURCHASE_ORDER, motivo PO_SHORTAGE por defecto; el pendiente queda en 1.
        var manual = await svc.ResolveAsync(po.Header.PublicId, lineC.PurchaseOrderLineId,
            new ShortageResolveRequest(ShortageActions.ManualAdjustment, Quantity: 1m, BinId: bin.WarehouseBinId), default);
        var txn = Assert.Single(await f.TransactionsAsync());
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment), txn.TxnTypeLookupId);
        Assert.Equal(1m, txn.Quantity);
        Assert.Equal(bin.WarehouseBinId, txn.ToBinId);
        Assert.Equal(f.LookupId(LookupDomains.EntityType, EntityTypes.PurchaseOrder), txn.RefEntityLookupId);
        Assert.Equal(po.Header.PurchaseOrderId, txn.RefId);
        Assert.Equal(f.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.PoShortage), txn.ReasonLookupId);
        Assert.Equal(txn.InventoryTransactionId, Assert.Single(await ResolutionsAsync(f, lineC.PurchaseOrderLineId)).InventoryTransactionId);
        Assert.Equal(1m, manual.Line.QtyPending);
        Assert.Equal(PurchaseOrderStatuses.Partial, manual.PurchaseOrder.StatusCode);
        Assert.Equal(1m, await f.OnHandAsync(pc.ProductId, bin.WarehouseBinId));

        // (d) Resolver el último pendiente → PARTIAL → RECEIVED con el comentario en el historial.
        var last = await svc.ResolveAsync(po.Header.PublicId, lineC.PurchaseOrderLineId, new ShortageResolveRequest(ShortageActions.Close), default);
        Assert.Equal(PurchaseOrderStatuses.Received, last.PurchaseOrder.StatusCode);
        var typeId = f.LookupId(LookupDomains.EntityType, EntityTypes.PurchaseOrder);
        var history = await f.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == po.Header.PurchaseOrderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        var received = history.Last();
        Assert.Equal(f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Received), received.ToStatusCodeId);
        Assert.Equal(PurchaseShortageService.AllResolvedComment, received.Comment);

        // (e) Una segunda resolución de una línea ya resuelta → 409.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => svc.ResolveAsync(po.Header.PublicId, lineA.PurchaseOrderLineId,
            new ShortageResolveRequest(ShortageActions.Close), default));
        Assert.Equal(ShortageRules.NoPendingShortage, conflict.Message);
        Assert.Single(await ResolutionsAsync(f, lineA.PurchaseOrderLineId));
    }

    [Fact]
    public async Task Cancelled_order_is_rejected_and_leaves_the_shortage_list()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var pb = await f.AddProductAsync("PB");
        var cancelled = await AddPurchaseOrderAsync(f, w, supplier, "PO-00001", PurchaseOrderStatuses.Cancelled, (pa, 10m, 8m, 2.5m));
        var open = await AddPurchaseOrderAsync(f, w, supplier, "PO-00002", PurchaseOrderStatuses.Partial, (pa, 10m, 7m, 2.5m), (pb, 4m, 4m, 1m));
        var svc = f.Get<PurchaseShortageService>();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => svc.ResolveAsync(cancelled.Header.PublicId,
            cancelled.Lines[0].PurchaseOrderLineId, new ShortageResolveRequest(ShortageActions.Close), default));
        Assert.Equal(ShortageRules.PoCancelled, ex.Message);
        Assert.Empty(await f.Db.Set<PurchaseOrderShortageResolution>().AsNoTracking().ToListAsync());

        var list = await svc.ListWithShortageAsync(default);
        var summary = Assert.Single(list);
        Assert.Equal(open.Header.PublicId, summary.PublicId);
        Assert.Equal(1, summary.LinesWithShortage);
        Assert.Equal(3m, summary.QtyPending);
        Assert.Equal(7.5m, summary.PendingCost);
    }
    /// <summary>Recibo OPEN sobre un ASN nuevo (EXPECTED) de la orden, como el que crea la recepción contra PO.</summary>
    private static async Task<ReceiptHeader> AddOpenReceiptAsync(WmsFixture f, Warehouse w, PurchaseOrder po)
    {
        var asn = new Asn
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, PurchaseOrderId = po.PurchaseOrderId,
            StatusCodeId = f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Expected), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<Asn>().Add(asn);
        await f.Db.SaveChangesAsync();
        var r = new ReceiptHeader
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, AsnId = asn.AsnId,
            ReceiptTypeLookupId = f.LookupId(LookupDomains.ReceiptType, ReceiptTypes.Asn), Number = "REC-OPEN-" + po.Number,
            StatusCodeId = f.StatusId(StatusDomains.ReceiptStatus, ReceiptStatuses.Open), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<ReceiptHeader>().Add(r);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return r;
    }

    [Fact]
    public async Task Cancelling_a_partial_order_with_an_open_receipt_is_409_then_cancels_with_comment_and_is_kept()
    {
        // D47 (maestro L461): se cancela desde PARTIAL con bitácora, pero no con un recibo OPEN; cancelada con recepciones no se elimina.
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var po = await AddPurchaseOrderAsync(f, w, supplier, "PO-00003", PurchaseOrderStatuses.Partial, (pa, 10m, 4m, 2m));
        var open = await AddOpenReceiptAsync(f, w, po.Header);
        var orders = f.Get<PurchaseOrderService>();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            orders.CancelAsync(po.Header.PublicId, new PurchaseOrderStatusRequest("Proveedor sin existencia"), default));
        Assert.Equal(PurchaseOrderRules.HasOpenReceipt, ex.Message);
        Assert.Equal("La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.", ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(PurchaseOrderStatuses.Partial,
            f.StatusCodeOf((await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PurchaseOrderId == po.Header.PurchaseOrderId)).StatusCodeId));

        // Sin el recibo abierto (eliminado), se cancela y el comentario queda en el historial PURCHASE_ORDER.
        var r = await f.Db.Set<ReceiptHeader>().SingleAsync(x => x.ReceiptHeaderId == open.ReceiptHeaderId);
        r.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var cancelled = await orders.CancelAsync(po.Header.PublicId, new PurchaseOrderStatusRequest("Proveedor sin existencia"), default);
        Assert.Equal(PurchaseOrderStatuses.Cancelled, cancelled.StatusCode);
        var typeId = f.LookupId(LookupDomains.EntityType, EntityTypes.PurchaseOrder);
        var history = await f.Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == po.Header.PurchaseOrderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal("Proveedor sin existencia", history.Last().Comment);

        var del = await Assert.ThrowsAsync<ConflictException>(() => orders.DeleteAsync(po.Header.PublicId, default));
        Assert.Equal(PurchaseOrderRules.DeleteCancelledWithReceipts, del.Message);
    }

    [Fact]
    public async Task Deleting_a_sent_order_with_an_open_receipt_is_409()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = "PO-00004",
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        f.Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.PurchaseOrderId, ProductId = pa.ProductId, QtyOrdered = 5m, UnitCost = 1m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await AddOpenReceiptAsync(f, w, po);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Get<PurchaseOrderService>().DeleteAsync(po.PublicId, default));
        Assert.Equal(PurchaseOrderRules.HasOpenReceiptDelete, ex.Message);
        Assert.True((await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PurchaseOrderId == po.PurchaseOrderId)).IsActive);
    }

    /// <summary>Orden SENT sin recepciones (sin ASN ni recibo), con una línea (pedido, 0 recibido).</summary>
    private static async Task<PurchaseOrder> AddSentOrderAsync(WmsFixture f, Warehouse w, Supplier supplier, Product p, string number)
    {
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.WarehouseId, Number = number,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow), StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<PurchaseOrder>().Add(po);
        await f.Db.SaveChangesAsync();
        f.Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderId = po.PurchaseOrderId, ProductId = p.ProductId, QtyOrdered = 10m, UnitCost = 1m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return po;
    }

    [Fact]
    public async Task Orders_without_a_confirmed_receipt_have_no_shortage()
    {
        // Maestro L305 / bitácora L762: el faltante se calcula contra recepciones CONFIRMADAS de la orden.
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var poA = await AddSentOrderAsync(f, w, supplier, pa, "PO-00010");   // SENT sin recibos
        var poB = await AddSentOrderAsync(f, w, supplier, pa, "PO-00011");   // SENT con solo un recibo OPEN
        await AddOpenReceiptAsync(f, w, poB);
        var poC = await AddPurchaseOrderAsync(f, w, supplier, "PO-00012", PurchaseOrderStatuses.Partial, (pa, 10m, 8m, 1m));   // control

        var svc = f.Get<PurchaseShortageService>();
        Assert.Equal(poC.Header.PublicId, Assert.Single(await svc.ListWithShortageAsync(default)).PublicId);
        Assert.Empty(await svc.LinesAsync(poA.PublicId, default));
        Assert.Empty(await svc.LinesAsync(poB.PublicId, default));
        Assert.Equal(2m, Assert.Single(await svc.LinesAsync(poC.Header.PublicId, default)).QtyPending);
    }

    [Fact]
    public async Task Inactive_product_is_rejected_in_purchase_order_lines()
    {
        // Maestro L326: un producto inactivo no admite líneas de compra (ni al crear ni al editar la orden).
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var active = await f.AddProductAsync("PA");
        var inactive = await f.AddProductAsync("PX", isActive: false);
        var orders = f.Get<PurchaseOrderService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(active.PublicId, 1m, 1m), new PurchaseOrderLineRequest(inactive.PublicId, 1m, 1m) }), default));
        Assert.Equal(PurchaseOrderRules.ProductInactive("PX"), Assert.Single(ex.Errors!["lines[1]"]));
        Assert.Equal("El producto PX está inactivo; no admite órdenes de compra.", PurchaseOrderRules.ProductInactive("PX"));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<PurchaseOrder>().AsNoTracking().ToListAsync());

        var draft = await orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(active.PublicId, 1m, 1m) }), default);
        var patch = await Assert.ThrowsAsync<ValidationException>(() => orders.UpdateAsync(draft.PublicId,
            new PurchaseOrderPatchRequest(Lines: new[] { new PurchaseOrderLineRequest(inactive.PublicId, 2m, 1m) }, RowVersion: draft.RowVersion), default));
        Assert.Equal(PurchaseOrderRules.ProductInactive("PX"), Assert.Single(patch.Errors!["lines[0]"]));
    }
    [Fact]
    public async Task Draft_order_is_edited_and_deleted()
    {
        // Maestro L460-L461 (D46/D47): en DRAFT la orden se edita (reemplazo de líneas, fecha esperada y notas) y, sin
        // recepciones, se elimina (baja lógica; un segundo borrado → 404).
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var supplier = await AddSupplierAsync(f);
        var pa = await f.AddProductAsync("PA");
        var pb = await f.AddProductAsync("PB");
        var orders = f.Get<PurchaseOrderService>();
        var draft = await orders.CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 1m, 1m) }), default);
        Assert.True(draft.CanEdit);

        var expected = draft.OrderDate.AddDays(5);
        var edited = await orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(ExpectedDate: expected, Notes: "ajuste",
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 3m, 2m), new PurchaseOrderLineRequest(pb.PublicId, 1m, 1.5m) },
            RowVersion: draft.RowVersion), default);
        Assert.Equal((PurchaseOrderStatuses.Draft, "ajuste", (DateOnly?)expected), (edited.StatusCode, edited.Notes, edited.ExpectedDate));
        Assert.Equal(2, edited.Lines.Count);
        Assert.Contains(edited.Lines, l => l.ProductPublicId == pa.PublicId && l.QtyOrdered == 3m && l.UnitCost == 2m);
        Assert.Contains(edited.Lines, l => l.ProductPublicId == pb.PublicId && l.QtyOrdered == 1m && l.UnitCost == 1.5m);
        Assert.Equal(7.5m, edited.Total);

        await orders.DeleteAsync(draft.PublicId, default);
        f.Db.ChangeTracker.Clear();
        Assert.False((await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PublicId == draft.PublicId)).IsActive);
        await Assert.ThrowsAsync<NotFoundException>(() => orders.DeleteAsync(draft.PublicId, default));
    }
}

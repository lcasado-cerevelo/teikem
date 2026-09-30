using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
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
/// Lote 6 (P4) — pruebas de SERVICIO de la Recepción sobre InMemory, con StatusService, InventoryLedger,
/// WarehouseTaskWriter y PutawaySuggester reales (resueltos por DI) y fakes de las costuras de P8
/// (IPurchaseOrderReceiving) y P9 (IReceiptConfirmationParticipant), para no depender de esas piezas.
/// Con InMemory los bloqueos de InventoryQueries cargan tracked sin bloqueo: se prueba la orquestación (asientos con
/// signo, AdjustmentTxnId, PO, ASN, cruce de muelle, putaway), no la concurrencia (la cubre el smoke).
/// </summary>
public sealed class ReceiptServiceTests
{
    [Fact]
    public async Task Confirming_po_receipt_posts_expected_and_variance_links_adjustment_and_applies_po()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();

        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        Assert.Equal(ReceiptStatuses.Receiving, created.Header.StatusCode);   // Lote 13: contra OC nace RECEIVING
        Assert.True(created.Header.IsOpen);
        Assert.StartsWith("REC-", created.Header.Number);
        Assert.Equal(ReceiptOrigins.PurchaseOrder, created.Header.Origin);
        var line = Assert.Single(created.Lines);
        Assert.Equal(10m, line.ExpectedQty);
        Assert.Equal(10m, line.ReceivedQty);                     // R8: lo recibido arranca igual a lo esperado
        Assert.Equal(f.StagingBinId, line.StagingBinId);         // primera posición de una zona STAGING
        Assert.Equal(12.5m, line.UnitCost);                      // costo congelado de la línea de PO

        var short8 = await receipts.UpdateLineAsync(created.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 8m), default);
        Assert.Equal(ReceiptStatuses.Discrepancy, short8.Header.StatusCode);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, new ReceiptConfirmRequest(), default);

        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, confirmed.Header.StatusCode);   // Lote 13: con diferencia
        Assert.False(confirmed.Header.IsOpen);
        Assert.Equal(1, confirmed.Header.PendingPutawayCount);
        Assert.NotNull(confirmed.Header.ReceivedAtUtc);
        var txns = await f.Db.Set<InventoryTransaction>().AsNoTracking().OrderBy(t => t.InventoryTransactionId).ToListAsync();
        Assert.Equal(2, txns.Count);
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt), txns[0].TxnTypeLookupId);
        Assert.Equal(10m, txns[0].Quantity);                     // RECEIPT + por lo esperado (D3/D4)
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment), txns[1].TxnTypeLookupId);
        Assert.Equal(-2m, txns[1].Quantity);                     // ADJUSTMENT − por la diferencia
        Assert.Equal(f.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.ReceiptVariance), txns[1].ReasonLookupId);
        Assert.All(txns, t => Assert.Equal(confirmed.Header.Id, t.RefId));
        Assert.Equal(txns[1].InventoryTransactionId, Assert.Single(confirmed.Lines).AdjustmentTxnId);

        var balance = await f.Db.Set<StockBalance>().AsNoTracking().SingleAsync(b => b.ProductId == f.ProductNoneId);
        Assert.Equal(8m, balance.QtyOnHand);
        Assert.Equal(f.StagingBinId, balance.WarehouseBinId);

        var applied = Assert.Single(f.PurchaseOrders.Applied);
        Assert.Equal(f.PoId, applied.PurchaseOrderId);
        var qty = Assert.Single(applied.Quantities);
        Assert.Equal(new PurchaseOrderReceiptQty(f.PoLineId, 8m), qty);

        var asn = await f.Db.Set<Asn>().AsNoTracking().SingleAsync();
        Assert.Equal(f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Received), asn.StatusCodeId);
        Assert.Equal(f.PoId, asn.PurchaseOrderId);

        var putaway = Assert.Single(confirmed.PutawayTasks);   // sin cruce de muelle: todo lo recibido va a putaway
        Assert.Equal(8m, putaway.Quantity);
        Assert.Equal(f.StagingBinId, putaway.FromBinId);
        Assert.Equal(f.ReserveBinId, putaway.ToBinId);          // L301: posición sugerida (la única fuera de staging)
    }

    [Fact]
    public async Task Atomic_po_receipt_applies_the_scanned_lines_instead_of_the_expected()
    {
        // Lote 8A (cola del aparato): contra PO con lines + confirm, lo escaneado manda (3 de 10), no lo esperado.
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var confirmed = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 3m) }, Confirm: true), default);

        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, confirmed.Header.StatusCode);   // 3 de 10: con diferencia
        var line = Assert.Single(confirmed.Lines);
        Assert.Equal(10m, line.ExpectedQty);
        Assert.Equal(3m, line.ReceivedQty);
        Assert.NotNull(line.AsnLineId);
        Assert.Equal(new PurchaseOrderReceiptQty(f.PoLineId, 3m), Assert.Single(Assert.Single(f.PurchaseOrders.Applied).Quantities));
        Assert.Equal(3m, (await f.Db.Set<StockBalance>().AsNoTracking().SingleAsync(b => b.ProductId == f.ProductNoneId)).QtyOnHand);
    }

    [Fact]
    public async Task Atomic_po_receipt_leaves_unmentioned_lines_at_zero_and_takes_foreign_products_as_extra()
    {
        // Decisión 13 del Lote 8A: OC de 2 líneas; se escanea solo una y un producto ajeno al documento. La no mencionada
        // queda en 0 (faltante visible), el ajeno entra como línea extra y ninguno de los dos cuenta contra la OC.
        await using var f = await ReceivingFixture.CreateAsync();
        var otherPublicId = Guid.NewGuid();
        var foreignPublicId = Guid.NewGuid();
        Product P(int productId, Guid publicId, string sku) => new()
        {
            ProductId = productId, PublicId = publicId, TenantId = ReceivingFixture.TenantId, ClientId = null, Sku = sku, Name = "Producto " + sku,
            BaseUomLookupId = f.LookupId(LookupDomains.UnitOfMeasure, "UN"),
            TrackingTypeLookupId = f.LookupId(LookupDomains.TrackingType, TrackingTypes.None), PurchaseCost = 12.5m, IsActive = true,
        };
        f.Db.Set<Product>().AddRange(P(304, otherPublicId, "PN2"), P(305, foreignPublicId, "PX"));
        f.Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderLineId = 602, PurchaseOrderId = f.PoId, ProductId = 304, QtyOrdered = 5m, UnitCost = 12.5m });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.PurchaseOrders.Pending = new PurchaseOrderForReceipt(f.PoId, "PO-00001", f.WarehouseId,
            new[] { new PurchaseOrderPendingLine(f.PoLineId, f.ProductNoneId, 10m, 12.5m), new PurchaseOrderPendingLine(602, 304, 5m, 12.5m) });

        var confirmed = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 3m), new ReceiptLineRequest(foreignPublicId, 2m) }, Confirm: true), default);

        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, confirmed.Header.StatusCode);
        Assert.Equal(3, confirmed.Lines.Count);
        var scanned = Assert.Single(confirmed.Lines, l => l.ProductPublicId == f.ProductNonePublicId);
        Assert.Equal(3m, scanned.ReceivedQty);
        var unmentioned = Assert.Single(confirmed.Lines, l => l.ProductPublicId == otherPublicId);
        Assert.Equal(5m, unmentioned.ExpectedQty);
        Assert.Equal(0m, unmentioned.ReceivedQty);
        var extra = Assert.Single(confirmed.Lines, l => l.ProductPublicId == foreignPublicId);
        Assert.Null(extra.AsnLineId);
        Assert.Null(extra.ExpectedQty);
        Assert.Equal(2m, extra.ReceivedQty);
        Assert.Equal(new PurchaseOrderReceiptQty(f.PoLineId, 3m), Assert.Single(Assert.Single(f.PurchaseOrders.Applied).Quantities));
    }

    [Fact]
    public async Task Asn_receipt_with_lines_checks_the_owner_and_merges_repeated_products()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m) }), default);
        var receipts = f.Get<ReceiptService>();

        // Producto de otro dueño: mismo 400 que una línea extra, con la clave lines[i].
        var owner = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) }), default));
        Assert.Contains(ReceiptRules.OwnerMismatch("PN"), owner.Errors!["lines[0].productPublicId"]);

        // Dos lecturas del mismo producto se suman sobre la línea del aviso (2 + 1 = 3 de 6).
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id,
            Lines: new[] { new ReceiptLineRequest(f.ProductClientPublicId, 2m), new ReceiptLineRequest(f.ProductClientPublicId, 1m) }), default);
        var line = Assert.Single(created.Lines);
        Assert.Equal(6m, line.ExpectedQty);
        Assert.Equal(3m, line.ReceivedQty);
        await receipts.DeleteAsync(created.Header.PublicId, default);

        // Sin lines: se recibe lo esperado (R8), como siempre.
        var plain = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        Assert.Equal(6m, Assert.Single(plain.Lines).ReceivedQty);
    }

    [Fact]
    public async Task Atomic_client_asn_receipt_matches_lots_and_merges_serial_scans()
    {
        // Lote 8A, decisión 14 — recibo en una llamada contra aviso de cliente (asnId + confirm): lo escaneado se aplica por
        // producto y lote sobre las líneas del aviso; las lecturas repetidas de un producto por serie acumulan sus series.
        await using var f = await ReceivingFixture.CreateAsync();
        var lotPublicId = Guid.NewGuid();
        var serialPublicId = Guid.NewGuid();
        Product P(int productId, Guid publicId, string sku, string tracking) => new()
        {
            ProductId = productId, PublicId = publicId, TenantId = ReceivingFixture.TenantId, ClientId = f.ClientId, Sku = sku, Name = "Producto " + sku,
            BaseUomLookupId = f.LookupId(LookupDomains.UnitOfMeasure, "UN"),
            TrackingTypeLookupId = f.LookupId(LookupDomains.TrackingType, tracking), PurchaseCost = 1m, IsActive = true,
        };
        f.Db.Set<Product>().AddRange(P(306, lotPublicId, "PCL", TrackingTypes.Lot), P(307, serialPublicId, "PCS", TrackingTypes.Serial));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var asns = f.Get<AsnService>();
        var asn = await asns.CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(lotPublicId, 5m, "L-1"), new AsnLineRequest(serialPublicId, 3m) }), default);
        var receipts = f.Get<ReceiptService>();

        // Serie repetida entre lecturas del mismo producto → lines[2].serialNumbers; no queda recibo ni asiento.
        var dup = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id,
            Lines: new[]
            {
                new ReceiptLineRequest(lotPublicId, 2m, Lot: new LotInput("L-1")),
                new ReceiptLineRequest(serialPublicId, 1m, SerialNumbers: new[] { "S1" }),
                new ReceiptLineRequest(serialPublicId, 1m, SerialNumbers: new[] { "S1" }),
            }, Confirm: true), default));
        Assert.True(dup.Errors!.ContainsKey("lines[2].serialNumbers"));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());

        var confirmed = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id,
            Lines: new[]
            {
                new ReceiptLineRequest(lotPublicId, 2m, Lot: new LotInput("L-1")),
                new ReceiptLineRequest(serialPublicId, 1m, SerialNumbers: new[] { "S1" }),
                new ReceiptLineRequest(serialPublicId, 1m, SerialNumbers: new[] { "S2" }),
            }, Confirm: true), default);

        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, confirmed.Header.StatusCode);   // 2 de 5 y 2 de 3
        Assert.Equal(2, confirmed.Lines.Count);
        Assert.All(confirmed.Lines, l => Assert.NotNull(l.AsnLineId));
        var lot = Assert.Single(confirmed.Lines, l => l.ProductPublicId == lotPublicId);
        Assert.Equal(2m, lot.ReceivedQty);
        Assert.Equal("L-1", lot.LotNumber);
        var serial = Assert.Single(confirmed.Lines, l => l.ProductPublicId == serialPublicId);
        Assert.Equal(2m, serial.ReceivedQty);
        Assert.Equal(new[] { "S1", "S2" }, serial.SerialNumbers.OrderBy(x => x));
        Assert.Equal(AsnStatuses.Received, (await asns.GetAsync(asn.Id, InventoryScope.Any, default)).StatusCode);
    }

    [Fact]
    public async Task Purchase_order_with_an_open_receipt_rejects_another_until_it_is_deleted()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var first = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default));
        Assert.Equal(ReceiptRules.PurchaseOrderHasOpenReceipt, ex.Message);

        await receipts.DeleteAsync(first.Header.PublicId, default);
        var second = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        Assert.NotEqual(first.Header.Number, second.Header.Number);
    }

    [Fact]
    public async Task Second_confirmation_is_rejected_with_422()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.ConfirmAsync(created.Header.PublicId, null, default));
        Assert.Equal(ReceiptRules.ReceiptNotOpen(created.Header.Number), ex.Message);
        Assert.Single(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());   // una sola tanda de RECEIPT
        Assert.Single(f.PurchaseOrders.Applied);
    }

    [Fact]
    public async Task Cross_dock_participant_reduces_the_putaway()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        f.CrossDock.Take = line => 3m;
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 8m) }), default);
        Assert.Equal(ReceiptTypes.Blind, created.Header.TypeCode);

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        Assert.Equal(1, f.CrossDock.Calls);
        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
        var task = Assert.Single(confirmed.PutawayTasks);
        Assert.Equal(5m, task.Quantity);
        Assert.Equal(f.ReserveBinId, task.ToBinId);
        var txn = Assert.Single(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());   // ciego: sin ajuste
        Assert.Equal(8m, txn.Quantity);
        Assert.Null(Assert.Single(confirmed.Lines).AdjustmentTxnId);
    }

    [Fact]
    public async Task Receipt_goes_straight_to_putaway_when_cross_dock_takes_everything()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        f.CrossDock.Take = line => line.ReceivedQty;
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m) }), default);

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Empty(confirmed.PutawayTasks);
        Assert.Empty(await f.Db.Set<WarehouseTask>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Deleting_open_po_receipt_cancels_its_asn()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);

        await receipts.DeleteAsync(created.Header.PublicId, default);

        var asn = await f.Db.Set<Asn>().AsNoTracking().SingleAsync();
        Assert.Equal(f.StatusId(StatusDomains.AsnStatus, AsnStatuses.Cancelled), asn.StatusCodeId);
        Assert.False((await f.Db.Set<ReceiptHeader>().AsNoTracking().SingleAsync()).IsActive);
        Assert.Empty(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => receipts.GetAsync(created.Header.PublicId, default));
    }

    [Fact]
    public async Task Client_asn_receipt_keeps_the_asn_expected_after_delete_and_rejects_a_second_receipt()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m) }), default);
        Assert.Equal(AsnStatuses.Expected, asn.StatusCode);
        var receipts = f.Get<ReceiptService>();

        var first = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        Assert.Equal(ReceiptOrigins.Asn, first.Header.Origin);
        var busy = await Assert.ThrowsAsync<ConflictException>(() => receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default));
        Assert.Equal(ReceiptRules.AsnBusy, busy.Message);

        await receipts.DeleteAsync(first.Header.PublicId, default);
        var again = await f.Get<AsnService>().GetAsync(asn.Id, InventoryScope.Any, default);
        Assert.Equal(AsnStatuses.Expected, again.StatusCode);
        var second = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        Assert.Equal(6m, Assert.Single(second.Lines).ReceivedQty);
    }

    [Fact]
    public async Task Asn_reads_respect_the_owner_scope()
    {
        // D44: con InventoryScope de dueño, solo los ASN de ese cliente; el de otro dueño → 404 sin oráculo.
        await using var f = await ReceivingFixture.CreateAsync();
        var asns = f.Get<AsnService>();
        var asn = await asns.CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m) }), default);
        var owner = new InventoryScope(f.ClientId);
        var other = new InventoryScope(f.ClientId + 1);

        Assert.Equal(asn.Id, (await asns.GetAsync(asn.Id, owner, default)).Id);
        Assert.Equal(asn.Id, Assert.Single(await asns.ListAsync(new AsnQuery(), owner, default)).Id);
        Assert.Equal(asn.Id, Assert.Single(await asns.ListAsync(new AsnQuery(), InventoryScope.Any, default)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => asns.GetAsync(asn.Id, other, default));
        Assert.Empty(await asns.ListAsync(new AsnQuery(), other, default));
    }

    [Fact]
    public async Task Warehouse_is_required_with_more_than_one_active_and_422_without_any()
    {
        // D26 (maestro L294): con un solo almacén activo se toma por defecto; con más de uno → 400; sin ninguno → 422.
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var blind = new ReceiptCreateRequest(Type: ReceiptTypes.Blind, Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) });

        f.Db.Set<Warehouse>().Add(new Warehouse
        {
            WarehouseId = 20, PublicId = Guid.NewGuid(), TenantId = ReceivingFixture.TenantId, Code = "W2", Name = "Almacén 2",
            CountryLookupId = f.LookupId(LookupDomains.Country, "PR"),
            StatusCodeId = f.StatusId(StatusDomains.WarehouseStatus, WarehouseStatuses.Active), IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var many = await Assert.ThrowsAsync<ValidationException>(() => receipts.CreateAsync(blind, default));
        Assert.Equal(WmsResolve.WarehouseRequiredMessage, Assert.Single(many.Errors!["warehousePublicId"]));
        Assert.Equal("Indique el almacén: la compañía tiene más de uno.", WmsResolve.WarehouseRequiredMessage);

        foreach (var w in await f.Db.Set<Warehouse>().ToListAsync()) w.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var none = await Assert.ThrowsAsync<StatusRuleException>(() => receipts.CreateAsync(blind, default));
        Assert.Equal(WmsResolve.NoActiveWarehouseMessage, none.Message);
        Assert.Equal("La compañía no tiene almacenes activos.", none.Message);
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Client_asn_rejects_products_of_another_owner()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductNonePublicId, 1m) }), default));
        Assert.Contains(ReceiptRules.OwnerMismatch("PN"), ex.Errors!.Values.SelectMany(v => v));
    }

    [Fact]
    public async Task Line_of_another_receipt_is_not_found()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var a = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) }), default);
        var b = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 2m) }), default);
        Assert.NotEqual(a.Header.Number, b.Header.Number);

        await Assert.ThrowsAsync<NotFoundException>(() => receipts.UpdateLineAsync(a.Header.PublicId, b.Lines[0].Id,
            new ReceiptLineUpdateRequest(ReceivedQty: 5m), default));
    }

    [Fact]
    public async Task Lot_product_requires_a_lot_to_confirm()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductLotPublicId, 5m) }), default);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => receipts.ConfirmAsync(created.Header.PublicId, null, default));
        Assert.Contains(ReceiptRules.LotRequired("PL"), ex.Errors!.Values.SelectMany(v => v));
        Assert.Empty(await f.Db.Set<InventoryTransaction>().AsNoTracking().ToListAsync());

        var withLot = await receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id,
            new ReceiptLineUpdateRequest(Lot: new LotInput("L1", null, new DateOnly(2027, 1, 31))), default);
        Assert.Equal("L1", withLot.Lines[0].LotNumber);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => receipts.UpdateLineAsync(created.Header.PublicId, created.Lines[0].Id,
            new ReceiptLineUpdateRequest(Lot: new LotInput("L1", null, new DateOnly(2027, 2, 28))), default));
        Assert.Equal(ReceiptRules.LotExistsWithOtherDates("L1"), conflict.Message);

        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
    }

    [Fact]
    public async Task Putaway_created_on_confirmation_targets_the_preferred_bin()
    {
        // Maestro L301 (putaway dirigido): la PUTAWAY del recibo nace con la posición sugerida; la preferida va primero.
        await using var f = await ReceivingFixture.CreateAsync();
        f.Db.Set<WarehouseZone>().Add(new WarehouseZone
        {
            WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "PCK", Name = "Picking",
            ZoneTypeLookupId = f.LookupId(LookupDomains.ZoneType, ZoneTypes.Picking), IsActive = true,
        });
        f.Db.Set<WarehouseBin>().Add(new WarehouseBin { WarehouseBinId = 203, WarehouseZoneId = 13, WarehouseId = f.WarehouseId, Code = "P-01", IsActive = true });
        var pn = await f.Db.Set<Product>().SingleAsync(p => p.ProductId == f.ProductNoneId);
        pn.PreferredWarehouseId = f.WarehouseId;
        pn.PreferredBinId = 203;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 4m) }), default);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        var putaway = Assert.Single(confirmed.PutawayTasks);
        Assert.Equal((f.StagingBinId, 203), (putaway.FromBinId!.Value, putaway.ToBinId!.Value));
        Assert.Equal("P-01", putaway.ToBinCode);
        var suggestion = (await f.Get<PutawaySuggester>().SuggestAsync(f.WarehouseId, f.ProductNoneId, null, 4m, f.StagingBinId, 3, default))[0];
        Assert.Equal((203, "PREFERRED"), (suggestion.BinId, suggestion.ReasonCode));
        Assert.False(string.IsNullOrEmpty(suggestion.RotationClass));
    }

    [Fact]
    public async Task Extra_line_on_a_client_asn_receipt_is_confirmed_as_an_inbound_adjustment()
    {
        // Maestro L300: el excedente de un producto que no venía en el aviso entra como línea extra (solo ajuste de entrada).
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m) }), default);
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);

        var owner = await Assert.ThrowsAsync<ValidationException>(() =>
            receipts.AddLineAsync(created.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default));
        Assert.Contains(ReceiptRules.OwnerMismatch("PN"), owner.Errors!.Values.SelectMany(v => v));

        var withExtra = await receipts.AddLineAsync(created.Header.PublicId, new ReceiptLineRequest(f.ProductClientPublicId, 2m), default);
        var extra = Assert.Single(withExtra.Lines, l => l.AsnLineId is null);
        Assert.Null(extra.ExpectedQty);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        var txns = await f.Db.Set<InventoryTransaction>().AsNoTracking().OrderBy(t => t.InventoryTransactionId).ToListAsync();
        var receiptType = f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Receipt);
        var adjustmentType = f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Adjustment);
        Assert.Equal(6m, Assert.Single(txns, t => t.TxnTypeLookupId == receiptType).Quantity);
        var adj = Assert.Single(txns, t => t.TxnTypeLookupId == adjustmentType);
        Assert.Equal(2m, adj.Quantity);
        Assert.Equal(f.LookupId(LookupDomains.AdjustmentReason, AdjustmentReasons.ReceiptVariance), adj.ReasonLookupId);
        Assert.Equal(adj.InventoryTransactionId, confirmed.Lines.Single(l => l.Id == extra.Id).AdjustmentTxnId);
        Assert.Equal(8m, (await f.Db.Set<StockBalance>().AsNoTracking().Where(b => b.ProductId == 303).SumAsync(b => b.QtyOnHand)));
    }

    [Fact]
    public async Task Only_extra_lines_are_removed_and_po_receipts_only_take_own_products()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);

        var asnLine = await Assert.ThrowsAsync<ConflictException>(() => receipts.RemoveLineAsync(created.Header.PublicId, created.Lines[0].Id, default));
        Assert.Equal(ReceiptRules.AsnLineNotRemovable, asnLine.Message);

        var clientProduct = await Assert.ThrowsAsync<ValidationException>(() =>
            receipts.AddLineAsync(created.Header.PublicId, new ReceiptLineRequest(f.ProductClientPublicId, 1m), default));
        Assert.Contains(ReceiptRules.OwnProductsOnly("PC"), clientProduct.Errors!.Values.SelectMany(v => v));

        var withExtra = await receipts.AddLineAsync(created.Header.PublicId, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default);
        Assert.Equal(2, withExtra.Lines.Count);
        var extra = withExtra.Lines.Single(l => l.AsnLineId is null);
        var after = await receipts.RemoveLineAsync(created.Header.PublicId, extra.Id, default);
        Assert.Equal(created.Lines[0].Id, Assert.Single(after.Lines).Id);
    }

    [Fact]
    public async Task Confirmed_receipt_is_frozen()
    {
        // Maestro L300: editable hasta confirmar; después es contenido congelado (ya está en el ledger).
        await using var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId), default);
        await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        var txnsBefore = await f.Db.Set<InventoryTransaction>().AsNoTracking().CountAsync();
        var onHandBefore = await f.Db.Set<StockBalance>().AsNoTracking().SumAsync(b => b.QtyOnHand);
        var expected = ReceiptRules.ReceiptNotOpen(created.Header.Number);
        var id = created.Header.PublicId;
        var lineId = created.Lines[0].Id;

        foreach (var attempt in new Func<Task>[]
                 {
                     () => receipts.UpdateLineAsync(id, lineId, new ReceiptLineUpdateRequest(ReceivedQty: 1m), default),
                     () => receipts.AddLineAsync(id, new ReceiptLineRequest(f.ProductNonePublicId, 1m), default),
                     () => receipts.RemoveLineAsync(id, lineId, default),
                     () => receipts.DeleteAsync(id, default),
                 })
        {
            var ex = await Assert.ThrowsAsync<StatusRuleException>(attempt);
            Assert.Equal(expected, ex.Message);
            f.Db.ChangeTracker.Clear();
        }
        Assert.Equal(txnsBefore, await f.Db.Set<InventoryTransaction>().AsNoTracking().CountAsync());
        Assert.Equal(onHandBefore, await f.Db.Set<StockBalance>().AsNoTracking().SumAsync(b => b.QtyOnHand));
        Assert.Equal(10m, (await receipts.GetAsync(id, default)).Lines[0].ReceivedQty);
    }

    [Fact]
    public async Task Warehouse_without_a_staging_zone_cannot_receive()
    {
        // D21: el recibo exige una posición de recepción; sin zona STAGING activa → 422 NoStagingBin.
        await using var f = await ReceivingFixture.CreateAsync();
        var zone = await f.Db.Set<WarehouseZone>().SingleAsync(z => z.WarehouseZoneId == 11);
        zone.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) }), default));
        Assert.Equal(ReceiptRules.NoStagingBin, ex.Message);
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Cancelling_an_asn_requires_no_active_receipt_and_only_applies_while_expected()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asns = f.Get<AsnService>();
        var receipts = f.Get<ReceiptService>();
        var asn = await asns.CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 3m) }), default);
        var receipt = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);

        var busy = await Assert.ThrowsAsync<ConflictException>(() => asns.CancelAsync(asn.Id, default));
        Assert.Equal(ReceiptRules.AsnHasOpenReceipt, busy.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(AsnStatuses.Expected, (await asns.GetAsync(asn.Id, InventoryScope.Any, default)).StatusCode);

        await receipts.DeleteAsync(receipt.Header.PublicId, default);
        var cancelled = await asns.CancelAsync(asn.Id, default);
        Assert.Equal(AsnStatuses.Cancelled, cancelled.StatusCode);
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => asns.CancelAsync(asn.Id, default));
        Assert.Equal(ReceiptRules.AsnNotExpected, again.Message);

        // Un ASN ya recibido tampoco se cancela.
        var received = await asns.CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 2m) }), default);
        var r2 = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: received.Id), default);
        await receipts.ConfirmAsync(r2.Header.PublicId, null, default);
        var done = await Assert.ThrowsAsync<StatusRuleException>(() => asns.CancelAsync(received.Id, default));
        Assert.Equal(ReceiptRules.AsnNotExpected, done.Message);
    }

    [Fact]
    public async Task Staging_bin_must_be_in_a_staging_zone()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(
            StagingBinId: f.ReserveBinId, Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 1m) }), default));
        Assert.Equal(ReceiptRules.StagingMustBeStaging, ex.Message);
    }
}

// ====================================================================================== fixture y fakes

/// <summary>
/// Fixture InMemory de la Recepción (al estilo de TripServiceFixture): catálogos y estatus WMS como el seed, un almacén
/// W1 con zona STG (STAGING, STG-01) y RSV (RESERVE, R-01), productos PN (NONE, propio), PL (LOT, propio) y PC (NONE, del
/// cliente C1), y una PO SENT con 10 de PN @ 12.5 servida por el fake de IPurchaseOrderReceiving.
/// </summary>
internal sealed class ReceivingFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lookupIds = new(StringComparer.OrdinalIgnoreCase);

    private ReceivingFixture(TeikemDbContext db, ServiceProvider services, TripTestLookups lookups,
        ReceivingFakePurchaseOrders purchaseOrders, ReceivingFakeCrossDock crossDock)
    {
        Db = db;
        Services = services;
        Lookups = lookups;
        PurchaseOrders = purchaseOrders;
        CrossDock = crossDock;
    }

    public TeikemDbContext Db { get; }
    public ServiceProvider Services { get; }
    public TripTestLookups Lookups { get; }
    public ReceivingFakePurchaseOrders PurchaseOrders { get; }
    public ReceivingFakeCrossDock CrossDock { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public int WarehouseId => 10;
    public int ClientId => 7;
    public int StagingBinId => 101;
    public int ReserveBinId => 201;
    public int ProductNoneId => 301;
    public Guid ProductNonePublicId { get; } = Guid.NewGuid();
    public Guid ProductLotPublicId { get; } = Guid.NewGuid();
    public Guid ProductClientPublicId { get; } = Guid.NewGuid();
    public Guid ClientPublicId { get; } = Guid.NewGuid();
    public int PoId => 501;
    public int PoLineId => 601;
    public Guid PoPublicId { get; } = Guid.NewGuid();

    public int StatusId(string domain, string code) => _statusIds[domain + "|" + code];
    public int LookupId(string domain, string code) => _lookupIds[domain + "|" + code];

    public static async Task<ReceivingFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("receiving-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var cache = new MemoryCache(new MemoryCacheOptions());
        // Módulos encendidos del tenant (ModuleService lee primero la caché).
        cache.Set($"modules:{TenantId}", new HashSet<string>(new[] { ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing }, StringComparer.OrdinalIgnoreCase));
        var purchaseOrders = new ReceivingFakePurchaseOrders();
        var crossDock = new ReceivingFakeCrossDock();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(tenant);
        services.AddSingleton<ILookupCache>(lookups);
        services.AddSingleton<IMemoryCache>(cache);
        services.AddSingleton<ISecurityEventWriter, ReceivingNullSecurityEventWriter>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton<ModuleService>();
        services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), sp.GetRequiredService<PermissionService>()));
        services.AddSingleton<INumberSequenceService, InMemoryNumberSequence>();
        services.AddSingleton<IInventoryChangeSink, InventoryChangeSink>();   // Lote 14 (P2): bandeja de cambios del ledger
        services.AddSingleton<InventoryLedger>();
        services.AddSingleton<WarehouseTaskWriter>();
        services.AddSingleton<PutawaySuggester>();
        services.AddSingleton<IPurchaseOrderReceiving>(purchaseOrders);
        services.AddSingleton<IReceiptConfirmationParticipant>(crossDock);
        services.AddSingleton<AsnService>();
        services.AddSingleton<ReceiptService>();
        var provider = services.BuildServiceProvider();

        var f = new ReceivingFixture(db, provider, lookups, purchaseOrders, crossDock);
        await f.SeedAsync();
        return f;
    }

    private async Task SeedAsync()
    {
        var id = 1;
        var all = new List<LookupCode>();
        LookupCode L(string entity, string code)
        {
            var l = new LookupCode { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            all.Add(l);
            _lookupIds[entity + "|" + code] = l.LookupCodeId;
            return l;
        }
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        foreach (var e in new[]
                 {
                     EntityTypes.Warehouse, EntityTypes.WarehouseDock, EntityTypes.Product, EntityTypes.Receipt, EntityTypes.ReceiptLine,
                     EntityTypes.Asn, EntityTypes.InventorySerial, EntityTypes.WarehouseTask, EntityTypes.InventoryTransaction,
                     EntityTypes.StockBalance, EntityTypes.PurchaseOrder, EntityTypes.Supplier, EntityTypes.CycleCount,
                     EntityTypes.PickBatch, EntityTypes.CrossDockPlan, EntityTypes.CrossDockAllocation, EntityTypes.TransportOrder,
                 })
            L(LookupDomains.EntityType, e);
        foreach (var c in new[] { ReceiptTypes.Asn, ReceiptTypes.Blind, ReceiptTypes.Return }) L(LookupDomains.ReceiptType, c);
        foreach (var c in new[] { TrackingTypes.None, TrackingTypes.Lot, TrackingTypes.Serial }) L(LookupDomains.TrackingType, c);
        foreach (var c in new[] { ZoneTypes.Picking, ZoneTypes.Reserve, ZoneTypes.Refrigerated, ZoneTypes.Quarantine, ZoneTypes.CrossDock, ZoneTypes.Staging })
            L(LookupDomains.ZoneType, c);
        foreach (var c in new[] { InventoryTxnTypes.Receipt, InventoryTxnTypes.Issue, InventoryTxnTypes.Transfer, InventoryTxnTypes.Adjustment, InventoryTxnTypes.CrossDock })
            L(LookupDomains.InventoryTxnType, c);
        foreach (var c in new[]
                 {
                     AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.Damage, AdjustmentReasons.Loss,
                     AdjustmentReasons.Found, AdjustmentReasons.Expired, AdjustmentReasons.PoShortage, AdjustmentReasons.PickBatchReversal,
                     AdjustmentReasons.Other,
                 })
            L(LookupDomains.AdjustmentReason, c);
        foreach (var c in new[]
                 {
                     WarehouseTaskTypes.Putaway, WarehouseTaskTypes.Pick, WarehouseTaskTypes.Pack, WarehouseTaskTypes.Replenish,
                     WarehouseTaskTypes.Count, WarehouseTaskTypes.Load, WarehouseTaskTypes.CrossDock,
                 })
            L(LookupDomains.WarehouseTaskType, c);
        var uom = L(LookupDomains.UnitOfMeasure, "UN");
        var country = L(LookupDomains.Country, "PR");
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
            Db.StatusCodes.Add(s);
            _statusIds[domain + "|" + code] = s.StatusCodeId;
        }
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Active, pipe, 1, true);
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive, term, 2);
        // Lote 13: los seis estatus del recibo (EXPECTED en el lugar de OPEN; los nuevos al final para no mover ids).
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Expected, pipe, 1, true);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Received, pipe, 4);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway, term, 6);
        S(StatusDomains.AsnStatus, AsnStatuses.Expected, pipe, 1, true);
        S(StatusDomains.AsnStatus, AsnStatuses.Received, term, 2);
        S(StatusDomains.AsnStatus, AsnStatuses.Cancelled, term, 3);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, pipe, 1, true);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, pipe, 2);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, term, 3);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled, term, 4);
        S(StatusDomains.SerialStatus, SerialStatuses.Available, pipe, 1, true);
        S(StatusDomains.SerialStatus, SerialStatuses.Reserved, lat, 2);
        S(StatusDomains.SerialStatus, SerialStatuses.Shipped, lat, 3);
        S(StatusDomains.SerialStatus, SerialStatuses.Scrapped, term, 4);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Planned, pipe, 1, true);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Moved, term, 2);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Cancelled, term, 3);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent, pipe, 2);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial, pipe, 3);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Received, term, 4);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, term, 5);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Receiving, pipe, 2);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Discrepancy, lat, 3);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.ReceivedWithVariance, lat, 5);
        ReceiptStatusSeed.AddLateralEntries(Db, LookupId(LookupDomains.EntityType, EntityTypes.Receipt), StatusId);

        Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", IsActive = true });

        Db.Set<Warehouse>().Add(new Warehouse
        {
            WarehouseId = WarehouseId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "W1", Name = "Almacén 1",
            CountryLookupId = country.LookupCodeId, StatusCodeId = StatusId(StatusDomains.WarehouseStatus, WarehouseStatuses.Active), IsActive = true,
        });
        Db.Set<WarehouseZone>().AddRange(
            new WarehouseZone { WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = "STG", Name = "Recepción", ZoneTypeLookupId = LookupId(LookupDomains.ZoneType, ZoneTypes.Staging), IsActive = true },
            new WarehouseZone { WarehouseZoneId = 12, WarehouseId = WarehouseId, Code = "RSV", Name = "Reserva", ZoneTypeLookupId = LookupId(LookupDomains.ZoneType, ZoneTypes.Reserve), IsActive = true });
        Db.Set<WarehouseBin>().AddRange(
            new WarehouseBin { WarehouseBinId = StagingBinId, WarehouseZoneId = 11, WarehouseId = WarehouseId, Code = "STG-01", IsActive = true },
            new WarehouseBin { WarehouseBinId = ReserveBinId, WarehouseZoneId = 12, WarehouseId = WarehouseId, Code = "R-01", IsActive = true });

        Db.Clients.Add(new Teikem.Domain.Clients.Client
        {
            ClientId = ClientId, PublicId = ClientPublicId, TenantId = TenantId, Code = "C1", Name = "Cliente Uno",
            StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });

        Product P(int productId, Guid publicId, string sku, string tracking, int? clientId) => new()
        {
            ProductId = productId, PublicId = publicId, TenantId = TenantId, ClientId = clientId, Sku = sku, Name = "Producto " + sku,
            BaseUomLookupId = uom.LookupCodeId, TrackingTypeLookupId = LookupId(LookupDomains.TrackingType, tracking),
            PurchaseCost = 12.5m, IsActive = true,
        };
        Db.Set<Product>().AddRange(
            P(ProductNoneId, ProductNonePublicId, "PN", TrackingTypes.None, null),
            P(302, ProductLotPublicId, "PL", TrackingTypes.Lot, null),
            P(303, ProductClientPublicId, "PC", TrackingTypes.None, 7));

        Db.Set<Supplier>().Add(new Supplier { SupplierId = 401, TenantId = TenantId, Name = "Proveedor Uno", IsActive = true });
        Db.Set<PurchaseOrder>().Add(new PurchaseOrder
        {
            PurchaseOrderId = PoId, PublicId = PoPublicId, TenantId = TenantId, SupplierId = 401, WarehouseId = WarehouseId,
            Number = "PO-00001", OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StatusCodeId = StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });
        Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine { PurchaseOrderLineId = PoLineId, PurchaseOrderId = PoId, ProductId = ProductNoneId, QtyOrdered = 10m, UnitCost = 12.5m });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        PurchaseOrders.Pending = new PurchaseOrderForReceipt(PoId, "PO-00001", WarehouseId,
            new[] { new PurchaseOrderPendingLine(PoLineId, ProductNoneId, 10m, 12.5m) });
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
    }
}

/// <summary>Costura de P8 falsa: entrega la PO pendiente configurada y registra cada ApplyReceiptAsync.</summary>
internal sealed class ReceivingFakePurchaseOrders : IPurchaseOrderReceiving
{
    public PurchaseOrderForReceipt? Pending { get; set; }
    public List<(int PurchaseOrderId, IReadOnlyList<PurchaseOrderReceiptQty> Quantities)> Applied { get; } = new();

    public Task<PurchaseOrderForReceipt> LockForReceiptAsync(Guid purchaseOrderPublicId, CancellationToken ct)
        => Task.FromResult(Pending ?? throw new NotFoundException("Orden de compra", feminine: true));

    public Task ApplyReceiptAsync(int purchaseOrderId, IReadOnlyList<PurchaseOrderReceiptQty> quantities, CancellationToken ct)
    {
        Applied.Add((purchaseOrderId, quantities.ToList()));
        return Task.CompletedTask;
    }
}

/// <summary>Costura de P9 falsa: toma para cruce de muelle lo que diga Take por línea (0 por defecto).</summary>
internal sealed class ReceivingFakeCrossDock : IReceiptConfirmationParticipant
{
    public Func<ReceiptLine, decimal> Take { get; set; } = _ => 0m;
    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<int, decimal>> OnReceiptConfirmedAsync(ReceiptHeader receipt, IReadOnlyList<ReceiptLine> lines, CancellationToken ct)
    {
        Calls++;
        IReadOnlyDictionary<int, decimal> result = lines.Select(l => (l.ReceiptLineId, Qty: Take(l))).Where(x => x.Qty > 0m)
            .ToDictionary(x => x.ReceiptLineId, x => x.Qty);
        return Task.FromResult(result);
    }
}

internal sealed class ReceivingNullSecurityEventWriter : ISecurityEventWriter
{
    public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        => Task.CompletedTask;
}

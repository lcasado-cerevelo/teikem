using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P0) — detalle de un movimiento del Kárdex (GET /inventory/transactions/{id}): documento de origen resuelto por tipo
/// con lo necesario para abrirlo (PublicId del recibo, la recolección y la orden de compra; el conteo por id; la tarea con su
/// documento padre), movimientos relacionados (misma referencia o mismo asiento), 404 de otro tenant; y la búsqueda de
/// posiciones entre almacenes. InMemory con WmsFixture y el ledger real.
/// </summary>
public class KardexDetailTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B1, WarehouseBin B2, Product P, Client Client);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<WarehouseLayoutService>();
        });
        var w = await f.AddWarehouseAsync("ALM-01");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var b1 = await f.AddBinAsync(z, "A-01");
        var b2 = await f.AddBinAsync(z, "A-02");
        var client = await f.AddClientAsync("C1");
        var p = await f.AddProductAsync("SKU-1", ownerClientId: client.ClientId);
        return new World(f, w, b1, b2, p, client);
    }

    private static InventoryPosting Receipt(World w, decimal qty, string? refType = null, int? refId = null)
        => new(InventoryTxnTypes.Receipt, w.P.ProductId, qty, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B1.WarehouseBinId, RefEntityType: refType, RefId: refId);

    [Fact]
    public async Task Receipt_document_with_public_id_party_and_related_movements()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var asn = new Asn
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.W.WarehouseId, ClientId = w.Client.ClientId, Reference = "ASN-REF-7",
            StatusCodeId = w.F.StatusId(StatusDomains.AsnStatus, AsnStatuses.Received), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        w.F.Db.Add(asn);
        await w.F.Db.SaveChangesAsync();
        var receipt = await w.F.AddReceiptAsync(w.W, ReceiptStatuses.Received, ReceiptTypes.Asn);
        var tracked = await w.F.Db.ReceiptHeaders.FindAsync(receipt.ReceiptHeaderId);
        tracked!.AsnId = asn.AsnId;
        tracked.ReceivedAtUtc = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var ids = await w.F.PostAsync(Receipt(w, 5m, EntityTypes.Receipt, receipt.ReceiptHeaderId), Receipt(w, 2m, EntityTypes.Receipt, receipt.ReceiptHeaderId));
        await w.F.PostAsync(Receipt(w, 1m));   // otro asiento, sin referencia

        var detail = await w.F.Get<InventoryReadService>().TransactionDetailAsync(ids[0], InventoryScope.Any, default);
        Assert.Equal(ids[0], detail.Transaction.Id);
        Assert.Equal("Cliente C1", detail.OwnerName);
        var doc = detail.Document!;
        Assert.Equal(EntityTypes.Receipt, doc.EntityCode);
        Assert.Equal(receipt.ReceiptHeaderId, doc.Id);
        Assert.Equal(receipt.PublicId, doc.PublicId);
        Assert.Equal(receipt.Number, doc.Number);
        Assert.Equal(ReceiptStatuses.Received, doc.StatusCode);
        Assert.Equal(new DateTime(2026, 9, 29, 15, 0, 0), doc.DateUtc);
        Assert.Equal("Cliente C1", doc.PartyName);
        Assert.Equal("ASN-REF-7", doc.Reference);
        Assert.Equal(ids.OrderBy(i => i).ToArray(), detail.Related.Select(r => r.Id).ToArray());   // el propio id y su pareja; no el otro asiento
        Assert.False(detail.RelatedTruncated);
    }

    [Fact]
    public async Task Pick_batch_cycle_count_and_purchase_order_documents()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        await w.F.PostAsync(Receipt(w, 20m));
        var order = await w.F.AddOrderAsync(w.Client.ClientId, "2026-000123");
        var batch = new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.W.WarehouseId, Number = "EMP-00042",
            StatusCodeId = w.F.StatusId(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), TransportOrderId = order.TransportOrderId,
            CollectedAtUtc = DateTime.UtcNow, IsActive = true,
        };
        var count = new CycleCount
        {
            TenantId = WmsFixture.TenantId, WarehouseId = w.W.WarehouseId, Number = "CC-00007",
            StatusCodeId = w.F.StatusId(StatusDomains.CycleCountStatus, CycleCountStatuses.Reconciled), CreatedAtUtc = DateTime.UtcNow, IsActive = true,
        };
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Proveedor Uno", IsActive = true };
        w.F.Db.AddRange(batch, count, supplier);
        await w.F.Db.SaveChangesAsync();
        var po = new PurchaseOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, SupplierId = supplier.SupplierId, WarehouseId = w.W.WarehouseId,
            Number = "PO-00003", OrderDate = new DateOnly(2026, 9, 28), StatusCodeId = w.F.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        w.F.Db.Add(po);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var issue = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, w.P.ProductId, 1m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, RefEntityType: EntityTypes.PickBatch, RefId: batch.PickBatchId)))[0];
        var countAdj = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.P.ProductId, 1m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, RefEntityType: EntityTypes.CycleCount, RefId: count.CycleCountId, ReasonCode: AdjustmentReasons.CountVariance)))[0];
        var poAdj = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, w.P.ProductId, 1m, ToWarehouseId: w.W.WarehouseId,
            ToBinId: w.B1.WarehouseBinId, RefEntityType: EntityTypes.PurchaseOrder, RefId: po.PurchaseOrderId, ReasonCode: AdjustmentReasons.PoShortage)))[0];
        var reads = w.F.Get<InventoryReadService>();

        var pick = (await reads.TransactionDetailAsync(issue, InventoryScope.Any, default)).Document!;
        Assert.Equal((EntityTypes.PickBatch, batch.PublicId, "EMP-00042", PickBatchStatuses.Packed), (pick.EntityCode, pick.PublicId!.Value, pick.Number, pick.StatusCode));
        Assert.Equal(("Cliente C1", "2026-000123"), (pick.PartyName, pick.Reference));

        var cc = (await reads.TransactionDetailAsync(countAdj, InventoryScope.Any, default)).Document!;
        Assert.Equal((EntityTypes.CycleCount, count.CycleCountId, "CC-00007", CycleCountStatuses.Reconciled), (cc.EntityCode, cc.Id, cc.Number, cc.StatusCode));
        Assert.Null(cc.PublicId);   // el conteo se abre por id

        var poDoc = (await reads.TransactionDetailAsync(poAdj, InventoryScope.Any, default)).Document!;
        Assert.Equal((EntityTypes.PurchaseOrder, po.PublicId, "PO-00003", "Proveedor Uno"), (poDoc.EntityCode, poDoc.PublicId!.Value, poDoc.Number, poDoc.PartyName));
        Assert.Equal(new DateTime(2026, 9, 28), poDoc.DateUtc);
    }

    [Fact]
    public async Task Warehouse_task_document_brings_its_parent_and_a_manual_move_relates_to_its_own_posting()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var receipt = await w.F.AddReceiptAsync(w.W, ReceiptStatuses.Received);
        await w.F.PostAsync(Receipt(w, 10m, EntityTypes.Receipt, receipt.ReceiptHeaderId));
        var task = await w.F.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w.W.WarehouseId, w.P.ProductId, 4m,
            FromBinId: w.B1.WarehouseBinId, ToBinId: w.B2.WarehouseBinId, RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId));
        var move = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Transfer, w.P.ProductId, 4m, FromWarehouseId: w.W.WarehouseId,
            FromBinId: w.B1.WarehouseBinId, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B2.WarehouseBinId, RefEntityType: EntityTypes.WarehouseTask,
            RefId: task.WarehouseTaskId)))[0];

        var doc = (await w.F.Get<InventoryReadService>().TransactionDetailAsync(move, InventoryScope.Any, default)).Document!;
        Assert.Equal(EntityTypes.WarehouseTask, doc.EntityCode);
        Assert.Equal($"Tarea #{task.WarehouseTaskId}", doc.Number);
        Assert.Equal(WarehouseTaskTypes.Putaway, doc.Reference);   // tipo de tarea (etiqueta del catálogo)
        Assert.Equal(WarehouseTaskStatuses.Pending, doc.StatusCode);
        Assert.Equal(EntityTypes.Receipt, doc.Parent!.EntityCode);
        Assert.Equal(receipt.PublicId, doc.Parent.PublicId);
        Assert.Null(doc.Parent.Parent);

        // Ajustes manuales del mismo asiento (mismo instante, usuario y producto): se relacionan entre sí.
        var pair = await w.F.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Adjustment, w.P.ProductId, 1m, FromWarehouseId: w.W.WarehouseId, FromBinId: w.B1.WarehouseBinId,
                ReasonCode: AdjustmentReasons.Damage),
            new InventoryPosting(InventoryTxnTypes.Adjustment, w.P.ProductId, 1m, FromWarehouseId: w.W.WarehouseId, FromBinId: w.B2.WarehouseBinId,
                ReasonCode: AdjustmentReasons.Damage));
        var manual = await w.F.Get<InventoryReadService>().TransactionDetailAsync(pair[1], InventoryScope.Any, default);
        Assert.Null(manual.Document);
        Assert.Equal(pair.OrderBy(i => i).ToArray(), manual.Related.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Movement_of_another_tenant_or_unknown_id_is_404()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        long foreign;
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var ow = await w.F.AddWarehouseAsync("OTRO");
            var oz = await w.F.AddZoneAsync(ow, "Z", ZoneTypes.Picking);
            var ob = await w.F.AddBinAsync(oz, "X-01");
            var op = await w.F.AddProductAsync("SKU-X");
            foreign = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, op.ProductId, 1m, ToWarehouseId: ow.WarehouseId,
                ToBinId: ob.WarehouseBinId)))[0];
        }
        var reads = w.F.Get<InventoryReadService>();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => reads.TransactionDetailAsync(foreign, InventoryScope.Any, default));
        Assert.Equal("Movimiento no encontrado.", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => reads.TransactionDetailAsync(999_999, InventoryScope.Any, default));

        // Con scope de dueño, un producto de otro dueño tampoco se ve.
        var own = await w.F.AddProductAsync("PROPIO");
        var mine = (await w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, own.ProductId, 1m, ToWarehouseId: w.W.WarehouseId,
            ToBinId: w.B1.WarehouseBinId)))[0];
        await Assert.ThrowsAsync<NotFoundException>(() => reads.TransactionDetailAsync(mine, new InventoryScope(w.Client.ClientId), default));
    }

    [Fact]
    public async Task Bin_search_crosses_warehouses_by_bin_or_zone_code()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var w2 = await w.F.AddWarehouseAsync("ALM-02");
        var z2 = await w.F.AddZoneAsync(w2, "RES", ZoneTypes.Reserve);
        await w.F.AddBinAsync(z2, "A-01");
        await w.F.AddBinAsync(z2, "A-09", isActive: false);
        var layout = w.F.Get<WarehouseLayoutService>();

        var byCode = await layout.SearchBinsAsync("A-0", null, false, 0, default);
        Assert.Equal(new[] { ("ALM-01", "A-01"), ("ALM-01", "A-02"), ("ALM-02", "A-01") }, byCode.Select(b => (b.WarehouseCode, b.Code)).ToArray());
        Assert.Equal("PCK", byCode[0].ZoneCode);
        Assert.Equal(4, (await layout.SearchBinsAsync("A-0", null, true, 0, default)).Count);   // con la inactiva A-09
        Assert.Equal("ALM-02", Assert.Single(await layout.SearchBinsAsync("RES", null, false, 10, default)).WarehouseCode);   // por código de zona
        Assert.Single(await layout.SearchBinsAsync(null, new[] { w2.PublicId }, false, 10, default));
        Assert.Single(await layout.SearchBinsAsync("A", null, false, 1, default));
        Assert.Equal(50, WarehouseLayoutService.MaxBinSearch);

        // Otro tenant no aporta posiciones.
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var ow = await w.F.AddWarehouseAsync("OTRO");
            var oz = await w.F.AddZoneAsync(ow, "Z", ZoneTypes.Picking);
            await w.F.AddBinAsync(oz, "A-05");
        }
        Assert.DoesNotContain(await layout.SearchBinsAsync("A-05", null, true, 50, default), b => b.WarehouseCode == "OTRO");
    }
}

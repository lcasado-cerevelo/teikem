using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0 — resolvers de pertenencia del WMS (InMemory, dos tenants): cada uno responde true para un id del tenant
/// activo y false para uno de otro tenant o inexistente (404 sin oráculo). WAREHOUSE_DOCK (sin TenantId) se resuelve por su
/// almacén filtrado. INVENTORY_SERIAL, WAREHOUSE_TASK y CROSSDOCK_ALLOCATION van con resolver cerrado.
/// </summary>
public class WmsOwnedEntityResolversTests
{
    [Fact]
    public async Task Resolvers_see_only_the_active_tenant()
    {
        await using var f = await WmsFixture.CreateAsync();
        var mine = await f.AddWarehouseAsync("ALM-01");
        var other = await f.AddWarehouseAsync("ALM-99", tenantId: WmsFixture.OtherTenantId);
        var myDock = await f.AddDockAsync(mine, "D1");
        var otherDock = await f.AddDockAsync(other, "D1");
        var myProduct = await f.AddProductAsync("PN");
        var otherProduct = await f.AddProductAsync("PX", tenantId: WmsFixture.OtherTenantId);

        void Seed(object mineEntity, object otherEntity)
        {
            f.Db.Add(mineEntity);
            f.Db.Add(otherEntity);
            f.Db.SaveChanges();
            f.Db.ChangeTracker.Clear();
        }
        var statusId = f.StatusId(StatusDomains.WarehouseStatus, WarehouseStatuses.Active);
        Seed(new ReceiptHeader { ReceiptHeaderId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, Number = "REC-1", StatusCodeId = statusId },
             new ReceiptHeader { ReceiptHeaderId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, Number = "REC-1", StatusCodeId = statusId });
        Seed(new Asn { AsnId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, StatusCodeId = statusId },
             new Asn { AsnId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, StatusCodeId = statusId });
        Seed(new CycleCount { CycleCountId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, Number = "CC-1", StatusCodeId = statusId },
             new CycleCount { CycleCountId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, Number = "CC-1", StatusCodeId = statusId });
        Seed(new PickBatch { PickBatchId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, Number = "EMP-1", StatusCodeId = statusId },
             new PickBatch { PickBatchId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, Number = "EMP-1", StatusCodeId = statusId });
        Seed(new DockAppointment { DockAppointmentId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, WarehouseDockId = myDock.WarehouseDockId, StatusCodeId = statusId },
             new DockAppointment { DockAppointmentId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, WarehouseDockId = otherDock.WarehouseDockId, StatusCodeId = statusId });
        Seed(new CrossDockPlan { CrossDockPlanId = 1, TenantId = WmsFixture.TenantId, WarehouseId = mine.WarehouseId, Number = "XD-1", StatusCodeId = statusId },
             new CrossDockPlan { CrossDockPlanId = 2, TenantId = WmsFixture.OtherTenantId, WarehouseId = other.WarehouseId, Number = "XD-1", StatusCodeId = statusId });
        Seed(new Supplier { SupplierId = 1, TenantId = WmsFixture.TenantId, Name = "Prov" },
             new Supplier { SupplierId = 2, TenantId = WmsFixture.OtherTenantId, Name = "Prov" });
        Seed(new PurchaseOrder { PurchaseOrderId = 1, TenantId = WmsFixture.TenantId, SupplierId = 1, WarehouseId = mine.WarehouseId, Number = "PO-1", StatusCodeId = statusId },
             new PurchaseOrder { PurchaseOrderId = 2, TenantId = WmsFixture.OtherTenantId, SupplierId = 2, WarehouseId = other.WarehouseId, Number = "PO-1", StatusCodeId = statusId });

        var cases = new (IOwnedEntityResolver Resolver, int Mine, int Other)[]
        {
            (new WarehouseOwnedEntityResolver(f.Db), mine.WarehouseId, other.WarehouseId),
            (new WarehouseDockOwnedEntityResolver(f.Db), myDock.WarehouseDockId, otherDock.WarehouseDockId),
            (new ProductOwnedEntityResolver(f.Db), myProduct.ProductId, otherProduct.ProductId),
            (new ReceiptOwnedEntityResolver(f.Db), 1, 2),
            (new AsnOwnedEntityResolver(f.Db), 1, 2),
            (new CycleCountOwnedEntityResolver(f.Db), 1, 2),
            (new PickBatchOwnedEntityResolver(f.Db), 1, 2),
            (new DockAppointmentOwnedEntityResolver(f.Db), 1, 2),
            (new CrossDockPlanOwnedEntityResolver(f.Db), 1, 2),
            (new PurchaseOrderOwnedEntityResolver(f.Db), 1, 2),
            (new SupplierOwnedEntityResolver(f.Db), 1, 2),
        };
        Assert.Equal(11, cases.Select(c => c.Resolver.EntityTypeCode).Distinct().Count());
        foreach (var (resolver, m, o) in cases)
        {
            Assert.True(await resolver.ExistsInTenantAsync(m, default), $"{resolver.EntityTypeCode} propio");
            Assert.False(await resolver.ExistsInTenantAsync(o, default), $"{resolver.EntityTypeCode} de otro tenant");
            Assert.False(await resolver.ExistsInTenantAsync(987654, default), $"{resolver.EntityTypeCode} inexistente");
        }

        // Desde el otro tenant, los papeles se invierten (el TenantId sale del contexto).
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.True(await new WarehouseDockOwnedEntityResolver(f.Db).ExistsInTenantAsync(otherDock.WarehouseDockId, default));
            Assert.False(await new WarehouseDockOwnedEntityResolver(f.Db).ExistsInTenantAsync(myDock.WarehouseDockId, default));
        }

        foreach (var code in new[]
                 {
                     EntityTypes.InventorySerial, EntityTypes.WarehouseTask, EntityTypes.CrossDockAllocation,
                     EntityTypes.StockBalance, EntityTypes.InventoryTransaction, EntityTypes.ReceiptLine,
                 })
            Assert.False(await new ClosedOwnedEntityResolver(code).ExistsInTenantAsync(1, default));
    }
}

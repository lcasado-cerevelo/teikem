using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Ajuste del dueño del producto (2026-09-30) al Lote 12: el proveedor y el almacén de una orden de compra se cambian por
/// PATCH solo mientras está en DRAFT. Fuera de DRAFT un valor distinto → 409 SupplierWarehouseOnlyDraft (antes que la
/// capacidad EDIT_PURCHASE_ORDER); un valor igual al actual no es un cambio. En DRAFT se valida como en el alta (404 ajeno o
/// inexistente, 422 dado de baja) y un error no deja cambios a medias. InMemory con WmsFixture y PurchaseOrderService real.
/// </summary>
public sealed class PurchaseOrderSupplierWarehouseEditTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<PurchaseOrderService>());

    private static async Task<Supplier> AddSupplierAsync(WmsFixture f, string name, bool isActive = true, int? tenantId = null)
    {
        var s = new Supplier { TenantId = tenantId ?? WmsFixture.TenantId, Name = name, IsActive = isActive };
        f.Db.Set<Supplier>().Add(s);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return s;
    }

    private static async Task SetStatusAsync(WmsFixture f, Guid publicId, string status)
    {
        var po = await f.Db.Set<PurchaseOrder>().SingleAsync(p => p.PublicId == publicId);
        po.StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, status);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<PurchaseOrder> StoredAsync(WmsFixture f, Guid publicId)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PublicId == publicId);
    }

    [Fact]
    public async Task Draft_order_changes_supplier_and_warehouse_and_keeps_its_lines()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var s2 = await AddSupplierAsync(f, "Proveedor Dos");
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var draft = await orders.CreateAsync(new PurchaseOrderCreateRequest(s1.SupplierId, w1.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 4m, 2m) }), default);

        var edited = await orders.UpdateAsync(draft.PublicId,
            new PurchaseOrderPatchRequest(RowVersion: draft.RowVersion, SupplierId: s2.SupplierId, WarehousePublicId: w2.PublicId), default);

        Assert.Equal((s2.SupplierId, "Proveedor Dos", w2.PublicId, "W2"), (edited.SupplierId, edited.SupplierName, edited.WarehousePublicId, edited.WarehouseCode));
        Assert.Equal(PurchaseOrderStatuses.Draft, edited.StatusCode);
        var line = Assert.Single(edited.Lines);
        Assert.Equal((pa.PublicId, 4m, 2m), (line.ProductPublicId, line.QtyOrdered, line.UnitCost));
        var stored = await StoredAsync(f, draft.PublicId);
        Assert.Equal((s2.SupplierId, w2.WarehouseId), (stored.SupplierId, stored.WarehouseId));

        // Solo uno de los dos: el otro no cambia (null = sin cambio).
        var back = await orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(WarehousePublicId: w1.PublicId), default);
        Assert.Equal((s2.SupplierId, w1.PublicId), (back.SupplierId, back.WarehousePublicId));
    }

    [Fact]
    public async Task Draft_order_validates_supplier_and_warehouse_like_the_creation_without_partial_changes()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var wInactive = await f.AddWarehouseAsync("W-BAJA", isActive: false);
        var wOther = await f.AddWarehouseAsync("W-OTRO", tenantId: WmsFixture.OtherTenantId);
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var s2 = await AddSupplierAsync(f, "Proveedor Dos");
        var sInactive = await AddSupplierAsync(f, "Proveedor Baja", isActive: false);
        var sOther = await AddSupplierAsync(f, "Proveedor Ajeno", tenantId: WmsFixture.OtherTenantId);
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var draft = await orders.CreateAsync(new PurchaseOrderCreateRequest(s1.SupplierId, w1.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 1m, 1m) }), default);

        var supplierInactive = await Assert.ThrowsAsync<StatusRuleException>(() =>
            orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(SupplierId: sInactive.SupplierId), default));
        Assert.Equal(PurchaseOrderRules.SupplierInactive, supplierInactive.Message);
        Assert.Equal(422, supplierInactive.StatusCode);

        var warehouseInactive = await Assert.ThrowsAsync<StatusRuleException>(() =>
            orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(SupplierId: s2.SupplierId, WarehousePublicId: wInactive.PublicId), default));
        Assert.Equal(PurchaseOrderRules.WarehouseInactive, warehouseInactive.Message);

        var supplierOther = await Assert.ThrowsAsync<NotFoundException>(() =>
            orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(SupplierId: sOther.SupplierId), default));
        Assert.Equal("Proveedor no encontrado.", supplierOther.Message);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(SupplierId: 987654), default));

        var warehouseOther = await Assert.ThrowsAsync<NotFoundException>(() =>
            orders.UpdateAsync(draft.PublicId, new PurchaseOrderPatchRequest(WarehousePublicId: wOther.PublicId), default));
        Assert.Equal("Almacén no encontrado.", warehouseOther.Message);

        // Ningún intento fallido dejó cambios (tampoco el proveedor válido que venía junto al almacén dado de baja).
        var stored = await StoredAsync(f, draft.PublicId);
        Assert.Equal((s1.SupplierId, w1.WarehouseId), (stored.SupplierId, stored.WarehouseId));
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Sent)]
    [InlineData(PurchaseOrderStatuses.Partial)]
    [InlineData(PurchaseOrderStatuses.Received)]
    [InlineData(PurchaseOrderStatuses.Cancelled)]
    public async Task Outside_draft_a_different_supplier_or_warehouse_is_409(string status)
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var s2 = await AddSupplierAsync(f, "Proveedor Dos");
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var po = await orders.CreateAsync(new PurchaseOrderCreateRequest(s1.SupplierId, w1.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 1m, 1m) }), default);
        await SetStatusAsync(f, po.PublicId, status);

        var supplier = await Assert.ThrowsAsync<ConflictException>(() =>
            orders.UpdateAsync(po.PublicId, new PurchaseOrderPatchRequest(SupplierId: s2.SupplierId), default));
        Assert.Equal(PurchaseOrderRules.SupplierWarehouseOnlyDraft, supplier.Message);
        Assert.Equal("El proveedor y el almacén solo se cambian mientras la orden de compra está en borrador.", supplier.Message);
        Assert.Equal(409, supplier.StatusCode);

        var warehouse = await Assert.ThrowsAsync<ConflictException>(() =>
            orders.UpdateAsync(po.PublicId, new PurchaseOrderPatchRequest(WarehousePublicId: w2.PublicId), default));
        Assert.Equal(PurchaseOrderRules.SupplierWarehouseOnlyDraft, warehouse.Message);

        var stored = await StoredAsync(f, po.PublicId);
        Assert.Equal((s1.SupplierId, w1.WarehouseId), (stored.SupplierId, stored.WarehouseId));
    }

    [Fact]
    public async Task Outside_draft_the_409_comes_before_the_edit_capability_and_the_same_values_are_not_a_change()
    {
        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var po = await orders.CreateAsync(new PurchaseOrderCreateRequest(s1.SupplierId, w1.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 1m, 1m) }), default);
        await SetStatusAsync(f, po.PublicId, PurchaseOrderStatuses.Sent);

        // Sin regla de capacidad (permitido por defecto): los mismos valores más una nota se aceptan en SENT.
        var same = await orders.UpdateAsync(po.PublicId,
            new PurchaseOrderPatchRequest(Notes: "misma", SupplierId: s1.SupplierId, WarehousePublicId: w1.PublicId), default);
        Assert.Equal(("misma", s1.SupplierId, w1.PublicId), (same.Notes, same.SupplierId, same.WarehousePublicId));

        // Con EDIT_PURCHASE_ORDER negada en SENT (como la siembra): el cambio de almacén sigue siendo 409 y lo demás 422.
        f.Db.StatusCapabilities.Add(new StatusCapability
        {
            StatusCapabilityId = 1, TenantId = null, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, EntityTypes.PurchaseOrder),
            StatusCodeId = f.StatusId(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent),
            CapabilityLookupId = f.LookupId(LookupDomains.Capability, Capabilities.EditPurchaseOrder), IsAllowed = false,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            orders.UpdateAsync(po.PublicId, new PurchaseOrderPatchRequest(WarehousePublicId: w2.PublicId), default));
        Assert.Equal(PurchaseOrderRules.SupplierWarehouseOnlyDraft, conflict.Message);
        var denied = await Assert.ThrowsAsync<StatusRuleException>(() =>
            orders.UpdateAsync(po.PublicId, new PurchaseOrderPatchRequest(Notes: "x", SupplierId: s1.SupplierId), default));
        Assert.Equal(422, denied.StatusCode);
    }

    [Fact]
    public async Task Supplier_and_warehouse_are_no_longer_rejected_as_immutable_extra_fields()
    {
        // El cuerpo JSON {"supplierId":..} ahora se enlaza a la propiedad; los demás campos fijos siguen siendo 400.
        var json = System.Text.Json.JsonSerializer.Deserialize<PurchaseOrderPatchRequest>(
            "{\"supplierId\":7,\"warehousePublicId\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"number\":\"PO-1\"}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(7, json.SupplierId);
        Assert.Equal(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), json.WarehousePublicId);
        Assert.Equal(new[] { "number" }, json.Extra!.Keys);

        await using var f = await CreateAsync();
        var w1 = await f.AddWarehouseAsync("W1");
        var s1 = await AddSupplierAsync(f, "Proveedor Uno");
        var pa = await f.AddProductAsync("PA");
        var orders = f.Get<PurchaseOrderService>();
        var po = await orders.CreateAsync(new PurchaseOrderCreateRequest(s1.SupplierId, w1.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(pa.PublicId, 1m, 1m) }), default);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => orders.UpdateAsync(po.PublicId, json with { SupplierId = null, WarehousePublicId = null }, default));
        Assert.Equal(PurchaseOrderRules.ImmutableField("number"), Assert.Single(ex.Errors!["number"]));
    }
}

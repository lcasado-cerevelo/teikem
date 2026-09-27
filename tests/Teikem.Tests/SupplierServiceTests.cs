using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P8, maestro L459) — pruebas de SERVICIO del catálogo de proveedores sobre InMemory (WmsFixture): nombre único
/// entre los activos (409), término de pago del catálogo (400), baja y reactivación, y la regla que lo conecta con Compras
/// (una orden nueva con un proveedor dado de baja → 422).
/// </summary>
public sealed class SupplierServiceTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<SupplierService>();
        s.AddSingleton<PurchaseOrderService>();
    });

    [Fact]
    public async Task Name_is_unique_among_active_suppliers()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<SupplierService>();
        var first = await svc.CreateAsync(new SupplierRequest("Proveedor Uno", ContactName: "Ana"), default);
        Assert.True(first.IsActive);

        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(new SupplierRequest("Proveedor Uno"), default));
        Assert.Equal(SupplierService.NameTaken, dup.Message);
        Assert.Equal("Ya existe un proveedor activo con ese nombre.", SupplierService.NameTaken);

        var other = await svc.CreateAsync(new SupplierRequest("Proveedor Dos"), default);
        var rename = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(other.Id, new SupplierPatchRequest(Name: "Proveedor Uno"), default));
        Assert.Equal(SupplierService.NameTaken, rename.Message);
        f.Db.ChangeTracker.Clear();

        var term = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(other.Id, new SupplierPatchRequest(PaymentTerm: "FOO"), default));
        Assert.True(term.Errors!.ContainsKey("paymentTerm"));
        f.Db.ChangeTracker.Clear();

        var updated = await svc.UpdateAsync(other.Id, new SupplierPatchRequest(Name: "Proveedor Tres", Phone: "787-555-0101"), default);
        Assert.Equal(("Proveedor Tres", "787-555-0101"), (updated.Name, updated.Phone));
    }

    [Fact]
    public async Task Deactivated_supplier_frees_its_name_and_cannot_be_reactivated_while_taken()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<SupplierService>();
        var first = await svc.CreateAsync(new SupplierRequest("Proveedor Uno"), default);

        var off = await svc.SetActiveAsync(first.Id, false, default);
        Assert.False(off.IsActive);
        Assert.DoesNotContain(await svc.ListAsync(false, null, default), s => s.Id == first.Id);

        var second = await svc.CreateAsync(new SupplierRequest("Proveedor Uno"), default);
        Assert.NotEqual(first.Id, second.Id);
        var reactivate = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(first.Id, true, default));
        Assert.Equal(SupplierService.NameTaken, reactivate.Message);
    }

    [Fact]
    public async Task Purchase_order_with_a_deactivated_supplier_is_rejected()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var p = await f.AddProductAsync("PA");
        var svc = f.Get<SupplierService>();
        var supplier = await svc.CreateAsync(new SupplierRequest("Proveedor Uno"), default);
        await svc.SetActiveAsync(supplier.Id, false, default);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<PurchaseOrderService>().CreateAsync(
            new PurchaseOrderCreateRequest(supplier.Id, w.PublicId, Lines: new[] { new PurchaseOrderLineRequest(p.PublicId, 1m, 1m) }), default));
        Assert.Equal(PurchaseOrderRules.SupplierInactive, ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<PurchaseOrder>().AsNoTracking().ToListAsync());
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P1/P2; maestro L326 y L780) — bajas vigiladas por documentos cuya reversa o reconciliación ENTRA al inventario
/// (el ledger rechaza entradas de un producto inactivo o a un almacén INACTIVE):
/// - producto: recolección COLLECTED, o PACKED con la orden activa en etapa inicial, y conteo OPEN/COUNTED → 409;
///   una PACKED cuya orden ya avanzó no bloquea (nunca se elimina);
/// - almacén (baja terminal): recolección PACKED con la orden en etapa inicial → 409 con Errors.pickBatches.
/// </summary>
public sealed class WmsDeactivationGuardTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Bin, Product P, int ClientId);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ProductService>();
            s.AddSingleton<WarehouseService>();
            s.AddSingleton<CycleCountService>();
            s.AddSingleton<Teikem.Infrastructure.Abstractions.ITenantClock>(Teikem.Infrastructure.Abstractions.TenantClock.Default);   // Lote 14
            s.AddSingleton<InventoryReconciler>();   // Lote 14: TraceabilityService delega la conciliación
            s.AddSingleton<InventoryReadService>();
        });
        var w = await f.AddWarehouseAsync("W2");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PN");
        var client = await f.AddClientAsync("C1");
        return new World(f, w, bin, p, client.ClientId);
    }

    /// <summary>Recolección de TODO el producto (queda en mano 0), con su orden opcional.</summary>
    private static async Task<PickBatch> PickAllAsync(World x, decimal qty, string status, int? orderId = null, bool manual = false)
    {
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, x.P.ProductId, qty, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));
        var ids = await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, x.P.ProductId, qty, FromWarehouseId: x.W.WarehouseId, FromBinId: x.Bin.WarehouseBinId));
        var b = new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = x.W.WarehouseId, Number = "EMP-" + Guid.NewGuid().ToString("N")[..5],
            StatusCodeId = x.F.StatusId(StatusDomains.PickBatchStatus, status), TransportOrderId = orderId, CollectedAtUtc = DateTime.UtcNow, IsActive = true,
            ManualIssueReasonId = manual ? x.F.LookupId(LookupDomains.ManualIssueReason, ManualIssueReasons.Sample) : null,   // 2026-10-11
        };
        x.F.Db.Set<PickBatch>().Add(b);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.Set<PickBatchLine>().Add(new PickBatchLine
        {
            PickBatchId = b.PickBatchId, ProductId = x.P.ProductId, FromBinId = x.Bin.WarehouseBinId, Quantity = qty, IssueTxnId = ids[0],
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return b;
    }

    private static async Task<bool> ProductActiveAsync(World x)
        => (await x.F.Db.Products.AsNoTracking().SingleAsync(p => p.ProductId == x.P.ProductId)).IsActive;

    [Fact]
    public async Task Product_with_a_collected_pick_batch_cannot_be_deactivated()
    {
        var x = await SeedAsync();
        await using var _ = x.F;
        await PickAllAsync(x, 5m, PickBatchStatuses.Collected);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.F.Get<ProductService>().DeactivateAsync(x.P.PublicId, default));
        Assert.Equal(ProductRules.DeactivateOpenDocs("PN"), ex.Message);
        Assert.True(await ProductActiveAsync(x));
    }

    [Fact]
    public async Task A_manual_issue_does_not_block_deactivating_the_product_or_the_warehouse()
    {
        // 2026-10-11: el despacho manual (DMA) queda en COLLECTED para siempre; no es un documento abierto.
        var x = await SeedAsync();
        await using var _ = x.F;
        await PickAllAsync(x, 5m, PickBatchStatuses.Collected, manual: true);

        await x.F.Get<ProductService>().DeactivateAsync(x.P.PublicId, default);
        Assert.False(await ProductActiveAsync(x));
        var done = await x.F.Get<WarehouseService>().DeactivateAsync(x.W.PublicId, null, default);
        Assert.Equal(WarehouseStatuses.Inactive, done.Warehouse.StatusCode);
    }

    [Fact]
    public async Task Product_with_a_packed_pick_batch_blocks_only_while_its_order_is_initial()
    {
        var x = await SeedAsync();
        await using var _ = x.F;
        var order = await x.F.AddOrderAsync(x.ClientId, "O-1");
        await PickAllAsync(x, 3m, PickBatchStatuses.Packed, order.TransportOrderId);
        await Assert.ThrowsAsync<ConflictException>(() => x.F.Get<ProductService>().DeactivateAsync(x.P.PublicId, default));

        // La orden avanzó: la recolección ya no se elimina, así que no bloquea la baja.
        (await x.F.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == order.TransportOrderId)).StatusCodeId =
            x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Confirmed);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        await x.F.Get<ProductService>().DeactivateAsync(x.P.PublicId, default);
        Assert.False(await ProductActiveAsync(x));
    }

    [Fact]
    public async Task Product_in_an_open_cycle_count_cannot_be_deactivated()
    {
        var x = await SeedAsync();
        await using var _ = x.F;
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, x.P.ProductId, 2m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));
        await x.F.Get<CycleCountService>().CreateAsync(new CycleCountCreateRequest(x.W.PublicId), default);
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, x.P.ProductId, 2m, FromWarehouseId: x.W.WarehouseId, FromBinId: x.Bin.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.F.Get<ProductService>().DeactivateAsync(x.P.PublicId, default));
        Assert.Equal(ProductRules.DeactivateOpenDocs("PN"), ex.Message);
        Assert.True(await ProductActiveAsync(x));
    }

    [Fact]
    public async Task Warehouse_with_a_deletable_packed_pick_batch_cannot_be_deactivated()
    {
        var x = await SeedAsync();
        await using var _ = x.F;
        var order = await x.F.AddOrderAsync(x.ClientId, "O-1");
        await PickAllAsync(x, 3m, PickBatchStatuses.Packed, order.TransportOrderId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.F.Get<WarehouseService>().DeactivateAsync(x.W.PublicId, null, default));
        Assert.Equal(WarehouseRules.WarehouseNotEmpty("W2"), ex.Message);
        Assert.Equal("Recolecciones sin empacar o con orden en etapa inicial: 1.", Assert.Single(ex.Errors!["pickBatches"]));

        // Con la orden avanzada, la recolección ya no se puede eliminar: la baja procede.
        (await x.F.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == order.TransportOrderId)).StatusCodeId =
            x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Confirmed);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var done = await x.F.Get<WarehouseService>().DeactivateAsync(x.W.PublicId, null, default);
        Assert.Equal(WarehouseStatuses.Inactive, done.Warehouse.StatusCode);

        // D26: la baja es definitiva; una segunda baja → 422.
        x.F.Db.ChangeTracker.Clear();
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => x.F.Get<WarehouseService>().DeactivateAsync(x.W.PublicId, null, default));
        Assert.Equal(WarehouseRules.WarehouseInactiveMessage, again.Message);
    }
    [Fact]
    public async Task Product_with_stock_on_hand_cannot_be_deactivated_and_once_inactive_leaves_selectors_but_stays_in_inventory()
    {
        // Maestro L326 (D25): la baja solo procede sin inventario en mano; inactivo desaparece de los selectores
        // (activeOnly) pero sigue en Inventario (saldos y Kárdex) e informes.
        var x = await SeedAsync();
        await using var _ = x.F;
        var products = x.F.Get<ProductService>();
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, x.P.ProductId, 5m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => products.DeactivateAsync(x.P.PublicId, default));
        Assert.Equal(ProductRules.DeactivateWithStock("PN", 5m), ex.Message);
        Assert.Equal("El producto PN tiene inventario en mano (5); no se puede desactivar.", ex.Message);
        Assert.True(await ProductActiveAsync(x));

        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, x.P.ProductId, 5m, FromWarehouseId: x.W.WarehouseId, FromBinId: x.Bin.WarehouseBinId));
        var detail = await products.DeactivateAsync(x.P.PublicId, default);
        Assert.False(detail.Product.IsActive);
        Assert.False(await ProductActiveAsync(x));

        Assert.DoesNotContain((await products.ListAsync(new ProductListQuery(ActiveOnly: true), InventoryScope.Any, default)).Items, i => i.PublicId == x.P.PublicId);
        Assert.Contains((await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default)).Items, i => i.PublicId == x.P.PublicId);
        var reads = x.F.Get<InventoryReadService>();
        var balances = await reads.BalancesAsync(new BalanceQuery(ProductPublicIds: new[] { x.P.PublicId }, IncludeZero: true), InventoryScope.Any, default);
        Assert.NotEmpty(balances.Items);
        var kardex = await reads.KardexAsync(new KardexQuery(ProductPublicIds: new[] { x.P.PublicId }), InventoryScope.Any, default);
        Assert.Equal(2, kardex.Total);

        // Reactivar lo devuelve a los selectores.
        Assert.True((await products.ReactivateAsync(x.P.PublicId, default)).Product.IsActive);
    }

    [Fact]
    public async Task Barcode_is_unique_among_active_products_and_preferred_bin_must_belong_to_preferred_warehouse()
    {
        var x = await SeedAsync();
        await using var _ = x.F;
        var products = x.F.Get<ProductService>();
        await products.CreateAsync(new ProductCreateRequest("PB1", "Barras 1", Barcode: "7501234567890"), default);

        var taken = await Assert.ThrowsAsync<ConflictException>(() =>
            products.CreateAsync(new ProductCreateRequest("PB2", "Barras 2", Barcode: "7501234567890"), default));
        Assert.Equal(ProductRules.BarcodeTaken, taken.Message);
        Assert.Equal("Ya existe un producto activo con ese código de barras.", taken.Message);

        var other = await x.F.AddWarehouseAsync("W9");
        var mismatch = await Assert.ThrowsAsync<ValidationException>(() => products.CreateAsync(new ProductCreateRequest("PM", "Otro almacén",
            PreferredWarehousePublicId: other.PublicId, PreferredBinId: x.Bin.WarehouseBinId), default));
        Assert.Equal(ProductRules.PreferredBinMismatch, Assert.Single(mismatch.Errors!["preferredBinId"]));
        Assert.Equal("La posición preferida debe pertenecer al almacén preferido.", ProductRules.PreferredBinMismatch);
        Assert.False(await x.F.Db.Products.AnyAsync(p => p.Sku == "PB2" || p.Sku == "PM"));
    }
}

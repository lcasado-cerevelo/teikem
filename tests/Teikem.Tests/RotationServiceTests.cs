using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Rotación FEFO / FIFO configurable (Lote 34), por los servicios: la recolección y el orden de salida usan el método de la compañía
/// (Tenant.RotationMethod) y, si el producto tiene excepción (Product.RotationMethod), la del producto.
/// </summary>
public class RotationServiceTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
        sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(),
        sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!)));

    private sealed record Scene(WmsFixture F, Warehouse W, Product P, InventoryLot Late, InventoryLot Early);

    /// <summary>Dos lotes del mismo producto: «TARDE» venció más lejos pero ENTRÓ primero (hace 30 días); «TEMPRANO» vence antes y entró ayer.</summary>
    private static async Task<Scene> SeedAsync()
    {
        var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PL", tracking: TrackingTypes.Lot);
        var today = TenantClock.Default.Today;
        var late = await f.AddLotAsync(p, "L-TARDE", today.AddDays(60));
        var early = await f.AddLotAsync(p, "L-TEMPRANO", today.AddDays(10));
        InventoryPosting In(InventoryLot lot) => new(InventoryTxnTypes.Receipt, p.ProductId, 5m, LotId: lot.LotId, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId);
        await f.PostAsync(In(late), In(early));
        foreach (var b in await f.Db.StockBalances.Where(b => b.ProductId == p.ProductId).ToListAsync())
            b.FirstReceivedAtUtc = b.LotId == late.LotId ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        return new Scene(f, w, p, late, early);
    }

    private static async Task SetMethodsAsync(Scene s, string? tenant, string? product)
    {
        if (tenant is not null) (await s.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationMethod = tenant;
        (await s.F.Db.Products.SingleAsync(x => x.ProductId == s.P.ProductId)).RotationMethod = product;
        await s.F.Db.SaveChangesAsync();
    }

    private static async Task<int?> CollectedLotAsync(Scene s)
    {
        var dto = await s.F.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(s.W.PublicId, new[] { new PickBatchLineRequest(s.P.PublicId, 1m) }), default);
        return dto.Lines.Single().LotId;
    }

    [Fact]
    public async Task By_default_a_company_collects_fefo_the_lot_that_expires_first()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        Assert.Equal(s.Early.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task A_fifo_company_collects_the_lot_that_entered_first_even_if_it_expires_later()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetMethodsAsync(s, tenant: "FIFO", product: null);
        Assert.Equal(s.Late.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task A_product_exception_beats_the_company_method_in_both_directions()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetMethodsAsync(s, tenant: "FIFO", product: "FEFO");      // la compañía es FIFO pero este producto es FEFO
        Assert.Equal(s.Early.LotId, await CollectedLotAsync(s));

        await SetMethodsAsync(s, tenant: "FEFO", product: "FIFO");      // y al revés
        Assert.Equal(s.Late.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task Exit_options_rank_follows_the_same_method()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        var service = new StockExitService(s.F.Db, s.F.Get<ILookupCache>());

        var fefo = await service.ListAsync(s.W.PublicId, new[] { s.P.PublicId }, 0, 50, default);
        Assert.Equal(new int?[] { s.Early.LotId, s.Late.LotId }, fefo.Items.OrderBy(i => i.Rank).Select(i => i.LotId).ToArray());

        await SetMethodsAsync(s, tenant: "FIFO", product: null);
        var fifo = await service.ListAsync(s.W.PublicId, new[] { s.P.PublicId }, 0, 50, default);
        Assert.Equal(new int?[] { s.Late.LotId, s.Early.LotId }, fifo.Items.OrderBy(i => i.Rank).Select(i => i.LotId).ToArray());
    }

    private static async Task SetEnforcementAsync(Scene s, string value)
    {
        (await s.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationEnforcement = value;
        await s.F.Db.SaveChangesAsync();
    }

    private static Task<PickBatchDto> CollectLotAsync(Scene s, int? lotId, decimal qty = 1m)
        => s.F.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(s.W.PublicId, new[] { new PickBatchLineRequest(s.P.PublicId, qty, LotId: lotId) }), default);

    [Fact]
    public async Task Suggest_mode_lets_the_user_pick_another_lot()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        var dto = await CollectLotAsync(s, s.Late.LotId);   // por omisión SUGGEST: el lote que NO toca sale igual
        Assert.Equal(s.Late.LotId, dto.Lines.Single().LotId);
    }

    [Fact]
    public async Task Enforce_mode_rejects_a_lot_that_rotation_does_not_assign()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetEnforcementAsync(s, RotationEnforcements.Enforce);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CollectLotAsync(s, s.Late.LotId));
        Assert.Equal(RotationRules.EnforcedMismatch(s.P.Sku, "P-01"), ex.Message);
    }

    [Fact]
    public async Task Enforce_mode_accepts_the_lot_rotation_assigns_and_picks_it_alone_when_none_is_given()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetEnforcementAsync(s, RotationEnforcements.Enforce);
        Assert.Equal(s.Early.LotId, (await CollectLotAsync(s, s.Early.LotId)).Lines.Single().LotId);
        Assert.Equal(s.Early.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task Enforce_mode_splits_the_quantity_across_lots_in_rotation_order()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetEnforcementAsync(s, RotationEnforcements.Enforce);
        var lines = (await CollectLotAsync(s, null, 7m)).Lines;   // 5 del que vence primero y 2 del otro
        Assert.Equal(new int?[] { s.Early.LotId, s.Late.LotId }, lines.OrderByDescending(l => l.Quantity).Select(l => l.LotId).ToArray());
        Assert.Equal(7m, lines.Sum(l => l.Quantity));
    }

    [Fact]
    public async Task Exit_options_tell_the_app_whether_rotation_is_enforced()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        var service = new StockExitService(s.F.Db, s.F.Get<ILookupCache>());
        Assert.Equal(RotationEnforcements.Suggest, (await service.ListAsync(s.W.PublicId, null, 0, 50, default)).RotationEnforcement);
        await SetEnforcementAsync(s, RotationEnforcements.Enforce);
        Assert.Equal(RotationEnforcements.Enforce, (await service.ListAsync(s.W.PublicId, null, 0, 50, default)).RotationEnforcement);
    }

    // ---- 2026-10-20: producto SIN lote — FIFO por omisión (lo que entró primero), o por posición ----

    private sealed record NoLotScene(WmsFixture F, Warehouse W, Product P, WarehouseBin Old, WarehouseBin New);

    /// <summary>Producto sin lote en dos posiciones: «Z-OLD» (código mayor) entró hace 30 días y «A-NEW» (código menor) entró ayer.</summary>
    private static async Task<NoLotScene> NoLotSeedAsync()
    {
        var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var zone = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var oldBin = await f.AddBinAsync(zone, "Z-OLD");
        var newBin = await f.AddBinAsync(zone, "A-NEW");
        var p = await f.AddProductAsync("PN");
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: oldBin.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: newBin.WarehouseBinId));
        foreach (var b in await f.Db.StockBalances.Where(b => b.ProductId == p.ProductId).ToListAsync())
            b.FirstReceivedAtUtc = b.WarehouseBinId == oldBin.WarehouseBinId ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        return new NoLotScene(f, w, p, oldBin, newBin);
    }

    private static async Task<int?> CollectedBinAsync(NoLotScene s)
    {
        var dto = await s.F.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(s.W.PublicId, new[] { new PickBatchLineRequest(s.P.PublicId, 1m) }), default);
        return dto.Lines.Single().BinId;
    }

    [Fact]
    public async Task A_product_without_lot_goes_out_fifo_by_default_even_if_another_position_has_a_lower_code()
    {
        var s = await NoLotSeedAsync();
        await using var _ = s.F;
        Assert.Equal(s.Old.WarehouseBinId, await CollectedBinAsync(s));
    }

    [Fact]
    public async Task The_company_can_choose_position_for_products_without_lot()
    {
        var s = await NoLotSeedAsync();
        await using var _ = s.F;
        (await s.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationMethodNoLot = RotationMethods.Position;
        await s.F.Db.SaveChangesAsync();
        Assert.Equal(s.New.WarehouseBinId, await CollectedBinAsync(s));   // «A-NEW» por su código, aunque entró después
    }

    [Fact]
    public async Task The_lot_method_does_not_change_how_products_without_lot_go_out()
    {
        var s = await NoLotSeedAsync();
        await using var _ = s.F;
        (await s.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationMethod = RotationMethods.Fifo;   // el de lote
        await s.F.Db.SaveChangesAsync();
        Assert.Equal(s.Old.WarehouseBinId, await CollectedBinAsync(s));   // el de «sin lote» sigue FIFO
    }

    [Fact]
    public async Task A_product_exception_can_ask_for_position_even_if_the_company_is_fifo()
    {
        var s = await NoLotSeedAsync();
        await using var _ = s.F;
        (await s.F.Db.Products.SingleAsync(x => x.ProductId == s.P.ProductId)).RotationMethod = RotationMethods.Position;
        await s.F.Db.SaveChangesAsync();
        Assert.Equal(s.New.WarehouseBinId, await CollectedBinAsync(s));
    }

    [Fact]
    public async Task An_exception_that_does_not_fit_the_product_type_is_ignored()
    {
        var s = await NoLotSeedAsync();
        await using var _ = s.F;
        (await s.F.Db.Products.SingleAsync(x => x.ProductId == s.P.ProductId)).RotationMethod = RotationMethods.Fefo;   // FEFO no aplica a un producto sin lote
        await s.F.Db.SaveChangesAsync();
        Assert.Equal(s.Old.WarehouseBinId, await CollectedBinAsync(s));   // manda el de la compañía para sin lote (FIFO)
    }

    // ---- ajustes de la compañía: validación de los dos métodos ----

    [Fact]
    public async Task Settings_expose_and_validate_the_two_methods()
    {
        await using var f = await WmsFixture.CreateAsync(sv => { sv.AddSingleton<TenantZoneCache>(); sv.AddSingleton<TenantService>(); });
        var svc = f.Get<TenantService>();
        var s = await svc.GetSettingsAsync(default);
        Assert.Equal(("FEFO", "FIFO"), (s.RotationMethod, s.RotationMethodNoLot));   // lo de siempre y el nuevo por omisión

        var saved = await svc.UpdateSettingsAsync(new TenantSettingsUpdateRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, RotationMethod: "fifo", RotationMethodNoLot: "position"), default);
        Assert.Equal(("FIFO", "POSITION"), (saved.RotationMethod, saved.RotationMethodNoLot));

        var badLot = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(new TenantSettingsUpdateRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, RotationMethod: "POSITION"), default));
        Assert.Contains(RotationRules.InvalidMethod, badLot.Errors.SelectMany(e => e.Value));   // por posición no es un método de productos con lote
        var badNoLot = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(new TenantSettingsUpdateRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, RotationMethodNoLot: "FEFO"), default));
        Assert.Contains(RotationRules.InvalidNoLotMethod, badNoLot.Errors.SelectMany(e => e.Value));   // FEFO no existe sin vencimiento
    }
}

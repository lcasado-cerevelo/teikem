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
/// Lote 6 (P7; maestro L782, D39) — filtros estructurados de la lista de Recolecciones sobre InMemory (WmsFixture): eliminadas
/// solo con includeDeleted, Desde/Hasta en UTC con 'hasta' inclusive (semiabierto al día siguiente), estatus sin distinguir
/// mayúsculas ni espacios, productos, y la búsqueda final aplicada DESPUÉS de los filtros. ListAsync no usa OrderService.
/// </summary>
public sealed class PickBatchServiceTests
{
    private static readonly DateTime Day10 = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

    private sealed record World(WmsFixture F, PickBatch Late10, PickBatch Early11, PickBatch Deleted, PickBatch Packed, Product Alfa, Product Beta);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(),
            sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(),
            null!)));
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var alfa = await f.AddProductAsync("SKU-ALFA");
        var beta = await f.AddProductAsync("SKU-BETA");
        var client = await f.AddClientAsync("C1");
        var order = await f.AddOrderAsync(client.ClientId, "2026-000555", packBatchNumber: "EMP-00004");

        async Task<PickBatch> Batch(string number, DateTime at, string status, bool active, Product p, int? orderId = null)
        {
            var b = new PickBatch
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = number,
                StatusCodeId = f.StatusId(StatusDomains.PickBatchStatus, status), TransportOrderId = orderId, CollectedAtUtc = at, IsActive = active,
            };
            f.Db.Set<PickBatch>().Add(b);
            await f.Db.SaveChangesAsync();
            f.Db.Set<PickBatchLine>().Add(new PickBatchLine { PickBatchId = b.PickBatchId, ProductId = p.ProductId, FromBinId = bin.WarehouseBinId, Quantity = 1m, IssueTxnId = 1 });
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
            return b;
        }

        var late10 = await Batch("EMP-00001", Day10.AddHours(23).AddMinutes(59), PickBatchStatuses.Collected, true, alfa);
        var early11 = await Batch("EMP-00002", Day10.AddDays(1), PickBatchStatuses.Collected, true, beta);
        var deleted = await Batch("EMP-00003", Day10.AddHours(12), PickBatchStatuses.Cancelled, false, alfa);
        var packed = await Batch("EMP-00004", Day10.AddDays(-1), PickBatchStatuses.Packed, true, alfa, order.TransportOrderId);
        return new World(f, late10, early11, deleted, packed, alfa, beta);
    }

    private static async Task<string[]> NumbersAsync(World w, PickBatchQuery q)
        => (await w.F.Get<PickBatchService>().ListAsync(q, default)).Items.Select(i => i.Number).OrderBy(n => n).ToArray();

    [Fact]
    public async Task Deleted_batches_only_with_include_deleted()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        Assert.Equal(new[] { "EMP-00001", "EMP-00002", "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery()));
        Assert.Equal(new[] { "EMP-00001", "EMP-00002", "EMP-00003", "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(IncludeDeleted: true)));
    }

    [Fact]
    public async Task Dates_are_utc_with_inclusive_to()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var day10 = DateOnly.FromDateTime(Day10);
        // 23:59 del día 'hasta' entra; 00:00 del día siguiente no.
        Assert.Equal(new[] { "EMP-00001" }, await NumbersAsync(w, new PickBatchQuery(From: day10, To: day10)));
        Assert.Equal(new[] { "EMP-00002" }, await NumbersAsync(w, new PickBatchQuery(From: day10.AddDays(1))));
        Assert.Equal(new[] { "EMP-00001", "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(To: day10)));
    }

    [Fact]
    public async Task Status_is_normalized_and_product_filter_uses_lines()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        Assert.Equal(new[] { "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(Status: new[] { " packed " })));
        Assert.Equal(new[] { "EMP-00001", "EMP-00002", "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(Status: new[] { "collected", " PACKED" })));
        Assert.Equal(new[] { "EMP-00002" }, await NumbersAsync(w, new PickBatchQuery(ProductPublicIds: new[] { w.Beta.PublicId })));
        Assert.Equal(new[] { "EMP-00001", "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(ProductPublicIds: new[] { w.Alfa.PublicId })));
    }

    [Fact]
    public async Task Search_applies_after_the_structural_filters()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        // La búsqueda encuentra SKU-ALFA en tres lotes, pero el filtro de producto BETA ya los excluyó.
        Assert.Empty(await NumbersAsync(w, new PickBatchQuery(ProductPublicIds: new[] { w.Beta.PublicId }, Search: "ALFA")));
        Assert.Equal(new[] { "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(Status: new[] { "PACKED" }, Search: "ALFA")));
        Assert.Equal(new[] { "EMP-00004" }, await NumbersAsync(w, new PickBatchQuery(Search: "2026-000555")));
        Assert.Equal(new[] { "EMP-00001" }, await NumbersAsync(w, new PickBatchQuery(Status: new[] { "COLLECTED" }, Search: "ALFA")));
    }
    private static Task<WmsFixture> CollectFixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
        sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(),
        sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!)));

    [Fact]
    public async Task Collecting_several_products_uses_fefo_for_lots_ships_the_scanned_serial_and_refs_every_issue()
    {
        // Maestro L781/L783 (dos productos a la vez, el stock baja exactamente), D14 (FEFO por lote) y L328 (serie despachada).
        await using var f = await CollectFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var pn = await f.AddProductAsync("PN", purchaseCost: 1.5m);
        var pl = await f.AddProductAsync("PL", tracking: TrackingTypes.Lot);
        var ps = await f.AddProductAsync("PS", tracking: TrackingTypes.Serial);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var late = await f.AddLotAsync(pl, "L-TARDE", today.AddDays(60));    // creado primero
        var early = await f.AddLotAsync(pl, "L-TEMPRANO", today.AddDays(10));
        InventoryPosting In(Product p, decimal q, int? lot = null, string? serial = null)
            => new(InventoryTxnTypes.Receipt, p.ProductId, q, LotId: lot, SerialNumber: serial, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId);
        await f.PostAsync(In(pn, 5m), In(pl, 2m, late.LotId), In(pl, 2m, early.LotId), In(ps, 1m, serial: "S1"), In(ps, 1m, serial: "S2"));
        var before = (await f.TransactionsAsync()).Count;

        var dto = await f.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(w.PublicId, new[]
        {
            new PickBatchLineRequest(pn.PublicId, 2m),
            new PickBatchLineRequest(pl.PublicId, 1m),
            new PickBatchLineRequest(ps.PublicId, null, SerialNumbers: new[] { "S2" }),
        }), default);

        Assert.Equal(PickBatchStatuses.Collected, dto.StatusCode);
        Assert.StartsWith("EMP-", dto.Number);
        Assert.Equal(early.LotId, dto.Lines.Single(l => l.Sku == "PL").LotId);          // FEFO: vence primero
        Assert.Equal(1.5m, dto.Lines.Single(l => l.Sku == "PN").UnitCost);             // costo congelado (D35)
        Assert.Equal("S2", dto.Lines.Single(l => l.Sku == "PS").SerialNumber);

        var issues = (await f.TransactionsAsync()).Skip(before).ToList();
        Assert.Equal(3, issues.Count);
        Assert.All(issues, t =>
        {
            Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Issue), t.TxnTypeLookupId);
            Assert.True(t.Quantity < 0m);
            Assert.Equal(f.LookupId(LookupDomains.EntityType, EntityTypes.PickBatch), t.RefEntityLookupId);
            Assert.Equal(dto.Id, t.RefId);                                             // Ref desde el INSERT (D48)
        });
        Assert.Equal(3m, await f.OnHandAsync(pn.ProductId, bin.WarehouseBinId));
        Assert.Equal(1m, await f.OnHandAsync(pl.ProductId, bin.WarehouseBinId, early.LotId));
        Assert.Equal(2m, await f.OnHandAsync(pl.ProductId, bin.WarehouseBinId, late.LotId));
        Assert.Equal(SerialStatuses.Shipped, f.StatusCodeOf((await f.SerialAsync(ps.ProductId, "S2")).StatusCodeId!.Value));
        Assert.Equal(SerialStatuses.Available, f.StatusCodeOf((await f.SerialAsync(ps.ProductId, "S1")).StatusCodeId!.Value));
    }

    [Fact]
    public async Task Collecting_products_of_two_owners_is_400_without_effects()
    {
        // D15: una recolección solo lleva productos de UN dueño.
        await using var f = await CollectFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var client = await f.AddClientAsync("CB");
        var pn = await f.AddProductAsync("PN");
        var p3 = await f.AddProductAsync("P3", ownerClientId: client.ClientId);
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, pn.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p3.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(w.PublicId, new[]
        {
            new PickBatchLineRequest(pn.PublicId, 1m), new PickBatchLineRequest(p3.PublicId, 1m),
        }), default));
        Assert.Equal(PickBatchRules.SingleOwner, Assert.Single(ex.Errors!["lines"]));
        Assert.Equal("Una recolección solo puede tener productos de un mismo dueño.", PickBatchRules.SingleOwner);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<PickBatch>().AsNoTracking().ToListAsync());
        Assert.Equal(2, (await f.TransactionsAsync()).Count);
        Assert.Equal(5m, await f.OnHandAsync(pn.ProductId, bin.WarehouseBinId));
    }

    /// <summary>Lote activo con una línea de 1 unidad de p desde bin (ISSUE ficticio 1), en el estatus y con la orden dados.</summary>
    private static async Task<PickBatch> AddBatchAsync(WmsFixture f, Warehouse w, WarehouseBin bin, Product p, string number, string status,
        int? orderId = null)
    {
        var batch = new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = number,
            StatusCodeId = f.StatusId(StatusDomains.PickBatchStatus, status), TransportOrderId = orderId, CollectedAtUtc = DateTime.UtcNow, IsActive = true,
        };
        f.Db.Set<PickBatch>().Add(batch);
        await f.Db.SaveChangesAsync();
        f.Db.Set<PickBatchLine>().Add(new PickBatchLine { PickBatchId = batch.PickBatchId, ProductId = p.ProductId, FromBinId = bin.WarehouseBinId, Quantity = 1m, IssueTxnId = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return batch;
    }

    private static async Task AssertUntouchedAsync(WmsFixture f, PickBatch batch, string status)
    {
        f.Db.ChangeTracker.Clear();
        var after = await f.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PickBatchId == batch.PickBatchId);
        Assert.True(after.IsActive);
        Assert.Equal(status, f.StatusCodeOf(after.StatusCodeId));
        Assert.All(await f.Db.Set<PickBatchLine>().AsNoTracking().Where(l => l.PickBatchId == batch.PickBatchId).ToListAsync(),
            l => Assert.Null(l.ReversalTxnId));
        Assert.Empty(await f.TransactionsAsync());
    }

    [Fact]
    public async Task Deleting_a_packed_batch_whose_order_advanced_is_422_and_changes_nothing()
    {
        // Bitácora L780/L783: un lote empacado solo se elimina si su orden sigue en la etapa inicial; si avanzó, 422 y
        // el lote y la orden quedan intactos (sin reversa).
        await using var f = await CollectFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PN");
        var client = await f.AddClientAsync("C1");
        var order = await f.AddOrderAsync(client.ClientId, "2026-000778", status: OrderStatuses.Confirmed, packBatchNumber: "EMP-00010");
        var batch = await AddBatchAsync(f, w, bin, p, "EMP-00010", PickBatchStatuses.Packed, order.TransportOrderId);
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick, PermissionCatalog.OrdersCancel);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() =>
            f.Get<PickBatchService>().DeleteAsync(batch.PublicId, new PickBatchDeleteRequest(), default));
        // La etiqueta del estatus es la del catálogo (el fixture siembra {"es":"CONFIRMED"}).
        Assert.Equal(PickBatchRules.DeleteBlocked("EMP-00010", "CONFIRMED"), ex.Message);
        await AssertUntouchedAsync(f, batch, PickBatchStatuses.Packed);
        Assert.True((await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == order.TransportOrderId)).IsActive);
    }

    [Fact]
    public async Task Deleting_a_batch_whose_original_bin_is_inactive_is_422_and_changes_nothing()
    {
        // D13: la reversa va a la posición original; si está inactiva, 422 sin efectos.
        await using var f = await CollectFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-02", isActive: false);
        var p = await f.AddProductAsync("PN");
        var batch = await AddBatchAsync(f, w, bin, p, "EMP-00011", PickBatchStatuses.Collected);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() =>
            f.Get<PickBatchService>().DeleteAsync(batch.PublicId, new PickBatchDeleteRequest(), default));
        Assert.Equal(PickBatchRules.ReversalBinInactive("P-02"), ex.Message);
        await AssertUntouchedAsync(f, batch, PickBatchStatuses.Collected);
    }

    [Fact]
    public async Task Collecting_an_inactive_product_is_422_without_effects()
    {
        // Maestro L326: un producto inactivo no se recolecta (la validación va antes de asignar existencias).
        await using var f = await CollectFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var pd = await f.AddProductAsync("PD", isActive: false);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<PickBatchService>().CollectAsync(
            new PickBatchCreateRequest(w.PublicId, new[] { new PickBatchLineRequest(pd.PublicId, 1m) }), default));
        Assert.Equal(PickBatchRules.ProductInactive("PD"), ex.Message);
        Assert.Equal("El producto PD está inactivo; no se puede recolectar.", ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<PickBatch>().AsNoTracking().ToListAsync());
        Assert.Empty(await f.TransactionsAsync());
    }
}

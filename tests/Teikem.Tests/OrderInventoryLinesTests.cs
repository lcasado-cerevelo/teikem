using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P7, D45) — IOrderInventoryLines en InMemory: solo recolecciones PACKED activas y líneas sin reversa; costo
/// congelado de la línea (D35) y precio de venta VIGENTE del producto; COLLECTED, CANCELLED y líneas revertidas fuera; una
/// recolección de otro tenant no aparece (filtro global de tenant sobre PickBatch y Product).
/// </summary>
public class OrderInventoryLinesTests
{
    private const int TenantId = 1;
    private const int OtherTenantId = 2;

    private const int OrderPacked = 10;
    private const int OrderCollected = 11;
    private const int OrderCancelled = 12;
    private const int OrderForeign = 13;
    private const int OrderPartlyReversed = 14;

    private sealed class World
    {
        public required TeikemDbContext Db { get; init; }
        public required Dictionary<string, int> Status { get; init; }
        public int Uom { get; init; }
        public int Lot { get; init; }
        public int Serial { get; init; }
        public int Stage { get; init; }
    }

    private static async Task<World> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("order-inventory-lines-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);

        var stage = new LookupCode { LookupCodeId = 1, Entity = LookupDomains.StageKind, InternalCode = StageKinds.Pipeline, LabelJson = "{\"es\":\"x\"}", IsActive = true };
        var uom = new LookupCode { LookupCodeId = 2, Entity = LookupDomains.UnitOfMeasure, InternalCode = "UN", LabelJson = "{\"es\":\"UN\"}", IsActive = true };
        var lot = new LookupCode { LookupCodeId = 3, Entity = LookupDomains.TrackingType, InternalCode = TrackingTypes.Lot, LabelJson = "{\"es\":\"LOT\"}", IsActive = true };
        var serial = new LookupCode { LookupCodeId = 4, Entity = LookupDomains.TrackingType, InternalCode = TrackingTypes.Serial, LabelJson = "{\"es\":\"SERIAL\"}", IsActive = true };
        db.LookupCodes.AddRange(stage, uom, lot, serial);

        var status = new Dictionary<string, int>();
        var sid = 100;
        foreach (var code in new[] { PickBatchStatuses.Collected, PickBatchStatuses.Packed, PickBatchStatuses.Cancelled })
        {
            db.StatusCodes.Add(new StatusCode
            {
                StatusCodeId = sid, Entity = StatusDomains.PickBatchStatus, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
                StageKindLookupId = stage.LookupCodeId, SortOrder = sid - 99, IsInitial = code == PickBatchStatuses.Collected, IsActive = true,
            });
            status[code] = sid++;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new World { Db = db, Status = status, Uom = uom.LookupCodeId, Lot = lot.LookupCodeId, Serial = serial.LookupCodeId, Stage = stage.LookupCodeId };
    }

    private static async Task<T> SaveAsync<T>(TeikemDbContext db, T entity) where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return entity;
    }

    private static Task<Product> ProductAsync(World w, string sku, int tracking, decimal? cost, decimal? price, int tenantId = TenantId)
        => SaveAsync(w.Db, new Product
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = sku, Name = "Producto " + sku, BaseUomLookupId = w.Uom,
            TrackingTypeLookupId = tracking, PurchaseCost = cost, SalePrice = price, IsActive = true,
        });

    private static Task<PickBatch> BatchAsync(World w, string number, string status, int? orderId, bool active = true, int tenantId = TenantId)
        => SaveAsync(w.Db, new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, WarehouseId = 1, Number = number, StatusCodeId = w.Status[status],
            TransportOrderId = orderId, ClientInvoiceNumber = orderId is null ? null : "FAC-" + orderId,
            CollectedAtUtc = DateTime.UtcNow, CollectedBy = 1, IsActive = active,
        });

    private static Task<PickBatchLine> LineAsync(World w, PickBatch b, Product p, decimal qty, decimal? unitCost, long issueTxnId,
        int? lotId = null, int? serialId = null, long? reversalTxnId = null)
        => SaveAsync(w.Db, new PickBatchLine
        {
            PickBatchId = b.PickBatchId, ProductId = p.ProductId, LotId = lotId, SerialId = serialId, FromBinId = 1, Quantity = qty,
            UnitCost = unitCost, IssueTxnId = issueTxnId, ReversalTxnId = reversalTxnId,
        });

    [Fact]
    public async Task Packed_batch_returns_lines_with_frozen_cost_and_current_price()
    {
        var w = await CreateAsync();
        var glucose = await ProductAsync(w, "GLU-100", w.Lot, cost: 2.5m, price: 4m);
        var meter = await ProductAsync(w, "MED-1", w.Serial, cost: 30m, price: 55m);
        var lot = await SaveAsync(w.Db, new InventoryLot { ProductId = glucose.ProductId, LotNumber = "L-26A", IsActive = true });
        var sn = await SaveAsync(w.Db, new InventorySerial { ProductId = meter.ProductId, SerialNumber = "SN-0001" });

        var batch = await BatchAsync(w, "EMP-00001", PickBatchStatuses.Packed, OrderPacked);
        await LineAsync(w, batch, glucose, 3m, 2.5m, 1001, lotId: lot.LotId);
        await LineAsync(w, batch, meter, 1m, 30m, 1002, serialId: sn.SerialId);

        // El costo y el precio del producto cambian DESPUÉS de empacar: el costo de la línea sigue congelado (D35),
        // el precio de venta es el vigente.
        var tracked = await w.Db.Set<Product>().FirstAsync(p => p.ProductId == glucose.ProductId);
        tracked.PurchaseCost = 9m;
        tracked.SalePrice = 4.75m;
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();

        var provider = new OrderInventoryLinesProvider(w.Db);
        var result = await provider.GetAsync(new[] { OrderPacked }, default);

        var lines = Assert.Single(result).Value;
        Assert.Equal(2, lines.Count);
        var g = lines.Single(l => l.Sku == "GLU-100");
        Assert.Equal(OrderPacked, g.TransportOrderId);
        Assert.Equal(batch.PickBatchId, g.PickBatchId);
        Assert.Equal("EMP-00001", g.PickBatchNumber);
        Assert.Equal(glucose.PublicId, g.ProductPublicId);
        Assert.Equal("L-26A", g.LotNumber);
        Assert.Null(g.SerialNumber);
        Assert.Equal(3m, g.Quantity);
        Assert.Equal(2.5m, g.UnitCost);
        Assert.Equal(4.75m, g.SalePrice);

        var m = lines.Single(l => l.Sku == "MED-1");
        Assert.Equal("SN-0001", m.SerialNumber);
        Assert.Equal(sn.SerialId, m.SerialId);
        Assert.Equal(1m, m.Quantity);
        Assert.Equal(55m, m.SalePrice);
    }

    [Fact]
    public async Task Collected_cancelled_reversed_and_foreign_are_excluded()
    {
        var w = await CreateAsync();
        var p = await ProductAsync(w, "GLU-100", w.Lot, cost: 2.5m, price: 4m);
        var foreignProduct = await ProductAsync(w, "GLU-100", w.Lot, cost: 2.5m, price: 4m, tenantId: OtherTenantId);

        // COLLECTED ligada (hipotéticamente) a una orden: el filtro de estatus la excluye.
        var collected = await BatchAsync(w, "EMP-00002", PickBatchStatuses.Collected, OrderCollected);
        await LineAsync(w, collected, p, 1m, 2.5m, 2001);

        // Eliminada: CANCELLED, inactiva y con su línea revertida.
        var cancelled = await BatchAsync(w, "EMP-00003", PickBatchStatuses.Cancelled, OrderCancelled, active: false);
        await LineAsync(w, cancelled, p, 1m, 2.5m, 3001, reversalTxnId: 3002);

        // PACKED de otro tenant con el mismo id de orden que pedimos.
        var foreign = await BatchAsync(w, "EMP-00001", PickBatchStatuses.Packed, OrderForeign, tenantId: OtherTenantId);
        await LineAsync(w, foreign, foreignProduct, 5m, 2.5m, 4001);

        // PACKED con una línea revertida y otra vigente: solo la vigente.
        var partly = await BatchAsync(w, "EMP-00004", PickBatchStatuses.Packed, OrderPartlyReversed);
        await LineAsync(w, partly, p, 2m, 2.5m, 5001, reversalTxnId: 5002);
        var kept = await LineAsync(w, partly, p, 4m, 2.5m, 5003);

        var provider = new OrderInventoryLinesProvider(w.Db);
        var result = await provider.GetAsync(new[] { OrderCollected, OrderCancelled, OrderForeign, OrderPartlyReversed }, default);

        Assert.False(result.ContainsKey(OrderCollected));
        Assert.False(result.ContainsKey(OrderCancelled));
        Assert.False(result.ContainsKey(OrderForeign));
        var only = Assert.Single(result[OrderPartlyReversed]);
        Assert.Equal(4m, only.Quantity);
        Assert.Equal(kept.ProductId, only.ProductId);
    }

    [Fact]
    public async Task Foreign_order_alone_and_empty_input_return_empty()
    {
        var w = await CreateAsync();
        var foreignProduct = await ProductAsync(w, "X", w.Lot, cost: 1m, price: 2m, tenantId: OtherTenantId);
        var foreign = await BatchAsync(w, "EMP-00001", PickBatchStatuses.Packed, OrderForeign, tenantId: OtherTenantId);
        await LineAsync(w, foreign, foreignProduct, 1m, 1m, 1);

        var provider = new OrderInventoryLinesProvider(w.Db);
        Assert.Empty(await provider.GetAsync(new[] { OrderForeign }, default));
        Assert.Empty(await provider.GetAsync(Array.Empty<int>(), default));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P8) — costura IPurchaseOrderReceiving en InMemory (al estilo de TripServiceFixture): qué PO se puede recibir,
/// qué líneas quedan pendientes (ordenado − recibido − resuelto) y cómo avanza la PO a PARTIAL/RECEIVED con historial.
/// </summary>
public class PurchaseOrderReceivingTests
{
    [Fact]
    public async Task Sent_po_returns_pending_lines_net_of_received_and_resolved()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Sent, (10m, 3m, 12.5m), (5m, 5m, 2m));
        f.AddResolution(po.Id, po.LineIds[0], 1m);
        await f.SaveAsync();

        var result = await f.Receiving.LockForReceiptAsync(po.PublicId, default);

        Assert.Equal(po.Id, result.PurchaseOrderId);
        Assert.Equal(po.Number, result.Number);
        Assert.Equal(PurchasingFixture.WarehouseId, result.WarehouseId);
        var line = Assert.Single(result.Lines);
        Assert.Equal(po.LineIds[0], line.PurchaseOrderLineId);
        Assert.Equal(PurchasingFixture.ProductA, line.ProductId);
        Assert.Equal(6m, line.QtyPending);
        Assert.Equal(12.5m, line.UnitCost);
    }

    [Theory]
    [InlineData(PurchaseOrderStatuses.Draft)]
    [InlineData(PurchaseOrderStatuses.Cancelled)]
    [InlineData(PurchaseOrderStatuses.Received)]
    public async Task Po_not_sent_or_partial_is_not_receivable(string status)
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(status, (10m, 0m, 1m));
        await f.SaveAsync();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Receiving.LockForReceiptAsync(po.PublicId, default));
        Assert.Equal("La orden de compra debe estar enviada o recibida parcial para recibir contra ella.", ex.Message);
        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task Po_without_pending_is_rejected_with_nothing_pending()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Partial, (10m, 8m, 1m));
        f.AddResolution(po.Id, po.LineIds[0], 2m);
        await f.SaveAsync();

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Receiving.LockForReceiptAsync(po.PublicId, default));
        Assert.Equal(PurchaseStatusRules.NothingPending, ex.Message);
    }

    [Fact]
    public async Task Po_of_another_tenant_is_not_found()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Sent, (10m, 0m, 1m));
        f.AddPo(PurchaseOrderStatuses.Sent, PurchasingFixture.OtherTenantId, (10m, 0m, 1m));
        await f.SaveAsync();
        var other = f.Pos.Last();

        await Assert.ThrowsAsync<NotFoundException>(() => f.Receiving.LockForReceiptAsync(other.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Receiving.LockForReceiptAsync(Guid.NewGuid(), default));
        Assert.NotNull(await f.Receiving.LockForReceiptAsync(po.PublicId, default));
    }

    [Fact]
    public async Task Partial_receipt_moves_sent_to_partial_with_history()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Sent, (10m, 0m, 12.5m), (5m, 0m, 2.5m));
        await f.SaveAsync();

        await f.Receiving.ApplyReceiptAsync(po.Id, new[] { new PurchaseOrderReceiptQty(po.LineIds[0], 4m) }, default);
        f.Db.ChangeTracker.Clear();

        var lines = await f.Db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == po.Id).OrderBy(l => l.PurchaseOrderLineId).ToListAsync();
        Assert.Equal(4m, lines[0].QtyReceived);
        Assert.Equal(0m, lines[1].QtyReceived);
        Assert.Equal(PurchaseOrderStatuses.Partial, await f.StatusOfAsync(po.Id));
        Assert.Equal(new[] { PurchaseOrderStatuses.Partial }, await f.HistoryAsync(po.Id));
    }

    [Fact]
    public async Task Complete_receipt_moves_sent_to_received_stepwise()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Sent, (10m, 0m, 12.5m), (5m, 0m, 2.5m));
        await f.SaveAsync();

        await f.Receiving.ApplyReceiptAsync(po.Id, new[]
        {
            new PurchaseOrderReceiptQty(po.LineIds[0], 10m),
            new PurchaseOrderReceiptQty(po.LineIds[1], 6m),
        }, default);
        f.Db.ChangeTracker.Clear();

        Assert.Equal(PurchaseOrderStatuses.Received, await f.StatusOfAsync(po.Id));
        Assert.Equal(new[] { PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received }, await f.HistoryAsync(po.Id));
    }

    [Fact]
    public async Task Partial_po_completed_by_second_receipt_and_resolutions()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var po = f.AddPo(PurchaseOrderStatuses.Partial, (10m, 6m, 1m), (5m, 3m, 1m));
        f.AddResolution(po.Id, po.LineIds[1], 2m);
        await f.SaveAsync();

        await f.Receiving.ApplyReceiptAsync(po.Id, new[] { new PurchaseOrderReceiptQty(po.LineIds[0], 4m) }, default);
        f.Db.ChangeTracker.Clear();

        Assert.Equal(PurchaseOrderStatuses.Received, await f.StatusOfAsync(po.Id));
        Assert.Equal(new[] { PurchaseOrderStatuses.Received }, await f.HistoryAsync(po.Id));
    }

    [Fact]
    public async Task Apply_rejects_cancelled_po_and_foreign_lines()
    {
        await using var f = await PurchasingFixture.CreateAsync();
        var cancelled = f.AddPo(PurchaseOrderStatuses.Cancelled, (10m, 0m, 1m));
        var sent = f.AddPo(PurchaseOrderStatuses.Sent, (10m, 0m, 1m));
        await f.SaveAsync();

        await Assert.ThrowsAsync<StatusRuleException>(() => f.Receiving.ApplyReceiptAsync(cancelled.Id,
            new[] { new PurchaseOrderReceiptQty(cancelled.LineIds[0], 1m) }, default));
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => f.Receiving.ApplyReceiptAsync(sent.Id,
            new[] { new PurchaseOrderReceiptQty(cancelled.LineIds[0], 1m) }, default));
    }
}

// ====================================================================================== fixture

/// <summary>
/// Fixture InMemory de Compras: catálogos y estatus PurchaseOrderStatus como el seed (DRAFT, SENT, PARTIAL pipeline;
/// RECEIVED y CANCELLED terminales), un almacén, un proveedor y dos productos propios; órdenes armadas por prueba.
/// </summary>
internal sealed class PurchasingFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    public const int OtherTenantId = 2;
    public const int WarehouseId = 10;
    public const int SupplierId = 20;
    public const int ProductA = 30;
    public const int ProductB = 31;

    public sealed record PoRef(int Id, Guid PublicId, string Number, IReadOnlyList<int> LineIds);

    private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
    private int _nextPo = 100;
    private int _nextLine = 1000;
    private int _nextResolution = 5000;

    private PurchasingFixture(TeikemDbContext db, TenantContext tenant, TripTestLookups lookups, PurchaseOrderReceivingService receiving)
    {
        Db = db;
        Tenant = tenant;
        Lookups = lookups;
        Receiving = receiving;
    }

    public TeikemDbContext Db { get; }
    public TenantContext Tenant { get; }
    public TripTestLookups Lookups { get; }
    public PurchaseOrderReceivingService Receiving { get; }
    public List<PoRef> Pos { get; } = new();
    public int PoEntityTypeId { get; private set; }

    public static async Task<PurchasingFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("purchasing-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var permissions = new PermissionService(db, tenant, cache, lookups, new PurchasingNullSecurityEventWriter());
        var statuses = new StatusService(db, tenant, lookups, Array.Empty<IStatusTransitionEffect>(), permissions);
        var f = new PurchasingFixture(db, tenant, lookups, new PurchaseOrderReceivingService(db, statuses));
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
            return l;
        }
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        PoEntityTypeId = L(LookupDomains.EntityType, EntityTypes.PurchaseOrder).LookupCodeId;
        L(LookupDomains.EntityType, EntityTypes.Receipt);
        L(LookupDomains.Capability, Capabilities.EditPurchaseOrder);
        var tracking = L(LookupDomains.TrackingType, TrackingTypes.None);
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
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent, pipe, 2);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial, pipe, 3);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Received, term, 4);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, term, 5);
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Active, pipe, 1, true);

        Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", IsActive = true });
        Db.Tenants.Add(new Tenant { TenantId = OtherTenantId, Name = "Otro tenant", IsActive = true });
        Db.Set<Warehouse>().Add(new Warehouse
        {
            WarehouseId = WarehouseId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "W1", Name = "Almacén 1",
            CountryLookupId = country.LookupCodeId, StatusCodeId = StatusId(WarehouseStatuses.Active, StatusDomains.WarehouseStatus), IsActive = true,
        });
        Db.Set<Supplier>().Add(new Supplier { SupplierId = SupplierId, TenantId = TenantId, Name = "Proveedor Uno", IsActive = true });
        foreach (var (pid, sku) in new[] { (ProductA, "PA"), (ProductB, "PB") })
            Db.Set<Product>().Add(new Product
            {
                ProductId = pid, PublicId = Guid.NewGuid(), TenantId = TenantId, Sku = sku, Name = "Producto " + sku,
                BaseUomLookupId = uom.LookupCodeId, TrackingTypeLookupId = tracking.LookupCodeId, PurchaseCost = 1m, IsActive = true,
            });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public int StatusId(string code, string domain = StatusDomains.PurchaseOrderStatus) => _statusIds[domain + "|" + code];

    /// <summary>Orden con líneas (ordenado, recibido, costo) sobre los productos A y B en ese orden.</summary>
    public PoRef AddPo(string status, params (decimal Ordered, decimal Received, decimal Cost)[] lines)
        => AddPo(status, TenantId, lines);

    public PoRef AddPo(string status, int tenantId, params (decimal Ordered, decimal Received, decimal Cost)[] lines)
    {
        var poId = _nextPo++;
        var po = new PurchaseOrder
        {
            PurchaseOrderId = poId, PublicId = Guid.NewGuid(), TenantId = tenantId, SupplierId = SupplierId, WarehouseId = WarehouseId,
            Number = $"PO-{poId:00000}", OrderDate = Teikem.Infrastructure.Abstractions.TenantClock.Default.Today, StatusCodeId = StatusId(status),
            IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Set<PurchaseOrder>().Add(po);
        var lineIds = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            var lineId = _nextLine++;
            lineIds.Add(lineId);
            Db.Set<PurchaseOrderLine>().Add(new PurchaseOrderLine
            {
                PurchaseOrderLineId = lineId, PurchaseOrderId = poId, ProductId = i == 0 ? ProductA : ProductB,
                QtyOrdered = lines[i].Ordered, QtyReceived = lines[i].Received, UnitCost = lines[i].Cost,
            });
        }
        var r = new PoRef(poId, po.PublicId, po.Number, lineIds);
        Pos.Add(r);
        return r;
    }

    public void AddResolution(int poId, int lineId, decimal qty)
        => Db.Set<PurchaseOrderShortageResolution>().Add(new PurchaseOrderShortageResolution
        {
            PurchaseOrderShortageResolutionId = _nextResolution++, TenantId = TenantId, PurchaseOrderId = poId, PurchaseOrderLineId = lineId,
            ActionLookupId = 1, Quantity = qty, CreatedAtUtc = DateTime.UtcNow,
        });

    public async Task SaveAsync()
    {
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async Task<string> StatusOfAsync(int poId)
    {
        var statusId = await Db.Set<PurchaseOrder>().AsNoTracking().Where(p => p.PurchaseOrderId == poId).Select(p => p.StatusCodeId).FirstAsync();
        return _statusIds.First(kv => kv.Value == statusId).Key.Split('|')[1];
    }

    public async Task<string[]> HistoryAsync(int poId)
    {
        var ids = await Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == PoEntityTypeId && h.EntityId == poId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        return ids.Select(i => _statusIds.First(kv => kv.Value == i).Key.Split('|')[1]).ToArray();
    }

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}

internal sealed class PurchasingNullSecurityEventWriter : ISecurityEventWriter
{
    public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        => Task.CompletedTask;
}

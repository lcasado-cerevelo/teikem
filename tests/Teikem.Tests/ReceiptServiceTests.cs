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
        Assert.Equal(ReceiptStatuses.Open, created.Header.StatusCode);
        Assert.StartsWith("REC-", created.Header.Number);
        Assert.Equal(ReceiptOrigins.PurchaseOrder, created.Header.Origin);
        var line = Assert.Single(created.Lines);
        Assert.Equal(10m, line.ExpectedQty);
        Assert.Equal(10m, line.ReceivedQty);                     // R8: lo recibido arranca igual a lo esperado
        Assert.Equal(f.StagingBinId, line.StagingBinId);         // primera posición de una zona STAGING
        Assert.Equal(12.5m, line.UnitCost);                      // costo congelado de la línea de PO

        await receipts.UpdateLineAsync(created.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 8m), default);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, new ReceiptConfirmRequest(), default);

        Assert.Equal(ReceiptStatuses.Received, confirmed.Header.StatusCode);
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
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Open, pipe, 1, true);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Received, pipe, 2);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway, term, 3);
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
            ClientId = 7, PublicId = ClientPublicId, TenantId = TenantId, Code = "C1", Name = "Cliente Uno",
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

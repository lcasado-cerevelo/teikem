using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P3 — lecturas de inventario en InMemory: InventoryScope (dueño) en saldos, Kárdex, genealogía y rastro de serie
/// (otro dueño → 404 sin oráculo), Any ve todo, SignedQuantity con filtro de posición, categorías con subcategorías,
/// origen legible y usuario, y conciliación ledger ↔ saldo. Los saldos y movimientos se siembran directamente (la prueba
/// no escribe a través del ledger; eso lo cubren InventoryAdjustmentServiceTests e InventoryLedgerTests).
/// </summary>
public class InventoryReadServiceTests
{
    private const int ClientA = 50;
    private const int ClientB = 51;

    private sealed record World(InventoryServiceFixture F, Warehouse W1, WarehouseBin B1, WarehouseBin B2, Product PA, Product PB, Product POwn,
        InventoryLot LotA, ProductCategory Root, ProductCategory Child, TransportOrder Order, InventorySerial SerialB);

    /// <summary>
    /// PA (cliente A, categoría hija, lote L-A): RECEIPT +10 a B1, TRANSFER 4 B1→B2, ISSUE −3 desde B2 hacia la orden.
    /// PB (cliente B, serie SB-1): RECEIPT +1 a B1. POwn (propio): RECEIPT +2 a B1. Saldos consistentes con el ledger.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var f = await InventoryServiceFixture.CreateAsync();
        await f.AddClientAsync(ClientA, "Cliente A");
        await f.AddClientAsync(ClientB, "Cliente B");
        var root = await f.AddCategoryAsync("Medidores");
        var child = await f.AddCategoryAsync("Glucosa", root.ProductCategoryId);
        var w1 = await f.AddWarehouseAsync("ALM-01");
        var zone = await f.AddZoneAsync(w1, "PCK", "PICKING");
        var b1 = await f.AddBinAsync(zone, "A01-R01-N1-P01");
        var b2 = await f.AddBinAsync(zone, "A01-R01-N1-P02");
        var pa = await f.AddProductAsync("PA", "LOT", ClientA, child.ProductCategoryId, cost: 2.5m, price: 4m);
        var pb = await f.AddProductAsync("PB", "SERIAL", ClientB);
        var pown = await f.AddProductAsync("POWN", "NONE");
        var lotA = await f.AddLotAsync(pa, "L-A", new DateOnly(2027, 1, 31));
        var serialB = await f.AddSerialAsync(pb, "SB-1", w1, b1);
        var order = await f.AddOrderAsync(ClientA, "2026-000777", "Farmacia Central");

        var t0 = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        await f.AddTxnAsync("RECEIPT", pa, 10m, to: (w1, b1), lot: lotA, at: t0);
        await f.AddTxnAsync("TRANSFER", pa, 4m, from: (w1, b1), to: (w1, b2), lot: lotA, at: t0.AddHours(1));
        await f.AddTxnAsync("ISSUE", pa, -3m, from: (w1, b2), lot: lotA, at: t0.AddHours(2),
            refEntity: EntityTypes.TransportOrder, refId: order.TransportOrderId);
        await f.AddTxnAsync("RECEIPT", pb, 1m, to: (w1, b1), serial: serialB, at: t0.AddHours(3));
        await f.AddTxnAsync("RECEIPT", pown, 2m, to: (w1, b1), at: t0.AddDays(2));

        await f.AddBalanceAsync(pa, w1, b1, lotA, 6m);
        await f.AddBalanceAsync(pa, w1, b2, lotA, 1m);
        await f.AddBalanceAsync(pb, w1, b1, null, 1m);
        await f.AddBalanceAsync(pown, w1, b1, null, 2m, reserved: 0.5m);
        await f.AddSerialHistoryAsync(serialB, null, "AVAILABLE");
        return new World(f, w1, b1, b2, pa, pb, pown, lotA, root, child, order, serialB);
    }

    // ---------------------------------------------------------------- saldos

    [Fact]
    public async Task Balances_any_sees_all_and_owner_scope_only_its_products()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        var all = await reads.BalancesAsync(new BalanceQuery(), InventoryScope.Any, default);
        Assert.Equal(4, all.Total);
        Assert.Equal(10m, all.TotalOnHand);
        Assert.Equal(9.5m, all.TotalAvailable);

        var onlyA = await reads.BalancesAsync(new BalanceQuery(), new InventoryScope(ClientA), default);
        Assert.Equal(2, onlyA.Total);
        Assert.All(onlyA.Items, b => Assert.Equal("PA", b.Sku));
        Assert.All(onlyA.Items, b => Assert.Equal("Cliente A", b.OwnerName));
        Assert.All(onlyA.Items, b => Assert.False(b.IsOwn));

        var own = all.Items.Single(b => b.Sku == "POWN");
        Assert.True(own.IsOwn);
        Assert.Equal("Propio", own.OwnerName);
        Assert.Equal(1.5m, own.QtyAvailable);   // en mano − reservado, calculado en código

        var pa = all.Items.Single(b => b.Sku == "PA" && b.BinId == w.B1.WarehouseBinId);
        Assert.Equal("L-A", pa.LotNumber);
        Assert.Equal(15m, pa.CostValue);         // 6 × 2.5
        Assert.Equal(24m, pa.SaleValue);         // 6 × 4
        Assert.Equal("PCK", pa.ZoneCode);
        Assert.Equal("PICKING", pa.ZoneTypeCode);
        Assert.Equal("ALM-01", pa.WarehouseCode);
    }

    [Fact]
    public async Task Balances_category_filter_includes_subcategories()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        var byRoot = await reads.BalancesAsync(new BalanceQuery(CategoryIds: new[] { w.Root.ProductCategoryId }), InventoryScope.Any, default);
        Assert.Equal(2, byRoot.Total);
        Assert.All(byRoot.Items, b => Assert.Equal("Glucosa", b.CategoryName));
    }

    [Fact]
    public async Task Balances_filters_bin_lot_and_search()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();
        Assert.Equal(1, (await reads.BalancesAsync(new BalanceQuery(BinIds: new[] { w.B2.WarehouseBinId }), InventoryScope.Any, default)).Total);
        Assert.Equal(2, (await reads.BalancesAsync(new BalanceQuery(LotNumber: "L-A"), InventoryScope.Any, default)).Total);
        Assert.Equal(1, (await reads.BalancesAsync(new BalanceQuery(Search: "POWN"), InventoryScope.Any, default)).Total);
    }

    // ---------------------------------------------------------------- Kárdex

    [Fact]
    public async Task Kardex_owner_scope_and_order()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        var all = await reads.KardexAsync(new KardexQuery(), InventoryScope.Any, default);
        Assert.Equal(5, all.Total);
        Assert.Equal("POWN", all.Items[0].Sku);   // más reciente primero

        var onlyA = await reads.KardexAsync(new KardexQuery(), new InventoryScope(ClientA), default);
        Assert.Equal(3, onlyA.Total);
        Assert.All(onlyA.Items, r => Assert.Equal("PA", r.Sku));
    }

    [Fact]
    public async Task Kardex_quantity_is_ledger_signed_and_signed_quantity_follows_bin_filter()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        var noFilter = await reads.KardexAsync(new KardexQuery(ProductPublicIds: new[] { w.PA.PublicId }), InventoryScope.Any, default);
        var transfer = noFilter.Items.Single(r => r.TypeCode == "TRANSFER");
        Assert.Equal(4m, transfer.Quantity);
        Assert.Equal(0m, transfer.SignedQuantity);
        Assert.Equal("ALM-01/A01-R01-N1-P01 → ALM-01/A01-R01-N1-P02", transfer.Position);
        var issue = noFilter.Items.Single(r => r.TypeCode == "ISSUE");
        Assert.Equal(-3m, issue.Quantity);
        Assert.Equal(-3m, issue.SignedQuantity);
        Assert.Equal("Despacho", issue.Type);
        Assert.Equal("Orden 2026-000777", issue.RefLabel);
        Assert.Equal(EntityTypes.TransportOrder, issue.RefEntityCode);
        Assert.Equal("Ana Operadora", issue.UserName);

        var fromB1 = await reads.KardexAsync(new KardexQuery(BinIds: new[] { w.B1.WarehouseBinId },
            ProductPublicIds: new[] { w.PA.PublicId }), InventoryScope.Any, default);
        Assert.Equal(2, fromB1.Total);   // RECEIPT a B1 y TRANSFER desde B1; el ISSUE sale de B2
        Assert.Equal(-4m, fromB1.Items.Single(r => r.TypeCode == "TRANSFER").SignedQuantity);
        Assert.Equal(10m, fromB1.Items.Single(r => r.TypeCode == "RECEIPT").SignedQuantity);

        var toB2 = await reads.KardexAsync(new KardexQuery(BinIds: new[] { w.B2.WarehouseBinId }), InventoryScope.Any, default);
        Assert.Equal(4m, toB2.Items.Single(r => r.TypeCode == "TRANSFER").SignedQuantity);
        Assert.Equal(-3m, toB2.Items.Single(r => r.TypeCode == "ISSUE").SignedQuantity);
    }

    [Fact]
    public async Task Kardex_dates_types_category_and_unknown_type()
    {
        var w = await SeedAsync();
        var reads = w.F.Get<InventoryReadService>();

        // 'hasta' es inclusivo por día: el 2026-09-20 incluye todo ese día UTC.
        var day = await reads.KardexAsync(new KardexQuery(From: new DateOnly(2026, 9, 20), To: new DateOnly(2026, 9, 20)), InventoryScope.Any, default);
        Assert.Equal(4, day.Total);
        var issues = await reads.KardexAsync(new KardexQuery(Types: new[] { "issue" }), InventoryScope.Any, default);
        Assert.Equal(1, issues.Total);
        var byCategory = await reads.KardexAsync(new KardexQuery(CategoryIds: new[] { w.Root.ProductCategoryId }), InventoryScope.Any, default);
        Assert.Equal(3, byCategory.Total);
        var bySerial = await reads.KardexAsync(new KardexQuery(SerialNumber: "SB-1"), InventoryScope.Any, default);
        Assert.Equal("SB-1", Assert.Single(bySerial.Items).SerialNumber);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => reads.KardexAsync(new KardexQuery(Types: new[] { "FOO" }), InventoryScope.Any, default));
        Assert.Equal("Tipo de movimiento desconocido: 'FOO'.", ex.Message);
        var inverted = await Assert.ThrowsAsync<ValidationException>(() =>
            reads.KardexAsync(new KardexQuery(From: new DateOnly(2026, 9, 21), To: new DateOnly(2026, 9, 20)), InventoryScope.Any, default));
        Assert.Equal(KardexRules.RangeInverted, inverted.Message);
    }

    // ---------------------------------------------------------------- trazabilidad

    [Fact]
    public async Task Genealogy_totals_and_destination_with_owner_scope()
    {
        var w = await SeedAsync();
        var trace = w.F.Get<TraceabilityService>();

        var g = await trace.LotGenealogyAsync(w.LotA.LotId, new InventoryScope(ClientA), default);
        Assert.Equal("L-A", g.LotNumber);
        Assert.Equal(10m, g.QtyIn);
        Assert.Equal(3m, g.QtyOut);
        Assert.Equal(7m, g.QtyOnHand);
        Assert.Equal(3, g.Movements.Count);
        var dest = Assert.Single(g.Destinations);
        Assert.Equal(EntityTypes.TransportOrder, dest.RefEntityCode);
        Assert.Equal(w.Order.PublicId, dest.OrderPublicId);
        Assert.Equal("Cliente A", dest.ClientName);
        Assert.Equal("Farmacia Central", dest.ConsigneeName);
        Assert.Equal(3m, dest.Quantity);

        // Lote de otro dueño: 404 sin oráculo; Any lo ve.
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => trace.LotGenealogyAsync(w.LotA.LotId, new InventoryScope(ClientB), default));
        Assert.Equal("Lote no encontrado.", ex.Message);
        Assert.Equal(10m, (await trace.LotGenealogyAsync(w.LotA.LotId, InventoryScope.Any, default)).QtyIn);
    }

    [Fact]
    public async Task Genealogy_of_other_tenant_lot_is_404()
    {
        var w = await SeedAsync();
        var otherProduct = await w.F.AddProductAsync("PX", "LOT", tenantId: InventoryServiceFixture.OtherTenantId);
        var otherLot = await w.F.AddLotAsync(otherProduct, "L-X", null);
        var trace = w.F.Get<TraceabilityService>();
        await Assert.ThrowsAsync<NotFoundException>(() => trace.LotGenealogyAsync(otherLot.LotId, InventoryScope.Any, default));
    }

    [Fact]
    public async Task Serial_trace_with_scope_movements_and_history()
    {
        var w = await SeedAsync();
        var trace = w.F.Get<TraceabilityService>();

        var t = await trace.SerialTraceAsync(w.PB.PublicId, " SB-1 ", new InventoryScope(ClientB), default);
        Assert.Equal("SB-1", t.Serial.SerialNumber);
        Assert.Equal("ALM-01", t.Serial.WarehouseCode);
        Assert.Equal("A01-R01-N1-P01", t.Serial.BinCode);
        Assert.Equal("AVAILABLE", t.Serial.StatusCode);
        Assert.Equal(1m, Assert.Single(t.Movements).Quantity);
        Assert.Equal("AVAILABLE", Assert.Single(t.StatusHistory).ToCode);

        await Assert.ThrowsAsync<NotFoundException>(() => trace.SerialTraceAsync(w.PB.PublicId, "SB-1", new InventoryScope(ClientA), default));
        await Assert.ThrowsAsync<NotFoundException>(() => trace.SerialTraceAsync(w.PB.PublicId, "NOPE", InventoryScope.Any, default));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => trace.SerialTraceAsync(w.PB.PublicId, " ", InventoryScope.Any, default));
        Assert.Equal(TraceabilityService.SerialNumberRequired, ex.Message);
    }

    // ---------------------------------------------------------------- conciliación

    [Fact]
    public async Task Reconciliation_without_and_with_mismatch()
    {
        var w = await SeedAsync();
        var trace = w.F.Get<TraceabilityService>();

        var ok = await trace.ReconcileAsync(null, default);
        Assert.Empty(ok.Mismatches);
        Assert.Equal(4, ok.BalancesChecked);

        // Descuadre forzado: el saldo de POWN dice 5 y el ledger 2.
        var balance = await w.F.Db.Set<StockBalance>().SingleAsync(b => b.ProductId == w.POwn.ProductId);
        balance.QtyOnHand = 5m;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var bad = await trace.ReconcileAsync(w.POwn.PublicId, default);
        var row = Assert.Single(bad.Mismatches);
        Assert.Equal("POWN", row.Sku);
        Assert.Equal(2m, row.LedgerQty);
        Assert.Equal(5m, row.BalanceQty);
        Assert.Equal("A01-R01-N1-P01", row.BinCode);
        Assert.Single((await trace.ReconcileAsync(null, default)).Mismatches);
    }
}

/// <summary>
/// Lote 6 / P3 — fixture InMemory propia de las pruebas de servicio de inventario (al estilo de TripServiceFixture):
/// StatusService, InventoryLedger, InventoryReadService, InventoryAdjustmentService y TraceabilityService reales por DI.
/// Siembra los catálogos WMS con los literales de logistica-db-seed.sql y ofrece builders de almacén, zona, posición,
/// producto, lote, serie, saldo y movimiento. Con InMemory, InventoryQueries carga tracked sin bloqueo y
/// RunInTransactionAsync no abre transacción real: se prueba la orquestación, no el bloqueo de SQL Server (smoke).
/// </summary>
internal sealed class InventoryServiceFixture
{
    public const int TenantId = 1;
    public const int OtherTenantId = 2;
    public const int UserId = 1;

    private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);

    private InventoryServiceFixture(TeikemDbContext db, ServiceProvider services, TripTestLookups lookups)
    {
        Db = db;
        Services = services;
        Lookups = lookups;
    }

    public TeikemDbContext Db { get; }
    public ServiceProvider Services { get; }
    public TripTestLookups Lookups { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public int StatusId(string domain, string code) => _statusIds[domain + "|" + code];

    public static async Task<InventoryServiceFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = UserId };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("inventory-services-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<ILookupCache>(lookups);
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        // PermissionService solo lo usa GetHistoryAsync (no se usa aquí).
        services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), null!));
        services.AddSingleton<InventoryLedger>();
        services.AddSingleton<InventoryReadService>();
        services.AddSingleton<InventoryAdjustmentService>();
        services.AddSingleton<TraceabilityService>();
        var f = new InventoryServiceFixture(db, services.BuildServiceProvider(), lookups);
        await f.SeedCatalogsAsync();
        return f;
    }

    private async Task SeedCatalogsAsync()
    {
        var id = 1;
        LookupCode L(string entity, string code) => new()
        {
            LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true,
        };
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        var all = new List<LookupCode> { pipe, lat, term };
        foreach (var e in new[]
                 {
                     "WAREHOUSE", "WAREHOUSE_DOCK", "PRODUCT", "INVENTORY_SERIAL", "INVENTORY_TRANSACTION", "STOCK_BALANCE", "RECEIPT",
                     "PICK_BATCH", "CYCLE_COUNT", "PURCHASE_ORDER", "TRANSPORT_ORDER", "WAREHOUSE_TASK", "CROSSDOCK_ALLOCATION",
                     "CROSSDOCK_PLAN", "CLIENT",
                 })
            all.Add(L(LookupDomains.EntityType, e));
        foreach (var c in new[] { "RECEIPT", "ISSUE", "TRANSFER", "ADJUSTMENT", "CROSSDOCK" }) all.Add(L("InventoryTxnType", c));
        foreach (var c in new[] { "RECEIPT_VARIANCE", "COUNT_VARIANCE", "DAMAGE", "LOSS", "FOUND", "EXPIRED", "PO_SHORTAGE", "PICK_BATCH_REVERSAL", "OTHER" })
            all.Add(L("AdjustmentReason", c));
        foreach (var c in new[] { "NONE", "LOT", "SERIAL" }) all.Add(L("TrackingType", c));
        foreach (var c in new[] { "PICKING", "RESERVE", "REFRIGERATED", "QUARANTINE", "CROSSDOCK", "STAGING" }) all.Add(L("ZoneType", c));
        all.Add(L(LookupDomains.UnitOfMeasure, "UN"));
        all.Add(L(LookupDomains.Country, "PR"));
        all.Add(L(LookupDomains.StopType, StopTypes.Delivery));
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode
            {
                StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}",
                StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true,
            };
            Db.StatusCodes.Add(s);
            _statusIds[domain + "|" + code] = s.StatusCodeId;
        }
        S("WarehouseStatus", "ACTIVE", pipe, 1, true);
        S("WarehouseStatus", "INACTIVE", term, 2);
        S("SerialStatus", "AVAILABLE", pipe, 1, true);
        S("SerialStatus", "RESERVED", lat, 2);
        S("SerialStatus", "SHIPPED", lat, 3);
        S("SerialStatus", "SCRAPPED", term, 4);
        S(StatusDomains.OrderStatus, OrderStatuses.Draft, pipe, 1, true);

        Db.Users.Add(new ApplicationUser { Id = UserId, UserName = "ana", Email = "ana@teikem.test", FullName = "Ana Operadora" });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    private async Task<T> SaveAsync<T>(T entity) where T : class
    {
        Db.Add(entity);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return entity;
    }

    public int LookupId(string entity, string code) => Lookups.GetIdAsync(entity, code).GetAwaiter().GetResult();

    public Task<Client> AddClientAsync(int clientId, string name, int tenantId = TenantId) => SaveAsync(new Client
    {
        ClientId = clientId, PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "C" + clientId, Name = name,
        StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
    });

    public Task<ProductCategory> AddCategoryAsync(string name, int? parentId = null, int tenantId = TenantId)
        => SaveAsync(new ProductCategory { TenantId = tenantId, Name = name, ParentId = parentId, IsActive = true });

    public Task<Warehouse> AddWarehouseAsync(string code, int tenantId = TenantId) => SaveAsync(new Warehouse
    {
        PublicId = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = "Almacén " + code,
        CountryLookupId = LookupId(LookupDomains.Country, "PR"), StatusCodeId = StatusId("WarehouseStatus", "ACTIVE"), IsActive = true,
    });

    public Task<WarehouseZone> AddZoneAsync(Warehouse w, string code, string zoneType) => SaveAsync(new WarehouseZone
    {
        WarehouseId = w.WarehouseId, Code = code, Name = "Zona " + code, ZoneTypeLookupId = LookupId("ZoneType", zoneType), IsActive = true,
    });

    public Task<WarehouseBin> AddBinAsync(WarehouseZone z, string code) => SaveAsync(new WarehouseBin
    {
        WarehouseZoneId = z.WarehouseZoneId, WarehouseId = z.WarehouseId, Code = code, IsActive = true,
    });

    public Task<Product> AddProductAsync(string sku, string tracking, int? ownerClientId = null, int? categoryId = null,
        decimal? cost = null, decimal? price = null, int tenantId = TenantId) => SaveAsync(new Product
    {
        PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = ownerClientId, Sku = sku, Name = "Producto " + sku,
        ProductCategoryId = categoryId, BaseUomLookupId = LookupId(LookupDomains.UnitOfMeasure, "UN"),
        TrackingTypeLookupId = LookupId("TrackingType", tracking), PurchaseCost = cost, SalePrice = price, IsActive = true,
    });

    public Task<InventoryLot> AddLotAsync(Product p, string number, DateOnly? expiry) => SaveAsync(new InventoryLot
    {
        ProductId = p.ProductId, LotNumber = number, ExpiryDate = expiry, IsActive = true,
    });

    public Task<InventorySerial> AddSerialAsync(Product p, string number, Warehouse w, WarehouseBin b) => SaveAsync(new InventorySerial
    {
        ProductId = p.ProductId, SerialNumber = number, StatusCodeId = StatusId("SerialStatus", "AVAILABLE"),
        CurrentWarehouseId = w.WarehouseId, CurrentBinId = b.WarehouseBinId,
    });

    public Task<StockBalance> AddBalanceAsync(Product p, Warehouse w, WarehouseBin? b, InventoryLot? lot, decimal onHand, decimal reserved = 0m)
        => SaveAsync(new StockBalance
        {
            TenantId = p.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = b?.WarehouseBinId,
            LotId = lot?.LotId, QtyOnHand = onHand, QtyReserved = reserved, UpdatedAtUtc = DateTime.UtcNow,
        });

    /// <summary>Movimiento sembrado directamente (la cantidad va CON signo, como la guarda el ledger).</summary>
    public Task<InventoryTransaction> AddTxnAsync(string type, Product p, decimal signedQty, (Warehouse W, WarehouseBin B)? from = null,
        (Warehouse W, WarehouseBin B)? to = null, InventoryLot? lot = null, InventorySerial? serial = null, DateTime? at = null,
        string? refEntity = null, int? refId = null, string? reason = null)
        => SaveAsync(new InventoryTransaction
        {
            TenantId = p.TenantId, TxnTypeLookupId = LookupId("InventoryTxnType", type), ProductId = p.ProductId,
            LotId = lot?.LotId, SerialId = serial?.SerialId,
            FromWarehouseId = from?.W.WarehouseId, FromBinId = from?.B.WarehouseBinId,
            ToWarehouseId = to?.W.WarehouseId, ToBinId = to?.B.WarehouseBinId,
            Quantity = signedQty,
            RefEntityLookupId = refEntity is null ? null : LookupId(LookupDomains.EntityType, refEntity), RefId = refId,
            ReasonLookupId = reason is null ? null : LookupId("AdjustmentReason", reason),
            CreatedAtUtc = at ?? DateTime.UtcNow, CreatedBy = UserId,
        });

    /// <summary>Orden del cliente con una parada DELIVERY (el consignatario de la genealogía).</summary>
    public async Task<TransportOrder> AddOrderAsync(int clientId, string number, string consignee)
    {
        var order = await SaveAsync(new TransportOrder
        {
            PublicId = Guid.NewGuid(), TenantId = TenantId, ClientId = clientId, OrderNumber = number, PackBatchNumber = "EMP-00001",
            ServiceTypeLookupId = 1, StatusCodeId = StatusId(StatusDomains.OrderStatus, OrderStatuses.Draft), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await SaveAsync(new OrderStop
        {
            TransportOrderId = order.TransportOrderId, StopTypeLookupId = LookupId(LookupDomains.StopType, StopTypes.Delivery),
            Sequence = 2, SnapName = consignee, StatusCodeId = 1,
        });
        return order;
    }

    public Task<EntityStatusHistory> AddSerialHistoryAsync(InventorySerial s, string? from, string to) => SaveAsync(new EntityStatusHistory
    {
        TenantId = TenantId, EntityTypeLookupId = LookupId(LookupDomains.EntityType, "INVENTORY_SERIAL"), EntityId = s.SerialId,
        FromStatusCodeId = from is null ? null : StatusId("SerialStatus", from), ToStatusCodeId = StatusId("SerialStatus", to),
        ChangedAtUtc = DateTime.UtcNow, ChangedBy = UserId,
    });
}

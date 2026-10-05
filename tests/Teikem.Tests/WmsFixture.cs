using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P0 — fixture InMemory del WMS (al estilo de TripServiceFixture): catálogos y estatus WMS como
/// logistica-db-seed.sql (StageKind, SerialStatus con RESERVED/SHIPPED laterales y SCRAPPED terminal, WarehouseTaskStatus con
/// CANCELLED, entradas laterales de WAREHOUSE_TASK), dos tenants y builders de almacén, zonas, posiciones, clientes y
/// productos. StatusService, InventoryLedger, WarehouseTaskWriter y PutawaySuggester son los reales, resueltos por DI.
/// Con InMemory, InventoryQueries carga tracked sin bloqueo y RunInTransactionAsync no abre transacción real: se prueba la
/// lógica del ledger (signo, reservas, series, validaciones), no el bloqueo de SQL Server (eso lo cubre el smoke).
/// El tenant activo se cambia con AsTenant (el TenantId sale del contexto, nunca del request).
/// Pruebas de servicio: CreateAsync(configure) registra servicios adicionales (WarehouseTaskService, CrossDockService…);
/// PermissionService y ModuleService son los reales, con el usuario como administrador de plataforma (todo permiso) y los
/// módulos WMS_LOTSERIAL, PURCHASING y CROSSDOCK encendidos (SetModules los cambia). Los catálogos incluyen todos los
/// dominios de estatus del lote, como logistica-db-seed.sql.
/// </summary>
internal sealed class WmsFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    public const int OtherTenantId = 2;

    private readonly Dictionary<string, int> _lookupIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 1000;

    private WmsFixture(TeikemDbContext db, TenantContext tenant, ServiceProvider services, TripTestLookups lookups)
    {
        Db = db;
        Tenant = tenant;
        Services = services;
        Lookups = lookups;
    }

    public TeikemDbContext Db { get; }
    public TenantContext Tenant { get; }
    public ServiceProvider Services { get; }
    public TripTestLookups Lookups { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public InventoryLedger Ledger => Get<InventoryLedger>();
    /// <summary>Lote 14 (P2): bandeja de cambios de inventario que anota InventoryLedger.PostAsync.</summary>
    public InventoryChangeSink Changes => Get<InventoryChangeSink>();
    public WarehouseTaskWriter TaskWriter => Get<WarehouseTaskWriter>();

    public int LookupId(string domain, string code) => _lookupIds[domain + "|" + code];
    public int StatusId(string domain, string code) => _statusIds[domain + "|" + code];
    public string StatusCodeOf(int statusCodeId) => _statusIds.Single(kv => kv.Value == statusCodeId).Key.Split('|')[1];

    public static async Task<WmsFixture> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("wms-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(tenant);
        services.AddSingleton<ILookupCache>(lookups);
        var cache = new MemoryCache(new MemoryCacheOptions());
        services.AddSingleton<IMemoryCache>(cache);
        services.AddSingleton<ISecurityEventWriter, ReceivingNullSecurityEventWriter>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton<ModuleService>();
        services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), sp.GetRequiredService<PermissionService>()));
        services.AddSingleton<INumberSequenceService, InMemoryNumberSequence>();
        // Lote 14 (P2): la bandeja de cambios de la petición; InMemory no dispara el interceptor de transacción, así que lo anotado
        // se queda aquí para que las pruebas lo lean (Changes).
        services.AddSingleton<InventoryChangeSink>();
        services.AddSingleton<IInventoryChangeSink>(sp => sp.GetRequiredService<InventoryChangeSink>());
        services.AddSingleton<InventoryLedger>();
        services.AddSingleton<WarehouseTaskWriter>();
        services.AddSingleton<PutawaySuggester>();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        var f = new WmsFixture(db, tenant, provider, lookups) { _cache = cache };
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing, ModuleKeys.CrossDock);
        await f.SeedCatalogsAsync();
        return f;
    }

    private MemoryCache? _cache;

    /// <summary>Módulos encendidos del tenant activo (ModuleService lee primero la caché).</summary>
    public void SetModules(params string[] moduleKeys)
        => _cache!.Set($"modules:{Tenant.TenantId}", new HashSet<string>(moduleKeys, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// El usuario deja de ser administrador de plataforma y queda SOLO con los permisos dados (PermissionService lee primero
    /// la caché 'perms:{usuario}:{tenant}'). Para probar las negativas de los permisos que exige el servicio.
    /// </summary>
    public void SetPermissions(params string[] codes)
    {
        Tenant.IsPlatformAdmin = false;
        _cache!.Set($"perms:{Tenant.UserId}:{Tenant.TenantId}", new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Ejecuta el bloque como otro tenant (el filtro global cambia con el contexto).</summary>
    public IDisposable AsTenant(int tenantId) => Tenant.As(tenantId);

    // ================================================================ catálogos

    private async Task SeedCatalogsAsync()
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
                     EntityTypes.Warehouse, EntityTypes.WarehouseDock, EntityTypes.Product, EntityTypes.Receipt, EntityTypes.Asn,
                     EntityTypes.InventorySerial, EntityTypes.WarehouseTask, EntityTypes.InventoryTransaction, EntityTypes.StockBalance,
                     EntityTypes.CycleCount, EntityTypes.PickBatch, EntityTypes.PurchaseOrder, EntityTypes.Supplier, EntityTypes.CrossDockPlan,
                     EntityTypes.CrossDockAllocation, EntityTypes.DockAppointment, EntityTypes.TransportOrder,
                 })
            L(LookupDomains.EntityType, e);
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
        foreach (var c in new[] { DockTypes.Inbound, DockTypes.Outbound, DockTypes.Both }) L(LookupDomains.DockType, c);
        L(LookupDomains.UnitOfMeasure, "UN");
        L(LookupDomains.Country, "PR");
        L(LookupDomains.Country, "US");
        L(LookupDomains.Capability, Capabilities.EditPurchaseOrder);
        // Pruebas de servicio (al final: no cambia los ids anteriores).
        L(LookupDomains.EntityType, EntityTypes.ReceiptLine);
        foreach (var c in new[] { ReceiptTypes.Asn, ReceiptTypes.Blind, ReceiptTypes.Return }) L(LookupDomains.ReceiptType, c);
        foreach (var c in new[] { DockDirections.Inbound, DockDirections.Outbound }) L(LookupDomains.DockDirection, c);
        foreach (var c in new[] { ShortageActions.Close, ShortageActions.Reorder, ShortageActions.ManualAdjustment }) L(LookupDomains.ShortageAction, c);
        L(LookupDomains.EntityType, EntityTypes.ProductCategory);   // Lote 8A: sincronización de categorías (al final: ids previos intactos)
        // Lote 14: descuadres Kárdex ↔ saldo (al final: ids previos intactos).
        L(LookupDomains.EntityType, EntityTypes.InventoryDiscrepancy);
        foreach (var c in new[] { DiscrepancyKinds.Balance, DiscrepancyKinds.ProductTotal }) L(LookupDomains.InventoryDiscrepancyKind, c);
        foreach (var c in new[] { ReconciliationTriggers.Event, ReconciliationTriggers.Manual, ReconciliationTriggers.Scheduled, ReconciliationTriggers.Migration })
            L(LookupDomains.ReconciliationTrigger, c);
        // Lote 16: modo de recepción (al final: ids previos intactos).
        foreach (var c in ReceivingModes.All) L(LookupDomains.ReceivingMode, c);
        // Lote 26 (Rentas R0): motivo de sistema de la conversión a serie (al final: ids previos intactos).
        L(LookupDomains.AdjustmentReason, AdjustmentReasons.TrackingConversion);
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode
            {
                StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId,
                SortOrder = sort, IsInitial = initial, IsActive = true,
            };
            Db.StatusCodes.Add(s);
            _statusIds[domain + "|" + code] = s.StatusCodeId;
        }
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Active, pipe, 1, true);
        S(StatusDomains.WarehouseStatus, WarehouseStatuses.Inactive, term, 2);
        S(StatusDomains.DockStatus, DockStatuses.Free, pipe, 1, true);
        S(StatusDomains.DockStatus, DockStatuses.Occupied, lat, 2);
        S(StatusDomains.DockStatus, DockStatuses.Maintenance, lat, 3);
        // D16: RESERVED y SHIPPED laterales; SCRAPPED terminal.
        S(StatusDomains.SerialStatus, SerialStatuses.Available, pipe, 1, true);
        S(StatusDomains.SerialStatus, SerialStatuses.Reserved, lat, 2);
        S(StatusDomains.SerialStatus, SerialStatuses.Shipped, lat, 3);
        S(StatusDomains.SerialStatus, SerialStatuses.Scrapped, term, 4);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, pipe, 1, true);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, pipe, 2);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Done, term, 3);
        S(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled, term, 4);
        S(StatusDomains.ClientStatus, ClientStatuses.Active, pipe, 1, true);
        // Resto de dominios WMS (logistica-db-seed.sql, Lote 6), al final: no cambia los ids anteriores.
        // Lote 13: EXPECTED ocupa el lugar de OPEN (ids previos intactos); RECEIVING, DISCREPANCY y RECEIVED_VARIANCE al final.
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Expected, pipe, 1, true);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Received, pipe, 4);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Putaway, term, 6);
        S(StatusDomains.AsnStatus, AsnStatuses.Expected, pipe, 1, true);
        S(StatusDomains.AsnStatus, AsnStatuses.Received, term, 2);
        S(StatusDomains.AsnStatus, AsnStatuses.Cancelled, term, 3);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Open, pipe, 1, true);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Counted, pipe, 2);
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.Reconciled, term, 3);
        S(StatusDomains.AppointmentStatus, AppointmentStatuses.Scheduled, pipe, 1, true);
        S(StatusDomains.AppointmentStatus, AppointmentStatuses.Arrived, pipe, 2);
        S(StatusDomains.AppointmentStatus, AppointmentStatuses.Completed, term, 3);
        S(StatusDomains.AppointmentStatus, AppointmentStatuses.NoShow, lat, 4);
        S(StatusDomains.AppointmentStatus, AppointmentStatuses.Cancelled, term, 5);
        S(StatusDomains.CrossDockStatus, CrossDockStatuses.Open, pipe, 1, true);
        S(StatusDomains.CrossDockStatus, CrossDockStatuses.Allocated, pipe, 2);
        S(StatusDomains.CrossDockStatus, CrossDockStatuses.Completed, term, 3);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Planned, pipe, 1, true);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Moved, term, 2);
        S(StatusDomains.AllocationStatus, AllocationStatuses.Cancelled, term, 3);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Sent, pipe, 2);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Partial, pipe, 3);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Received, term, 4);
        S(StatusDomains.PurchaseOrderStatus, PurchaseOrderStatuses.Cancelled, term, 5);
        S(StatusDomains.PickBatchStatus, PickBatchStatuses.Collected, pipe, 1, true);
        S(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed, pipe, 2);
        S(StatusDomains.PickBatchStatus, PickBatchStatuses.Cancelled, term, 3);
        S(StatusDomains.OrderStatus, OrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.OrderStatus, OrderStatuses.Confirmed, pipe, 2);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Receiving, pipe, 2);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.Discrepancy, lat, 3);
        S(StatusDomains.ReceiptStatus, ReceiptStatuses.ReceivedWithVariance, lat, 5);
        // Lote 14 (D5): descuadre Kárdex ↔ saldo, como logistica-db-seed.sql (al final: ids previos intactos).
        S(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Open, pipe, 1, true);
        S(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Resolved, term, 2);
        S(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.Dismissed, term, 3);
        S(StatusDomains.InventoryDiscrepancyStatus, InventoryDiscrepancyStatuses.SelfCorrected, term, 4);
        // Lote 14 (D7): conteo cerrado con diferencia (al final: ids previos intactos).
        S(StatusDomains.CycleCountStatus, CycleCountStatuses.ReconciledVariance, term, 4);
        ReceiptStatusSeed.AddLateralEntries(Db, LookupId(LookupDomains.EntityType, EntityTypes.Receipt), StatusId);

        // 3G: WAREHOUSE_TASK CANCELLED solo desde PENDING e IN_PROGRESS.
        var taskType = LookupId(LookupDomains.EntityType, EntityTypes.WarehouseTask);
        var le = 1;
        foreach (var from in new[] { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress })
            Db.StatusLateralEntries.Add(new StatusLateralEntry
            {
                StatusLateralEntryId = le++, TenantId = null, EntityTypeLookupId = taskType,
                LateralStatusCodeId = StatusId(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Cancelled),
                FromStatusCodeId = StatusId(StatusDomains.WarehouseTaskStatus, from), IsAllowed = true,
            });

        Db.Tenants.AddRange(
            new Tenant { TenantId = TenantId, Name = "Tenant de prueba", IsActive = true },
            new Tenant { TenantId = OtherTenantId, Name = "Otro tenant", IsActive = true });
        await Db.SaveChangesAsync();
    }

    // ================================================================ builders

    /// <summary>Almacén ACTIVE del tenant indicado (por defecto el activo del contexto).</summary>
    public async Task<Warehouse> AddWarehouseAsync(string code, int? tenantId = null, bool isActive = true)
    {
        var w = new Warehouse
        {
            WarehouseId = _nextId++, PublicId = Guid.NewGuid(), TenantId = tenantId ?? Tenant.TenantId!.Value, Code = code, Name = "Almacén " + code,
            CountryLookupId = LookupId(LookupDomains.Country, "PR"),
            StatusCodeId = StatusId(StatusDomains.WarehouseStatus, isActive ? WarehouseStatuses.Active : WarehouseStatuses.Inactive), IsActive = isActive,
        };
        Db.Warehouses.Add(w);
        await SaveAsync();
        return w;
    }

    public async Task<WarehouseZone> AddZoneAsync(Warehouse w, string code, string zoneType, bool isActive = true)
    {
        var z = new WarehouseZone
        {
            WarehouseZoneId = _nextId++, WarehouseId = w.WarehouseId, Code = code, Name = "Zona " + code,
            ZoneTypeLookupId = LookupId(LookupDomains.ZoneType, zoneType), IsActive = isActive,
        };
        Db.WarehouseZones.Add(z);
        await SaveAsync();
        return z;
    }

    /// <summary>Posición de la zona; cupo y partes opcionales (Lote 1 de cambios de Almacén: ocupación y búsqueda por partes).</summary>
    public async Task<WarehouseBin> AddBinAsync(WarehouseZone z, string code, bool isActive = true, decimal? maxWeightKg = null,
        int? maxCapacityQty = null, string? aisle = null, string? rack = null, string? level = null, string? position = null)
    {
        var b = new WarehouseBin
        {
            WarehouseBinId = _nextId++, WarehouseZoneId = z.WarehouseZoneId, WarehouseId = z.WarehouseId, Code = code, IsActive = isActive,
            MaxWeightKg = maxWeightKg, MaxCapacityQty = maxCapacityQty, Aisle = aisle, Rack = rack, Level = level, Position = position,
        };
        Db.WarehouseBins.Add(b);
        await SaveAsync();
        return b;
    }

    public async Task<WarehouseDock> AddDockAsync(Warehouse w, string code, string dockType = DockTypes.Both)
    {
        var d = new WarehouseDock
        {
            WarehouseDockId = _nextId++, WarehouseId = w.WarehouseId, Code = code, DockTypeLookupId = LookupId(LookupDomains.DockType, dockType),
            StatusCodeId = StatusId(StatusDomains.DockStatus, DockStatuses.Free), IsActive = true,
        };
        Db.WarehouseDocks.Add(d);
        await SaveAsync();
        return d;
    }

    public async Task<Client> AddClientAsync(string code, int? tenantId = null)
    {
        var c = new Client
        {
            ClientId = _nextId++, PublicId = Guid.NewGuid(), TenantId = tenantId ?? Tenant.TenantId!.Value, Code = code, Name = "Cliente " + code,
            StatusCodeId = StatusId(StatusDomains.ClientStatus, ClientStatuses.Active), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Clients.Add(c);
        await SaveAsync();
        return c;
    }

    /// <summary>Producto con seguimiento NONE/LOT/SERIAL, dueño opcional (cliente 3PL) y tenant opcional.</summary>
    public async Task<Product> AddProductAsync(string sku, string tracking = TrackingTypes.None, int? ownerClientId = null, int? tenantId = null,
        bool isActive = true, decimal? purchaseCost = null, decimal? weightKg = null, int? preferredWarehouseId = null, int? preferredBinId = null)
    {
        var p = new Product
        {
            ProductId = _nextId++, PublicId = Guid.NewGuid(), TenantId = tenantId ?? Tenant.TenantId!.Value, ClientId = ownerClientId, Sku = sku,
            Name = "Producto " + sku, BaseUomLookupId = LookupId(LookupDomains.UnitOfMeasure, "UN"),
            TrackingTypeLookupId = LookupId(LookupDomains.TrackingType, tracking), IsActive = isActive, PurchaseCost = purchaseCost,
            WeightKg = weightKg, PreferredWarehouseId = preferredWarehouseId, PreferredBinId = preferredBinId,
        };
        Db.Products.Add(p);
        await SaveAsync();
        return p;
    }

    public async Task<InventoryLot> AddLotAsync(Product p, string number, DateOnly? expiry = null)
    {
        var l = new InventoryLot { LotId = _nextId++, ProductId = p.ProductId, LotNumber = number, ExpiryDate = expiry, IsActive = true };
        Db.InventoryLots.Add(l);
        await SaveAsync();
        return l;
    }

    /// <summary>Orden de transporte activa del tenant activo (DRAFT por defecto: etapa inicial).</summary>
    public async Task<Teikem.Domain.Orders.TransportOrder> AddOrderAsync(int clientId, string number, string status = OrderStatuses.Draft,
        string? packBatchNumber = null)
    {
        var o = new Teikem.Domain.Orders.TransportOrder
        {
            PublicId = Guid.NewGuid(), TenantId = Tenant.TenantId!.Value, ClientId = clientId, OrderNumber = number,
            PackBatchNumber = packBatchNumber ?? "EMP-" + number, ServiceTypeLookupId = 1,
            StatusCodeId = StatusId(StatusDomains.OrderStatus, status), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        Db.TransportOrders.Add(o);
        await SaveAsync();
        return o;
    }

    /// <summary>Recibo (encabezado sin líneas) en el estatus indicado, del tenant activo.</summary>
    public async Task<ReceiptHeader> AddReceiptAsync(Warehouse w, string statusCode, string type = ReceiptTypes.Blind)
    {
        var r = new ReceiptHeader
        {
            ReceiptHeaderId = _nextId++, PublicId = Guid.NewGuid(), TenantId = Tenant.TenantId!.Value, WarehouseId = w.WarehouseId,
            ReceiptTypeLookupId = LookupId(LookupDomains.ReceiptType, type), Number = $"REC-{_nextId:00000}",
            StatusCodeId = StatusId(StatusDomains.ReceiptStatus, statusCode), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Set<ReceiptHeader>().Add(r);
        await SaveAsync();
        return r;
    }

    /// <summary>Tarea PENDING creada con el WarehouseTaskWriter real (historial null → PENDING).</summary>
    public async Task<WarehouseTask> AddTaskAsync(WarehouseTaskSpec spec)
    {
        try { return await Db.RunInTransactionAsync(ct => TaskWriter.CreateAsync(spec, ct), default); }
        finally { Db.ChangeTracker.Clear(); }
    }

    public async Task<WarehouseTask> TaskAsync(int id)
        => await Db.WarehouseTasks.AsNoTracking().SingleAsync(t => t.WarehouseTaskId == id);

    /// <summary>Códigos de estatus destino del historial de la entidad, en orden.</summary>
    public async Task<List<string>> HistoryCodesAsync(string entityType, int entityId)
    {
        var typeId = LookupId(LookupDomains.EntityType, entityType);
        var toIds = await Db.EntityStatusHistories.AsNoTracking()
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        return toIds.Select(StatusCodeOf).ToList();
    }

    // ================================================================ ledger y lecturas

    /// <summary>Contabiliza por el ledger real dentro de RunInTransactionAsync y limpia el tracker.</summary>
    public async Task<IReadOnlyList<long>> PostAsync(params InventoryPosting[] postings)
    {
        try
        {
            return await Db.RunInTransactionAsync(ct => Ledger.PostAsync(postings, ct), default);
        }
        finally
        {
            Db.ChangeTracker.Clear();
        }
    }

    public async Task ReserveAsync(params StockReservation[] reservations)
    {
        try { await Db.RunInTransactionAsync(ct => Ledger.ReserveAsync(reservations, ct), default); }
        finally { Db.ChangeTracker.Clear(); }
    }

    public async Task ReleaseAsync(params StockReservation[] reservations)
    {
        try { await Db.RunInTransactionAsync(ct => Ledger.ReleaseAsync(reservations, ct), default); }
        finally { Db.ChangeTracker.Clear(); }
    }

    public async Task<StockBalance?> BalanceAsync(int productId, int binId, int? lotId = null)
        => await Db.StockBalances.AsNoTracking().SingleOrDefaultAsync(b => b.ProductId == productId && b.WarehouseBinId == binId && b.LotId == lotId);

    public async Task<decimal> OnHandAsync(int productId, int binId, int? lotId = null)
        => (await BalanceAsync(productId, binId, lotId))?.QtyOnHand ?? 0m;

    public async Task<List<InventoryTransaction>> TransactionsAsync()
        => await Db.InventoryTransactions.AsNoTracking().OrderBy(t => t.InventoryTransactionId).ToListAsync();

    public async Task<InventorySerial> SerialAsync(int productId, string number)
        => await Db.InventorySerials.AsNoTracking().SingleAsync(s => s.ProductId == productId && s.SerialNumber == number);

    private async Task SaveAsync()
    {
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
    }
}

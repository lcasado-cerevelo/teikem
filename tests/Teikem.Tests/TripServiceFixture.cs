using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using AvailabilityResult = Teikem.Domain.Fleet.AvailabilityResult;
using RouteOptimizationRequest = Teikem.Domain.Trips.RouteOptimizationRequest;
using RouteOptimizationResult = Teikem.Domain.Trips.RouteOptimizationResult;
using TripEntity = Teikem.Domain.Trips.Trip;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P8 — fixture InMemory para probar los SERVICIOS de Trips y rutas (optimización, despacho, escaneo) con el
/// StatusService, RouteWriter, TripIssueBuilder, TripReadService y los efectos reales, resueltos por DI como en producción.
/// Con InMemory, TripQueries carga el Trip tracked sin bloqueo, RunInTransactionAsync no abre transacción real y el SQL
/// crudo (coordenadas, pines, pings) es no-op: se prueba la orquestación de cada servicio, no el bloqueo de SQL Server
/// (eso lo cubre el smoke). Pipelines sembrados igual que logistica-db-seed.sql.
/// </summary>
internal sealed class TripServiceFixture : IAsyncDisposable
{
    public const int TenantId = 1;
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
    private int _nextOrderId = 5000;
    private int _nextStopId = 9000;
    private int _nextTripId = 700;

    private TripServiceFixture(TeikemDbContext db, ServiceProvider services, TripTestLookups lookups, FakeRouteOptimizer optimizer,
        AlwaysAvailable availability)
    {
        Db = db;
        Services = services;
        Lookups = lookups;
        Optimizer = optimizer;
        Availability = availability;
    }

    public TeikemDbContext Db { get; }
    public ServiceProvider Services { get; }
    public TripTestLookups Lookups { get; }
    public FakeRouteOptimizer Optimizer { get; }
    /// <summary>Disponibilidad del Lote 4 de prueba: todo disponible salvo los vehículos marcados en BlockedVehicles.</summary>
    public AlwaysAvailable Availability { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Zonas sembradas: Z1 = CP 00901, Z2 = CP 00902. Las órdenes con CP 00999 no tienen zona.</summary>
    public int Zone1 { get; private set; }
    public int Zone2 { get; private set; }

    public static async Task<TripServiceFixture> CreateAsync()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("trip-services-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var optimizer = new FakeRouteOptimizer();
        var availability = new AlwaysAvailable();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<ILookupCache>(lookups);
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<IFleetAvailabilityService>(availability);
        services.AddSingleton<IRouteOptimizer>(optimizer);
        services.AddSingleton<IStatusTransitionEffect>(sp => new TripStatusEffect(db, sp, lookups));
        services.AddSingleton<IStatusTransitionEffect>(sp => new TripOrderReleaseEffect(db, sp));
        // PermissionService solo lo usa GetHistoryAsync (no se usa aquí).
        services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), null!));
        services.AddSingleton<ModuleService>();
        services.AddSingleton<RouteWriter>();
        services.AddSingleton<TripIssueBuilder>();
        services.AddSingleton<TripReadService>();
        services.AddSingleton<TripDispatchService>();
        services.AddSingleton<RouteOptimizationService>();
        services.AddSingleton<OrderReadService>();
        services.AddSingleton<OutboundScanService>();
        // Edición de cabecera (UpdateAsync) y monitor: UpdateAsync no numera; el contador de prueba solo cubre la firma.
        services.AddSingleton<INumberSequenceService, InMemoryNumberSequence>();
        services.AddSingleton<TripService>();
        services.AddSingleton<TripMonitorService>();
        var provider = services.BuildServiceProvider();

        var f = new TripServiceFixture(db, provider, lookups, optimizer, availability);
        await f.SeedCatalogsAsync();
        return f;
    }

    public int Id(string domain, string code) => _statusIds[domain + "|" + code];

    public string CodeOf(int statusCodeId) => _statusIds.Single(kv => kv.Value == statusCodeId).Key.Split('|')[1];

    private async Task SeedCatalogsAsync()
    {
        var id = 1;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
        var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
        var term = L(LookupDomains.StageKind, StageKinds.Terminal);
        var postal = L(LookupDomains.ZoneMatchType, ZoneMatchTypes.PostalCode);
        var all = new List<LookupCode>
        {
            pipe, lat, term, postal,
            L(LookupDomains.EntityType, EntityTypes.Trip),
            L(LookupDomains.EntityType, EntityTypes.Route),
            L(LookupDomains.EntityType, EntityTypes.RouteStop),
            L(LookupDomains.EntityType, EntityTypes.OptimizationRun),
            L(LookupDomains.EntityType, EntityTypes.TransportOrder),
            L(LookupDomains.EntityType, EntityTypes.Client),
            L(LookupDomains.EntityType, EntityTypes.Vehicle),
            L(LookupDomains.Capability, Capabilities.AssignTrip),
            L(LookupDomains.Capability, Capabilities.EditTrip),
            L(LookupDomains.StopType, StopTypes.Pickup),
            L(LookupDomains.StopType, StopTypes.Delivery),
            L(LookupDomains.OptimizerEngine, OptimizerEngines.Heuristic),
            L(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.Exact),
            L(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.ZipCentroid),
            L(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.Manual),
        };
        Db.LookupCodes.AddRange(all);
        Lookups.Load(all);

        var sid = 100;
        void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
        {
            var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
            Db.StatusCodes.Add(s);
            _statusIds[domain + "|" + code] = s.StatusCodeId;
        }

        S(StatusDomains.OrderStatus, OrderStatuses.Draft, pipe, 1, true);
        S(StatusDomains.OrderStatus, OrderStatuses.Confirmed, pipe, 2);
        S(StatusDomains.OrderStatus, OrderStatuses.Pickup, pipe, 3);
        S(StatusDomains.OrderStatus, OrderStatuses.Inbound, pipe, 4);
        S(StatusDomains.OrderStatus, OrderStatuses.Planned, pipe, 5);
        S(StatusDomains.OrderStatus, OrderStatuses.InTransit, pipe, 6);
        S(StatusDomains.OrderStatus, OrderStatuses.Arrived, pipe, 7);
        S(StatusDomains.OrderStatus, OrderStatuses.Delivered, term, 8);
        S(StatusDomains.OrderStatus, OrderStatuses.OnHold, lat, 20);
        S(StatusDomains.OrderStatus, OrderStatuses.Cancelled, term, 30);

        S(StatusDomains.StopStatus, StopStatuses.Pending, pipe, 1, true);
        S(StatusDomains.StopStatus, StopStatuses.Completed, term, 3);

        S(StatusDomains.TripStatus, TripStatuses.Draft, pipe, 1, true);
        S(StatusDomains.TripStatus, TripStatuses.Planned, pipe, 2);
        S(StatusDomains.TripStatus, TripStatuses.Dispatched, pipe, 3);
        S(StatusDomains.TripStatus, TripStatuses.InProgress, pipe, 4);
        S(StatusDomains.TripStatus, TripStatuses.Completed, term, 5);
        S(StatusDomains.TripStatus, TripStatuses.Cancelled, term, 6);

        S(StatusDomains.RouteStatus, RouteStatuses.Draft, pipe, 1, true);
        S(StatusDomains.RouteStatus, RouteStatuses.Optimized, pipe, 2);
        S(StatusDomains.RouteStatus, RouteStatuses.Active, pipe, 3);
        S(StatusDomains.RouteStatus, RouteStatuses.Archived, term, 4);

        S(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending, pipe, 1, true);
        S(StatusDomains.RouteStopStatus, RouteStopStatuses.Completed, term, 4);

        S(StatusDomains.OptimizationRunStatus, OptimizationRunStatuses.Pending, pipe, 1, true);
        S(StatusDomains.OptimizationRunStatus, OptimizationRunStatuses.Ok, term, 2);
        S(StatusDomains.OptimizationRunStatus, OptimizationRunStatuses.Error, term, 3);

        Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", MaxStopsPerRouteDefault = 25, IsActive = true });
        Db.ModuleDefinitions.Add(new ModuleDefinition { ModuleKey = ModuleKeys.Catalog, Name = "Catálogo", NameEn = "Catalog", Category = "CORE", IsActive = true });
        Db.TenantModules.Add(new TenantModule { TenantId = TenantId, ModuleKey = ModuleKeys.Catalog, IsEnabled = true });

        var z1 = new DispatchZone { DispatchZoneId = 11, TenantId = TenantId, Code = "Z1", Name = "Zona 1", IsActive = true };
        var z2 = new DispatchZone { DispatchZoneId = 12, TenantId = TenantId, Code = "Z2", Name = "Zona 2", IsActive = true };
        Db.DispatchZones.AddRange(z1, z2);
        Db.DispatchZoneMembers.AddRange(
            new DispatchZoneMember { DispatchZoneMemberId = 1, DispatchZoneId = z1.DispatchZoneId, MatchTypeLookupId = postal.LookupCodeId, MatchValue = "00901" },
            new DispatchZoneMember { DispatchZoneMemberId = 2, DispatchZoneId = z2.DispatchZoneId, MatchTypeLookupId = postal.LookupCodeId, MatchValue = "00902" });
        Zone1 = z1.DispatchZoneId;
        Zone2 = z2.DispatchZoneId;
        await Db.SaveChangesAsync();
    }

    /// <summary>Orden activa, no especial, con una parada DELIVERY pendiente en ese CP (define la zona).</summary>
    /// <remarks>clientId/orderNumber permiten armar rutas multi-cliente (la numeración es por cliente: puede repetirse).</remarks>
    public async Task<SeededOrder> SeedOrderAsync(string status, string postalCode = "00901", int clientId = 1, string? orderNumber = null)
    {
        var orderId = _nextOrderId++;
        var stopId = _nextStopId++;
        var publicId = Guid.NewGuid();
        var number = orderNumber ?? $"2026-{orderId:000000}";
        Db.TransportOrders.Add(new TransportOrder
        {
            TransportOrderId = orderId, PublicId = publicId, TenantId = TenantId, ClientId = clientId,
            OrderNumber = number, PackBatchNumber = $"PB{orderId}", ClientInvoiceNumber = $"F{orderId}",
            ServiceTypeLookupId = 1, StatusCodeId = Id(StatusDomains.OrderStatus, status), IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });
        Db.OrderStops.Add(new OrderStop
        {
            OrderStopId = stopId, TransportOrderId = orderId,
            StopTypeLookupId = await Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery),
            Sequence = 2, SnapLine1 = "Calle 1", SnapCity = "San Juan", SnapPostalCode = postalCode, ServiceMinutes = 10,
            GeocodeAccuracyLookupId = await Lookups.GetIdAsync(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.ZipCentroid),
            StatusCodeId = Id(StatusDomains.StopStatus, StopStatuses.Pending),
        });
        await Db.SaveChangesAsync();
        return new SeededOrder(orderId, publicId, number, $"PB{orderId}", stopId);
    }

    /// <summary>Cliente del tenant (para ClientName en las paradas de rutas multi-cliente).</summary>
    public async Task SeedClientAsync(int clientId, string name)
    {
        Db.Clients.Add(new Client
        {
            ClientId = clientId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "C" + clientId, Name = name,
            StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,   // el estatus del cliente no interviene aquí
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Ruta sin versión (se crea al agregar órdenes). Se guarda y se suelta del tracker.</summary>
    public async Task<(int TripId, Guid PublicId)> SeedTripAsync(string code, string status, int? zoneId = null, DateOnly? planDate = null,
        bool withDriver = true, bool withVehicle = true, bool isActive = true)
    {
        var trip = new TripEntity
        {
            TripId = _nextTripId++, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = code,
            PlanDate = planDate ?? Today, DispatchZoneId = zoneId,
            DriverId = withDriver ? 7 : null, VehicleId = withVehicle ? 9 : null,
            StatusCodeId = Id(StatusDomains.TripStatus, status),
            PlannedStartUtc = (planDate ?? Today).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc),
            IsActive = isActive, CreatedAtUtc = DateTime.UtcNow,
        };
        Db.Trips.Add(trip);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return (trip.TripId, trip.PublicId);
    }

    /// <summary>Chofer del tenant (sin override = usa Tenant.MaxStopsPerRouteDefault). SeedTripAsync usa el id 7.</summary>
    public async Task SeedDriverAsync(int driverId, int? maxStopsPerRoute, string code = "D7", string name = "Chofer Siete")
    {
        Db.Drivers.Add(new Driver
        {
            DriverId = driverId, PublicId = Guid.NewGuid(), TenantId = TenantId, EmployeeCode = code, FullName = name,
            MaxStopsPerRoute = maxStopsPerRoute, StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Vehículo del tenant con su capacidad. SeedTripAsync usa el id 9.</summary>
    public async Task SeedVehicleAsync(int vehicleId, int? maxStops, decimal? maxWeightKg, decimal? maxVolumeM3, string code = "V9")
    {
        Db.Vehicles.Add(new Vehicle
        {
            VehicleId = vehicleId, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = code, StatusCodeId = 1,
            MaxStops = maxStops, MaxWeightKg = maxWeightKg, MaxVolumeM3 = maxVolumeM3, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Cambia Tenant.MaxStopsPerRouteDefault (respaldo del máximo de paradas, maestro L270).</summary>
    public async Task SetTenantMaxStopsDefaultAsync(int value)
    {
        var t = await Db.Tenants.SingleAsync(x => x.TenantId == TenantId);
        t.MaxStopsPerRouteDefault = value;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Agrega órdenes con RouteWriter (como TripOrderService, sin la transacción) y guarda.</summary>
    public async Task AddOrdersAsync(int tripId, params int[] orderIds)
    {
        var trip = await Db.LockTripAsync(tripId, default);
        await Get<RouteWriter>().AddOrdersAsync(trip, orderIds, default);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Cambia el estatus del Trip directamente (sin efectos): prepara escenarios.</summary>
    public async Task SetTripStatusAsync(int tripId, string status)
    {
        var trip = await Db.Trips.SingleAsync(t => t.TripId == tripId);
        trip.StatusCodeId = Id(StatusDomains.TripStatus, status);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Override del tenant que deshabilita la etapa (StatusCodeOverride.IsEnabled = false).</summary>
    public async Task DisableAsync(string domain, string code)
    {
        Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = TenantId, StatusCodeId = Id(domain, code), IsEnabled = false });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Niega una capacidad en un estatus para el tenant (StatusCapability.IsAllowed = false).</summary>
    public async Task DenyCapabilityAsync(string entityType, string statusDomain, string statusCode, string capability)
    {
        Db.StatusCapabilities.Add(new StatusCapability
        {
            TenantId = TenantId,
            EntityTypeLookupId = await Lookups.GetIdAsync(LookupDomains.EntityType, entityType),
            StatusCodeId = Id(statusDomain, statusCode),
            CapabilityLookupId = await Lookups.GetIdAsync(LookupDomains.Capability, capability),
            IsAllowed = false,
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    /// <summary>Enciende o apaga un módulo del tenant (TenantModule) e invalida la caché de ModuleService.</summary>
    public async Task SetModuleEnabledAsync(string moduleKey, bool enabled)
    {
        var row = await Db.TenantModules.SingleAsync(m => m.TenantId == TenantId && m.ModuleKey == moduleKey);
        row.IsEnabled = enabled;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        Get<ModuleService>().Invalidate(TenantId);
    }

    /// <summary>Zona de despacho activa de OTRO tenant (id 99): el filtro global la esconde.</summary>
    public async Task<int> SeedForeignZoneAsync()
    {
        Db.DispatchZones.Add(new DispatchZone { DispatchZoneId = 99, TenantId = TenantId + 1, Code = "ZX", Name = "Zona ajena", IsActive = true });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return 99;
    }

    /// <summary>Chofer activo de OTRO tenant: el filtro global lo esconde.</summary>
    public async Task<Guid> SeedForeignDriverAsync()
    {
        var publicId = Guid.NewGuid();
        Db.Drivers.Add(new Driver
        {
            DriverId = 99, PublicId = publicId, TenantId = TenantId + 1, EmployeeCode = "DX", FullName = "Chofer ajeno",
            StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return publicId;
    }

    public async Task<string> TripStatusAsync(int tripId)
        => CodeOf(await Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.StatusCodeId).SingleAsync());

    public async Task<string> OrderStatusAsync(int orderId)
        => CodeOf(await Db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == orderId).Select(o => o.StatusCodeId).SingleAsync());

    public async Task<List<string?>> CommentsAsync(string entityType, int entityId)
    {
        var typeId = await Lookups.GetIdAsync(LookupDomains.EntityType, entityType);
        return await Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.Comment).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await Db.DisposeAsync();
    }
}

internal sealed record SeededOrder(int Id, Guid PublicId, string OrderNumber, string PackBatchNumber, int StopId);

/// <summary>IRouteOptimizer de prueba: el handler decide el resultado (o lanza, o espera la cancelación).</summary>
internal sealed class FakeRouteOptimizer : IRouteOptimizer
{
    public string EngineCode => OptimizerEngines.Heuristic;
    public Func<RouteOptimizationRequest, CancellationToken, Task<RouteOptimizationResult>> Handler { get; set; }
        = (req, _) => Task.FromResult(new RouteOptimizationResult(req.Stops.Select(s => s.OrderStopId).ToList(), Array.Empty<Teikem.Domain.Trips.UnassignedPlanStop>(), null));
    public int Calls { get; private set; }

    public Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken ct)
    {
        Calls++;
        return Handler(request, ct);
    }
}

/// <summary>
/// Disponibilidad del Lote 4 de prueba: todo chofer y vehículo está disponible, salvo los vehículos de BlockedVehicles
/// (simulan una orden de trabajo abierta: problema bloqueante, maestro L286) y los choferes de BlockedDrivers (licencia
/// vencida).
/// </summary>
internal sealed class AlwaysAvailable : IFleetAvailabilityService
{
    public const string BlockedVehicleMessage = "Tiene una orden de trabajo abierta.";
    public const string BlockedDriverMessage = "La licencia está vencida.";
    public HashSet<int> BlockedVehicles { get; } = new();
    public HashSet<int> BlockedDrivers { get; } = new();

    public Task<FleetAvailabilityDto> GetAsync(DateOnly date, bool onlyAvailable, CancellationToken ct)
        => Task.FromResult(new FleetAvailabilityDto(date, Array.Empty<DriverAvailabilityDto>(), Array.Empty<VehicleAvailabilityDto>()));

    public Task<AvailabilityResult> CheckDriverAsync(int driverId, DateOnly date, CancellationToken ct)
        => Task.FromResult(BlockedDrivers.Contains(driverId)
            ? new AvailabilityResult(false, new[] { new AvailabilityIssue("LICENSE_EXPIRED", BlockedDriverMessage, true) })
            : new AvailabilityResult(true, Array.Empty<AvailabilityIssue>()));

    public Task<AvailabilityResult> CheckVehicleAsync(int vehicleId, DateOnly date, CancellationToken ct)
        => Task.FromResult(BlockedVehicles.Contains(vehicleId)
            ? new AvailabilityResult(false, new[] { new AvailabilityIssue("WORK_ORDER_OPEN", BlockedVehicleMessage, true) })
            : new AvailabilityResult(true, Array.Empty<AvailabilityIssue>()));
}

/// <summary>Contador de numeración en memoria (por clase y cliente), para servicios que lo reciben por constructor.</summary>
internal sealed class InMemoryNumberSequence : INumberSequenceService
{
    private readonly Dictionary<(string, int?), long> _next = new();

    public Task EnsureAsync(string kind, int? clientId, CancellationToken ct)
    {
        _next.TryAdd((kind, clientId), 1);
        return Task.CompletedTask;
    }

    public Task<long> NextAsync(string kind, int? clientId, CancellationToken ct)
    {
        if (!_next.TryGetValue((kind, clientId), out var value)) throw new InvalidOperationException("Falta EnsureAsync.");
        _next[(kind, clientId)] = value + 1;
        return Task.FromResult(value);
    }
}

/// <summary>ILookupCache en memoria sobre los LookupCode sembrados.</summary>
internal sealed class TripTestLookups : ILookupCache
{
    private List<LookupCode> _all = new();

    public void Load(IEnumerable<LookupCode> all) => _all = all.ToList();

    /// <summary>
    /// Gancho de prueba que corre antes de cada GetIdAsync: permite simular una carrera en un punto preciso del flujo
    /// (p. ej. otra solicitud que asigna la orden mientras el escaneo ya tiene la ruta bloqueada). El gancho se desarma solo.
    /// </summary>
    public Func<Task>? BeforeGetId { get; set; }

    public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default)
    {
        if (BeforeGetId is { } hook) await hook();
        return Find(entity, code)?.LookupCodeId ?? throw new NotFoundException(entity, code);
    }

    public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
        => Task.FromResult(Find(entity, code)?.LookupCodeId);

    public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
        => Task.FromResult(_all.FirstOrDefault(l => l.LookupCodeId == lookupCodeId));

    public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LookupCode>>(_all.Where(l => string.Equals(l.Entity, entity, StringComparison.OrdinalIgnoreCase)).ToList());

    public void Invalidate() { }

    private LookupCode? Find(string entity, string code)
        => _all.FirstOrDefault(l => string.Equals(l.Entity, entity, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(l.InternalCode, code, StringComparison.OrdinalIgnoreCase));
}

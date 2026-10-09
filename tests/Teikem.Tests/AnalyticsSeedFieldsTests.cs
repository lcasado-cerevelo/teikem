using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Seeding;
using Xunit;
using Trip = Teikem.Domain.Trips.Trip;
using TripRoute = Teikem.Domain.Trips.Route;
using RouteStop = Teikem.Domain.Trips.RouteStop;
using RentalAnalyticsRules = Teikem.Domain.Wms.RentalAnalyticsRules;
using TripOrder = Teikem.Domain.Trips.TripOrder;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P7: el contenido de sistema del lote (vista 'Rutas', indicadores 'Órdenes sin chofer asignado', 'Órdenes en
/// excepción' y 'Rutas sobre el máximo de paradas', gráfico 'Rutas por estatus') solo usa campos que existen en
/// TripDataSource o en TransportOrderDataSource. El seeder no valida campos al sembrar: un nombre mal escrito dejaría un
/// indicador en cero sin error, por eso se corre el seeder real sobre InMemory y se revisa cada campo que referencia
/// (columnas, filtros, agrupación, orden, campo agregado y agrupación del gráfico) de las definiciones de TRIP y TRANSPORT_ORDER.
/// También fija la forma de las fuentes: TRIP con DateField PlanDate y relaciones; TRANSPORT_ORDER con los campos del lote
/// y la relación Trip; ninguna con campos de dinero de chofer (Amount/Rate).
/// </summary>
public class AnalyticsSeedFieldsTests
{
    private const int TenantId = 1;

    /// <summary>Caché de catálogos falsa: asigna un id estable a cada (dominio, código) que el seeder pida.</summary>
    private sealed class FakeLookups : ILookupCache
    {
        private readonly Dictionary<(string, string), LookupCode> _byKey = new();
        private readonly Dictionary<int, LookupCode> _byId = new();

        public Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => Task.FromResult(Get(entity, code).LookupCodeId);
        public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default) => Task.FromResult<int?>(Get(entity, code).LookupCodeId);
        public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default) => Task.FromResult(_byId.GetValueOrDefault(lookupCodeId));
        public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LookupCode>>(_byId.Values.Where(l => l.Entity == entity).ToList());
        public void Invalidate() { }

        private LookupCode Get(string entity, string code)
        {
            var key = (entity.ToUpperInvariant(), code.ToUpperInvariant());
            if (_byKey.TryGetValue(key, out var lc)) return lc;
            lc = new LookupCode { LookupCodeId = _byId.Count + 1000, Entity = entity, InternalCode = code };
            _byKey[key] = lc;
            _byId[lc.LookupCodeId] = lc;
            return lc;
        }

        public string? CodeOf(int id) => _byId.GetValueOrDefault(id)?.InternalCode;
    }

    private static TeikemDbContext InMemoryDb(TenantContext tenant)
        => new(new DbContextOptionsBuilder<TeikemDbContext>().UseInMemoryDatabase("analytics-seed-fields-" + Guid.NewGuid()).Options, tenant);

    private sealed record FieldUse(string Source, string Where, string Field);

    /// <summary>Corre el seeder real y devuelve cada uso de campo de las definiciones de TRIP y TRANSPORT_ORDER.</summary>
    private static Task<(List<FieldUse> Uses, List<string> Reports, List<(string Name, string Source)> Indicators, List<(string Name, string Source)> Charts)> SeedAsync()
        => SeedAsync(new[] { EntityTypes.Trip, EntityTypes.TransportOrder }, EntityTypes.Trip);

    /// <summary>Corre el seeder real y devuelve cada uso de campo de las definiciones de las fuentes dadas.</summary>
    private static async Task<(List<FieldUse> Uses, List<string> Reports, List<(string Name, string Source)> Indicators, List<(string Name, string Source)> Charts)> SeedAsync(
        string[] sources, string? reportSource)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        var uses = new List<FieldUse>();

        var reports = new List<string>();
        foreach (var r in await db.ReportDefinitions.AsNoTracking().ToListAsync())
        {
            var source = lookups.CodeOf(r.BaseEntityTypeLookupId);
            if (source is null || !sources.Contains(source)) continue;
            if (reportSource is null || source == reportSource) reports.Add(r.Name);
            var where = $"vista '{r.Name}'";
            foreach (var c in JsonSerializer.Deserialize<string[]>(r.ColumnsJson ?? "[]") ?? Array.Empty<string>()) uses.Add(new(source, where, c));
            foreach (var f in JsonFields(r.FilterJson)) uses.Add(new(source, where + " (filtro)", f));
            foreach (var f in JsonFields(r.GroupJson)) uses.Add(new(source, where + " (agrupación)", f));
            foreach (var f in JsonFields(r.SortJson)) uses.Add(new(source, where + " (orden)", f));
        }

        var indicators = new List<(string, string)>();
        foreach (var i in await db.IndicatorDefinitions.AsNoTracking().ToListAsync())
        {
            if (!sources.Contains(i.DataSourceKey)) continue;
            indicators.Add((i.Name, i.DataSourceKey));
            var where = $"indicador '{i.Name}'";
            if (!string.IsNullOrEmpty(i.FieldKey)) uses.Add(new(i.DataSourceKey, where, i.FieldKey));
            foreach (var f in JsonFields(i.FilterJson)) uses.Add(new(i.DataSourceKey, where + " (filtro)", f));
        }

        var charts = new List<(string, string)>();
        foreach (var c in await db.ChartDefinitions.AsNoTracking().ToListAsync())
        {
            if (!sources.Contains(c.DataSourceKey)) continue;
            charts.Add((c.Name, c.DataSourceKey));
            var where = $"gráfico '{c.Name}'";
            uses.Add(new(c.DataSourceKey, where + " (agrupación)", c.GroupByField));
            if (!string.IsNullOrEmpty(c.FieldKey)) uses.Add(new(c.DataSourceKey, where, c.FieldKey));
            foreach (var f in JsonFields(c.FilterJson)) uses.Add(new(c.DataSourceKey, where + " (filtro)", f));
        }
        return (uses, reports, indicators, charts);
    }

    /// <summary>Todos los valores de "field" (y de "by" en una agrupación) de un JSON de filtro/orden/agrupación.</summary>
    private static IEnumerable<string> JsonFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        var found = new List<string>();
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement, found);
        return found;

        static void Walk(JsonElement e, List<string> acc)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.NameEquals("field") && p.Value.ValueKind == JsonValueKind.String) acc.Add(p.Value.GetString()!);
                        else if (p.NameEquals("by") && p.Value.ValueKind == JsonValueKind.Array)
                            acc.AddRange(p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
                        else Walk(p.Value, acc);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) Walk(x, acc);
                    break;
            }
        }
    }

    private static IReadOnlyList<DataField> FieldsOf(string source)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var db = InMemoryDb(tenant);
        return source == EntityTypes.Trip
            ? new TripDataSource(db, tenant).Fields
            : new TransportOrderDataSource(db, new FakeLookups(), tenant).Fields;
    }

    [Fact]
    public async Task Lote5_system_content_is_seeded()
    {
        var (_, reports, indicators, charts) = await SeedAsync();
        Assert.Contains("Rutas", reports);
        Assert.Contains(("Órdenes sin chofer asignado", EntityTypes.TransportOrder), indicators);
        Assert.Contains(("Órdenes en excepción", EntityTypes.TransportOrder), indicators);
        Assert.Contains(("Rutas sobre el máximo de paradas", EntityTypes.Trip), indicators);
        Assert.Contains(("Rutas por estatus", EntityTypes.Trip), charts);
    }

    [Fact]
    public async Task Every_seeded_field_exists_in_its_data_source()
    {
        var (uses, _, _, _) = await SeedAsync();
        Assert.NotEmpty(uses);
        var known = new Dictionary<string, HashSet<string>>
        {
            [EntityTypes.Trip] = FieldsOf(EntityTypes.Trip).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            [EntityTypes.TransportOrder] = FieldsOf(EntityTypes.TransportOrder).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
        var missing = uses.Where(u => !known[u.Source].Contains(u.Field))
            .Select(u => $"{u.Source} · {u.Where}: '{u.Field}'").Distinct().ToList();
        Assert.True(missing.Count == 0, "Campos sembrados que la fuente no expone:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Trip_data_source_shape()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var source = new TripDataSource(InMemoryDb(tenant), tenant);
        Assert.Equal(EntityTypes.Trip, source.Key);
        Assert.Equal("PlanDate", source.DateField);
        Assert.Equal("Id", source.IdField);
        Assert.Equal(DataFieldType.Date, source.Fields.Single(f => f.Key == "PlanDate").Type);
        Assert.Equal(DataFieldType.Bool, source.Fields.Single(f => f.Key == "OverStopLimit").Type);
        foreach (var key in new[] { "Code", "ZoneCode", "DriverName", "VehicleCode", "Status", "StatusCode", "StopCount", "EffectiveMaxStops", "TotalDistanceKm", "IsActive" })
            Assert.Contains(source.Fields, f => f.Key == key);

        Assert.Contains(source.Relations, r => r.Key == "Driver" && r.TargetSourceKey == EntityTypes.Driver && r.LocalField == "DriverId");
        Assert.Contains(source.Relations, r => r.Key == "Vehicle" && r.TargetSourceKey == EntityTypes.Vehicle && r.LocalField == "VehicleId");
        Assert.All(source.Relations, r => Assert.Contains(source.Fields, f => f.Key == r.LocalField));

        // Sin dinero en la fuente TRIP.
        Assert.DoesNotContain(source.Fields, f => f.IsMoney);
        Assert.DoesNotContain(source.Fields, f => f.Key.Contains("Amount", StringComparison.OrdinalIgnoreCase) || f.Key.Contains("Rate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Order_data_source_exposes_trip_driver_zone_and_exception()
    {
        var fields = FieldsOf(EntityTypes.TransportOrder).ToDictionary(f => f.Key, f => f.Type);
        Assert.Equal(DataFieldType.Number, fields["TripId"]);
        Assert.Equal(DataFieldType.Text, fields["TripCode"]);
        Assert.Equal(DataFieldType.Text, fields["TripStatusCode"]);
        Assert.Equal(DataFieldType.Text, fields["AssignedDriverCode"]);
        Assert.Equal(DataFieldType.Text, fields["AssignedDriverName"]);
        Assert.Equal(DataFieldType.Bool, fields["HasAssignedDriver"]);
        Assert.Equal(DataFieldType.Text, fields["DispatchZoneCode"]);
        Assert.Equal(DataFieldType.Bool, fields["IsException"]);

        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var source = new TransportOrderDataSource(InMemoryDb(tenant), new FakeLookups(), tenant);
        Assert.Contains(source.Relations, r => r.Key == "Trip" && r.TargetSourceKey == EntityTypes.Trip && r.LocalField == "TripId");
        // Los campos nuevos son solo identidad: ningún monto ni tarifa de chofer.
        Assert.DoesNotContain(source.Fields, f => f.Key.Contains("Rate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(source.Fields, f => f.Key.StartsWith("Trip", StringComparison.Ordinal) && f.IsMoney);
        Assert.DoesNotContain(source.Fields, f => f.Key.StartsWith("AssignedDriver", StringComparison.Ordinal) && f.IsMoney);
    }

    [Fact]
    public async Task Order_data_source_resolves_current_trip_assigned_driver_and_exception_by_value()
    {
        // Definición V5 del indicador 'Órdenes sin chofer asignado': sin ruta vigente con chofer y sin DriverTrip activo.
        // 'Órdenes en excepción' = ON_HOLD, PARTIAL o FAILED.
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        using var db = InMemoryDb(tenant);
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 1, Entity = StatusDomains.OrderStatus, InternalCode = OrderStatuses.Confirmed, LabelJson = "{\"es\":\"Confirmada\"}" },
            new StatusCode { StatusCodeId = 2, Entity = StatusDomains.OrderStatus, InternalCode = OrderStatuses.OnHold, LabelJson = "{\"es\":\"En espera\"}" },
            new StatusCode { StatusCodeId = 3, Entity = StatusDomains.TripStatus, InternalCode = TripStatuses.Planned, LabelJson = "{\"es\":\"Planificada\"}" });
        db.Drivers.AddRange(
            new Driver { DriverId = 10, TenantId = TenantId, EmployeeCode = "D1", FullName = "Ana", StatusCodeId = 1, IsActive = true },
            new Driver { DriverId = 11, TenantId = TenantId, EmployeeCode = "D2", FullName = "Beto", StatusCodeId = 1, IsActive = true },
            new Driver { DriverId = 12, TenantId = TenantId, EmployeeCode = "D3", FullName = "Carla", StatusCodeId = 1, IsActive = true });
        db.Trips.AddRange(
            new Trip { TripId = 100, TenantId = TenantId, Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26), DriverId = 10, StatusCodeId = 3, IsActive = true },
            new Trip { TripId = 101, TenantId = TenantId, Code = "2026-0002", PlanDate = new DateOnly(2026, 9, 26), StatusCodeId = 3, IsActive = true });
        TransportOrder O(int id, int status, bool special = false) => new()
        {
            TransportOrderId = id, TenantId = TenantId, ClientId = 1, OrderNumber = "O" + id, PackBatchNumber = "PB" + id,
            ClientInvoiceNumber = "F" + id, ServiceTypeLookupId = 1, StatusCodeId = status, IsSpecialDelivery = special,
            IsActive = true, CreatedAtUtc = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc).AddMinutes(id),
        };
        db.TransportOrders.AddRange(
            O(1, 1),                 // en ruta vigente con chofer
            O(2, 1, special: true),  // entrega especial: DriverTrip activo, sin ruta
            O(3, 1),                 // estuvo en una ruta (TripOrder no vigente) y tiene un DriverTrip inactivo: libre
            O(4, 1),                 // en ruta vigente SIN chofer: no cuenta como asignada
            O(5, 2));                // ON_HOLD, libre: en excepción
        db.TripOrders.AddRange(
            new TripOrder { TripOrderId = 1, TenantId = TenantId, TripId = 100, TransportOrderId = 1, IsCurrent = true },
            new TripOrder { TripOrderId = 2, TenantId = TenantId, TripId = 100, TransportOrderId = 3, IsCurrent = false },
            new TripOrder { TripOrderId = 3, TenantId = TenantId, TripId = 101, TransportOrderId = 4, IsCurrent = true });
        db.DriverTrips.AddRange(
            new DriverTrip { DriverTripId = 1, TenantId = TenantId, DriverId = 11, TransportOrderId = 2, TripDate = new DateOnly(2026, 9, 26), StatusCodeId = 1, IsActive = true },
            new DriverTrip { DriverTripId = 2, TenantId = TenantId, DriverId = 12, TransportOrderId = 3, TripDate = new DateOnly(2026, 9, 26), StatusCodeId = 1, IsActive = false });
        await db.SaveChangesAsync();

        var rows = (await new TransportOrderDataSource(db, new FakeLookups(), tenant).LoadAsync(new DataQuery(), default))
            .ToDictionary(r => (int)r["Id"]!);

        Assert.Equal(100, rows[1]["TripId"]);
        Assert.Equal("2026-0001", rows[1]["TripCode"]);
        Assert.Equal(TripStatuses.Planned, rows[1]["TripStatusCode"]);
        Assert.Equal("D1", rows[1]["AssignedDriverCode"]);
        Assert.Equal("Ana", rows[1]["AssignedDriverName"]);
        Assert.Equal(true, rows[1]["HasAssignedDriver"]);

        Assert.Null(rows[2]["TripCode"]);
        Assert.Equal("D2", rows[2]["AssignedDriverCode"]);
        Assert.Equal(true, rows[2]["HasAssignedDriver"]);

        Assert.Null(rows[3]["TripId"]);
        Assert.Null(rows[3]["AssignedDriverCode"]);
        Assert.Equal(false, rows[3]["HasAssignedDriver"]);

        Assert.Equal("2026-0002", rows[4]["TripCode"]);
        Assert.Null(rows[4]["AssignedDriverCode"]);
        Assert.Equal(false, rows[4]["HasAssignedDriver"]);

        Assert.Equal(new[] { false, false, false, false, true }, Enumerable.Range(1, 5).Select(i => (bool)rows[i]["IsException"]!));
    }

    [Fact]
    public async Task Trip_data_source_counts_current_route_stops_and_flags_over_stop_limit()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        using var db = InMemoryDb(tenant);
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 1, Entity = StatusDomains.TripStatus, InternalCode = TripStatuses.Dispatched, LabelJson = "{\"es\":\"Despachada\",\"en\":\"Dispatched\"}" },
            new StatusCode { StatusCodeId = 2, Entity = StatusDomains.RouteStatus, InternalCode = RouteStatuses.Active, LabelJson = "{\"es\":\"Activa\"}" },
            new StatusCode { StatusCodeId = 3, Entity = StatusDomains.RouteStatus, InternalCode = RouteStatuses.Archived, LabelJson = "{\"es\":\"Archivada\"}" },
            new StatusCode { StatusCodeId = 4, Entity = StatusDomains.RouteStopStatus, InternalCode = RouteStopStatuses.Pending, LabelJson = "{}" },
            new StatusCode { StatusCodeId = 5, Entity = StatusDomains.RouteStopStatus, InternalCode = RouteStopStatuses.Completed, LabelJson = "{}" });
        db.Drivers.Add(new Driver { DriverId = 10, TenantId = TenantId, EmployeeCode = "D1", FullName = "Ana", MaxStopsPerRoute = 2, StatusCodeId = 1, IsActive = true });
        db.Trips.AddRange(
            new Trip { TripId = 100, TenantId = TenantId, Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26), DriverId = 10, StatusCodeId = 1, IsActive = true },
            new Trip { TripId = 200, TenantId = 2, Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26), StatusCodeId = 1, IsActive = true }); // otro tenant
        db.Routes.AddRange(
            new TripRoute { RouteId = 1, TripId = 100, Version = 1, IsActive = false, StatusCodeId = 3 },   // archivada: no cuenta
            new TripRoute { RouteId = 2, TripId = 100, Version = 2, IsActive = true, StatusCodeId = 2 },
            new TripRoute { RouteId = 3, TripId = 200, Version = 1, IsActive = true, StatusCodeId = 2 });
        db.RouteStops.AddRange(
            new RouteStop { RouteStopId = 1, RouteId = 1, OrderStopId = 1, Sequence = 1, StatusCodeId = 4 },
            new RouteStop { RouteStopId = 2, RouteId = 2, OrderStopId = 1, Sequence = 1, StatusCodeId = 5 },
            new RouteStop { RouteStopId = 3, RouteId = 2, OrderStopId = 2, Sequence = 2, StatusCodeId = 4 },
            new RouteStop { RouteStopId = 4, RouteId = 2, OrderStopId = 3, Sequence = 3, StatusCodeId = 4 },
            new RouteStop { RouteStopId = 5, RouteId = 3, OrderStopId = 9, Sequence = 1, StatusCodeId = 4 });
        await db.SaveChangesAsync();

        var source = new TripDataSource(db, tenant);
        var rows = await source.LoadAsync(new DataQuery(), default);
        var row = Assert.Single(rows); // el filtro de tenant excluye la ruta del tenant 2
        Assert.Equal(100, row["Id"]);
        Assert.Equal("2026-0001", row["Code"]);
        Assert.Equal(TripStatuses.Dispatched, row["StatusCode"]);
        Assert.Equal(2, row["RouteVersion"]);
        Assert.Equal(RouteStatuses.Active, row["RouteStatusCode"]);
        Assert.Equal(3, row["StopCount"]);
        Assert.Equal(1, row["CompletedStops"]);
        Assert.Equal(2, row["PendingStops"]);
        Assert.Equal(2, row["EffectiveMaxStops"]);
        Assert.Equal(true, row["OverStopLimit"]);
        Assert.Equal("D1", row["DriverCode"]);
        Assert.Equal(false, row["IsEditable"]);

        // Rango sobre PlanDate: desde inclusivo, hasta exclusivo.
        Assert.Single(await source.LoadAsync(new DataQuery { FromUtc = new DateTime(2026, 9, 26), ToUtc = new DateTime(2026, 9, 27) }, default));
        Assert.Empty(await source.LoadAsync(new DataQuery { FromUtc = new DateTime(2026, 9, 27), ToUtc = new DateTime(2026, 9, 28) }, default));
        Assert.Empty(await source.LoadAsync(new DataQuery { Ids = new[] { 200 } }, default));
        // Lote 15: el motor pasa el rango también en días LOCALES (FromDay / ToDayExclusive); una ruta del 26 entra con el día 26
        // aunque el instante UTC del rango sea las 04:00Z (medianoche de Puerto Rico).
        Assert.Single(await source.LoadAsync(new DataQuery { FromUtc = new DateTime(2026, 9, 26, 4, 0, 0), ToUtc = new DateTime(2026, 9, 27, 4, 0, 0),
            FromDay = new DateOnly(2026, 9, 26), ToDayExclusive = new DateOnly(2026, 9, 27) }, default));
        Assert.Empty(await source.LoadAsync(new DataQuery { FromDay = new DateOnly(2026, 9, 27), ToDayExclusive = new DateOnly(2026, 9, 28) }, default));
    }

    // ================================================================ Lote 6 — Inventario y almacén

    private static readonly string[] WmsSources =
    {
        EntityTypes.Warehouse, EntityTypes.Product, EntityTypes.StockBalance, EntityTypes.InventoryTransaction, EntityTypes.Receipt,
        EntityTypes.WarehouseTask, EntityTypes.PickBatch, EntityTypes.CycleCount,
    };

    private static Teikem.Infrastructure.Analytics.IDataSource WmsSource(string key)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var db = InMemoryDb(tenant);
        var lookups = new FakeLookups();
        var reads = new Teikem.Infrastructure.Services.InventoryReadService(db, tenant, lookups, TenantClock.Default);
        return key switch
        {
            EntityTypes.Warehouse => new WarehouseDataSource(db, tenant),
            EntityTypes.Product => new ProductDataSource(db, lookups),
            EntityTypes.StockBalance => new StockBalanceDataSource(db, reads),
            EntityTypes.InventoryTransaction => new InventoryTransactionDataSource(db, reads),
            EntityTypes.Receipt => new ReceiptDataSource(db, tenant, lookups),
            EntityTypes.WarehouseTask => new WarehouseTaskDataSource(db, tenant, lookups),
            EntityTypes.PickBatch => new PickBatchDataSource(db, tenant),
            EntityTypes.CycleCount => new CycleCountDataSource(db, tenant),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    [Fact]
    public async Task Lote6_old_movements_view_is_renamed_once_without_duplicates()
    {
        // Tenant sembrado con la versión L874 ('Movimientos por tipo', un solo campo): el seeder la corrige a la L887.
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        db.ReportDefinitions.Add(new Teikem.Domain.Analytics.ReportDefinition
        {
            TenantId = TenantId, Name = SystemAnalyticsSeeder.MovementsByTypeReportNameV1, IsSystem = true,
            GroupJson = SystemAnalyticsSeeder.MovementsByTypeGroupV1,
        });
        await db.SaveChangesAsync();
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        var views = await db.ReportDefinitions.AsNoTracking().Where(r => r.Name.StartsWith("Movimientos por tipo")).ToListAsync();
        var view = Assert.Single(views);
        Assert.Equal(SystemAnalyticsSeeder.MovementsByTypeReportName, view.Name);
        Assert.Equal(SystemAnalyticsSeeder.MovementsByTypeGroup, view.GroupJson);
    }

    [Fact]
    public async Task Lote6_system_content_is_seeded_in_the_warehouse_module()
    {
        var (_, reports, indicators, charts) = await SeedAsync(WmsSources, null);
        foreach (var name in new[] { "Inventario", "Inventario bajo mínimo", "Productos por cliente dueño", "Kárdex de movimientos", "Movimientos por tipo y producto",
                     "Ajustes de inventario", "Próximos a vencer", "Recepciones con diferencia" })
            Assert.Contains(name, reports);
        foreach (var (name, source) in new[]
                 {
                     ("Productos activos", EntityTypes.Product), ("Inventario disponible", EntityTypes.StockBalance),
                     ("Movimientos registrados", EntityTypes.InventoryTransaction), ("Valor de inventario a costo", EntityTypes.StockBalance),
                     ("Productos bajo mínimo", EntityTypes.Product), ("Valor de inventario a venta", EntityTypes.StockBalance),
                     ("Tareas de almacén pendientes", EntityTypes.WarehouseTask),
                 })
            Assert.Contains((name, source), indicators);
        Assert.Equal(9, indicators.Count);   // 7 del Lote 6 + 'Unidades recibidas' y 'Conteos con diferencia' (Lote 7A)
        foreach (var name in new[] { "Valor de inventario por categoría", "Disponible por categoría", "Movimientos por tipo", "Movimientos por usuario",
                     "Productos por categoría", "Movimientos por día" })
            Assert.Contains(charts, c => c.Name == name);
        Assert.Equal(7, charts.Count);   // 6 del Lote 6 + 'Movimientos de inventario por tipo' (Lote 7A)

        // Forma exacta de las vistas del mock (L874) y de los indicadores de dinero, todos en el módulo WAREHOUSE.
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        // Maestro L887 (amplía la L874): 'Movimientos por tipo y producto', dos campos de agrupación y fila de totales.
        var byType = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == SystemAnalyticsSeeder.MovementsByTypeReportName);
        Assert.Equal("Movimientos por tipo y producto", byType.Name);
        Assert.Equal(SystemAnalyticsSeeder.MovementsByTypeGroup, byType.GroupJson);
        using (var group = JsonDocument.Parse(byType.GroupJson!))
        {
            var by = group.RootElement.GetProperty("by").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(new[] { "TxnType", "Sku" }, by);
            Assert.True(group.RootElement.GetProperty("totals").GetBoolean());
        }
        Assert.False(await db.ReportDefinitions.AnyAsync(r => r.Name == SystemAnalyticsSeeder.MovementsByTypeReportNameV1));
        Assert.Contains("\"fn\":\"SUM\",\"field\":\"Quantity\"", byType.GroupJson);
        var byOwner = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == "Productos por cliente dueño");
        Assert.Equal(SystemAnalyticsSeeder.ActiveProductsFilter, byOwner.FilterJson);
        Assert.Equal(new[] { "OwnerName", "Sku", "Name", "QtyOnHand", "QtyAvailable" }, JsonSerializer.Deserialize<string[]>(byOwner.ColumnsJson!));
        var below = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == "Inventario bajo mínimo");
        Assert.Equal(SystemAnalyticsSeeder.ProductsBelowMinFilter, below.FilterJson);

        var wh = lookups.CodeOf((await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == "Valor de inventario a costo")).BusinessModuleLookupId);
        Assert.Equal(BusinessModules.Warehouse, wh);
        Assert.True((await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == "Valor de inventario a costo")).IsMoney);
        Assert.True((await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == "Valor de inventario a venta")).IsMoney);
        var movements = await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == "Movimientos registrados");
        Assert.Equal(DateRangeModes.Last7, lookups.CodeOf(movements.DateRangeModeLookupId!.Value));   // maestro L948
        var costChart = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == "Valor de inventario por categoría");
        Assert.True(costChart.IsMoney);
        Assert.Equal("CostValue", costChart.FieldKey);
        Assert.Equal(AggregateFns.Sum, lookups.CodeOf(costChart.AggregateFnLookupId));
        var donut = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == "Movimientos por tipo");
        Assert.Equal(DateRangeModes.Last30, lookups.CodeOf(donut.DateRangeModeLookupId!.Value));
        Assert.Equal(ChartTypes.Donut, lookups.CodeOf(donut.ChartTypeLookupId));
        // Los gráficos anteriores siguen con COUNT en Operación (el helper ganó parámetros opcionales).
        var old = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == "Rutas por estatus");
        Assert.Equal(AggregateFns.Count, lookups.CodeOf(old.AggregateFnLookupId));
        Assert.Equal(BusinessModules.Operations, lookups.CodeOf(old.BusinessModuleLookupId));
        Assert.Null(old.FieldKey);
    }

    [Fact]
    public async Task Every_lote6_seeded_field_exists_in_its_data_source()
    {
        var (uses, _, _, _) = await SeedAsync(WmsSources, null);
        Assert.NotEmpty(uses);
        var known = WmsSources.ToDictionary(s => s, s => WmsSource(s).Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var missing = uses.Where(u => !known[u.Source].Contains(u.Field)).Select(u => $"{u.Source} · {u.Where}: '{u.Field}'").Distinct().ToList();
        Assert.True(missing.Count == 0, "Campos sembrados que la fuente no expone:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Wms_data_sources_have_the_documented_fields_date_field_and_money()
    {
        var expected = new Dictionary<string, (string? DateField, string[] Fields, string[] Money)>
        {
            [EntityTypes.Warehouse] = (null, new[] { "Id", "PublicId", "Code", "Name", "City", "Status", "StatusCode", "IsActive", "ZoneCount", "BinCount", "ActiveBinCount", "DockCount", "QtyOnHand",
                "ReceivingMode", "ReceivingModeCode" }, Array.Empty<string>()),   // Lote 16
            [EntityTypes.Product] = (null, new[] { "Id", "PublicId", "Sku", "Name", "CategoryId", "Category", "OwnerClientId", "OwnerName", "IsOwn", "BaseUom", "PackUom", "PackQty", "TrackingType", "Barcode",
                "PurchaseCost", "SalePrice", "QtyOnHand", "QtyReserved", "QtyAvailable", "CostValue", "SaleValue", "MinQty", "IsBelowMin", "IsActive" }, new[] { "PurchaseCost", "SalePrice", "CostValue", "SaleValue" }),
            [EntityTypes.StockBalance] = (null, new[] { "Id", "WarehouseId", "WarehouseCode", "ZoneCode", "ZoneType", "BinCode", "ProductId", "Sku", "ProductName", "Category", "OwnerName",
                "IsOwn", "LotNumber", "ExpiryDate", "DaysToExpiry", "QtyOnHand", "QtyReserved", "QtyAvailable", "CostValue", "SaleValue", "UpdatedAtUtc" }, new[] { "CostValue", "SaleValue" }),
            [EntityTypes.InventoryTransaction] = ("CreatedAtUtc", new[] { "Id", "CreatedAtUtc", "Date", "TxnType", "TxnTypeCode", "ProductId", "Sku", "ProductName", "Category",
                "Quantity", "SignedQuantity", "Units", "FromWarehouse", "FromBin", "ToWarehouse", "ToBin", "Position", "LotNumber", "SerialNumber", "RefEntity", "RefId", "RefLabel",
                "Reason", "ReasonCode", "UserName" }, Array.Empty<string>()),
            [EntityTypes.Receipt] = ("ReceivedAtUtc", new[] { "Id", "PublicId", "Number", "Type", "TypeCode", "Origin", "WarehouseCode", "SupplierName", "ClientName",
                "PurchaseOrderNumber", "Carrier", "Reference", "Status", "StatusCode", "LineCount", "ExpectedQty", "ReceivedQty", "VarianceQty", "HasVariance", "ReceivedCost", "CreatedAtUtc",
                "ReceivedAtUtc", "ReceivingMode", "ReceivingModeCode" }, new[] { "ReceivedCost" }),
            [EntityTypes.WarehouseTask] = ("CreatedAtUtc", new[] { "Id", "CreatedAtUtc", "Type", "TypeCode", "Status", "StatusCode", "Priority", "WarehouseCode", "Sku",
                "Quantity", "FromBin", "ToBin", "AssignedTo", "CompletedAtUtc", "AgeHours", "RefLabel" }, Array.Empty<string>()),
            [EntityTypes.PickBatch] = ("CollectedAtUtc", new[] { "Id", "PublicId", "Number", "CollectedAtUtc", "Status", "StatusCode", "WarehouseCode", "LineCount", "TotalQty",
                "TotalCost", "PackBatchNumber", "OrderNumber", "ClientInvoiceNumber", "ClientName", "PackedAtUtc", "IsActive" }, new[] { "TotalCost" }),
            [EntityTypes.CycleCount] = ("ReconciledAtUtc", new[] { "Id", "Number", "WarehouseId", "WarehouseCode", "Status", "StatusCode", "LineCount", "CountedLines",
                "VarianceLines", "NetVariance", "HasVariance", "CreatedAtUtc", "ReconciledAtUtc" }, Array.Empty<string>()),
        };
        foreach (var (key, (dateField, fields, money)) in expected)
        {
            var source = WmsSource(key);
            Assert.Equal(key, source.Key);
            Assert.Equal(dateField, source.DateField);
            Assert.Equal(fields.OrderBy(f => f, StringComparer.Ordinal), source.Fields.Select(f => f.Key).OrderBy(f => f, StringComparer.Ordinal));
            Assert.Equal(money.OrderBy(f => f, StringComparer.Ordinal), source.Fields.Where(f => f.IsMoney).Select(f => f.Key).OrderBy(f => f, StringComparer.Ordinal));
        }
    }
    // ================================================================ Lote 7A — Pulso de almacén

    [Fact]
    public async Task Lote7A_warehouse_pulse_indicators_and_chart_are_seeded_for_everyone_in_the_warehouse_module()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        var received = await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == SystemAnalyticsSeeder.ReceivedUnitsIndicatorName);
        Assert.Equal("Unidades recibidas", received.Name);
        Assert.Equal(EntityTypes.InventoryTransaction, received.DataSourceKey);
        Assert.Equal("Quantity", received.FieldKey);
        Assert.Equal(AggregateFns.Sum, lookups.CodeOf(received.AggregateFnLookupId));
        Assert.Equal(SystemAnalyticsSeeder.ReceiptMovementsFilter, received.FilterJson);
        Assert.Contains("\"value\":\"" + InventoryTxnTypes.Receipt + "\"", received.FilterJson);
        Assert.Contains("\"value\":\"" + AdjustmentReasons.ReceiptVariance + "\"", received.FilterJson);   // D4: neto = lo recibido
        Assert.Equal(DateRangeModes.Last7, lookups.CodeOf(received.DateRangeModeLookupId!.Value));

        var counts = await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == SystemAnalyticsSeeder.CountsWithVarianceIndicatorName);
        Assert.Equal("Conteos con diferencia", counts.Name);
        Assert.Equal(EntityTypes.CycleCount, counts.DataSourceKey);
        Assert.Null(counts.FieldKey);
        Assert.Equal(AggregateFns.Count, lookups.CodeOf(counts.AggregateFnLookupId));
        Assert.Contains("\"value\":\"" + CycleCountStatuses.ReconciledVariance + "\"", counts.FilterJson);   // Lote 14 (D7)
        Assert.Equal(DateRangeModes.Last30, lookups.CodeOf(counts.DateRangeModeLookupId!.Value));

        // 'Productos bajo mínimo' ya existía (Lote 6) con el mismo filtro que la vista 'Inventario bajo mínimo'; 'Productos
        // activos' tampoco se duplica.
        var below = await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == "Productos bajo mínimo");
        var belowView = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == "Inventario bajo mínimo");
        Assert.Equal(belowView.FilterJson, below.FilterJson);
        Assert.Equal(EntityTypes.Product, below.DataSourceKey);
        Assert.Equal(1, await db.IndicatorDefinitions.CountAsync(i => i.Name == "Productos activos"));

        var chart = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == SystemAnalyticsSeeder.MovementsByTypeChartName);
        Assert.Equal("Movimientos de inventario por tipo", chart.Name);
        Assert.Equal(EntityTypes.InventoryTransaction, chart.DataSourceKey);
        Assert.Equal("TxnType", chart.GroupByField);
        Assert.Equal("Units", chart.FieldKey);   // Lote 15 (D12): unidades en positivo (antes Quantity con signo)
        Assert.Equal(AggregateFns.Sum, lookups.CodeOf(chart.AggregateFnLookupId));
        Assert.Equal(ChartTypes.Bar, lookups.CodeOf(chart.ChartTypeLookupId));
        Assert.Equal(DateRangeModes.Last7, lookups.CodeOf(chart.DateRangeModeLookupId!.Value));
        Assert.False(chart.IsMoney);

        // Todos: en Pulso, módulo WAREHOUSE y visibles a toda la organización; los indicadores de sistema y el gráfico (Lote 15)
        // de la compañía, sin dueño.
        foreach (var d in new Teikem.Domain.Analytics.AnalyticsDefinitionBase[] { received, counts, below, chart })
        {
            Assert.Equal(d != chart, d.IsSystem);
            Assert.Null(d.OwnerUserId);
            Assert.True(d.ShowInPulse);
            Assert.Equal(BusinessModules.Warehouse, lookups.CodeOf(d.BusinessModuleLookupId));
            Assert.Equal(ReportVisibilities.Tenant, lookups.CodeOf(d.VisibilityLookupId));
        }
    }

    [Fact]
    public async Task Lote7A_reseeding_is_idempotent_by_name()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        foreach (var name in new[] { SystemAnalyticsSeeder.ReceivedUnitsIndicatorName, SystemAnalyticsSeeder.CountsWithVarianceIndicatorName, "Productos bajo mínimo", "Productos activos" })
            Assert.Equal(1, await db.IndicatorDefinitions.CountAsync(i => i.TenantId == TenantId && i.Name == name));
        Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.TenantId == TenantId && c.Name == SystemAnalyticsSeeder.MovementsByTypeChartName));
    }

    [Fact]
    public async Task Lote7A_received_units_v1_filter_is_corrected_once_and_custom_filter_is_kept()
    {
        // Tenant sembrado con la primera versión (solo RECEIPT): el seeder la corrige; un filtro personalizado se respeta.
        const string custom = "{\"and\":[{\"field\":\"TxnTypeCode\",\"op\":\"eq\",\"value\":\"ADJUSTMENT\"}]}";
        foreach (var (initial, expected) in new[]
                 {
                     (SystemAnalyticsSeeder.ReceiptMovementsFilterV1, SystemAnalyticsSeeder.ReceiptMovementsFilter),
                     (custom, custom),
                 })
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
            var lookups = new FakeLookups();
            using var db = InMemoryDb(tenant);
            db.IndicatorDefinitions.Add(new Teikem.Domain.Analytics.IndicatorDefinition
            {
                TenantId = TenantId, Name = SystemAnalyticsSeeder.ReceivedUnitsIndicatorName, IsSystem = true,
                DataSourceKey = EntityTypes.InventoryTransaction, FieldKey = "Quantity", FilterJson = initial,
            });
            await db.SaveChangesAsync();
            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

            var received = Assert.Single(await db.IndicatorDefinitions.AsNoTracking()
                .Where(i => i.Name == SystemAnalyticsSeeder.ReceivedUnitsIndicatorName).ToListAsync());
            Assert.Equal(expected, received.FilterJson);
        }
    }

    [Fact]
    public void Lote7A_cycle_count_data_source_is_registered()
    {
        // Sin la fuente registrada el indicador 'Conteos con diferencia' haría fallar el Pulso completo (registry.Get → 404).
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Teikem.Infrastructure.DependencyInjection.AddTeikemInfrastructure(services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var sources = services.Where(d => d.ServiceType == typeof(Teikem.Infrastructure.Analytics.IDataSource)).Select(d => d.ImplementationType).ToList();
        Assert.Contains(typeof(CycleCountDataSource), sources);
        Assert.Contains(typeof(InventoryTransactionDataSource), sources);
        Assert.Contains(typeof(ProductDataSource), sources);
    }

    [Fact]
    public async Task Lote7A_cycle_count_data_source_flags_variance_against_the_posted_balance()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        using var db = InMemoryDb(tenant);
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 1, Entity = StatusDomains.CycleCountStatus, InternalCode = CycleCountStatuses.Open, LabelJson = "{\"es\":\"Abierto\"}" },
            new StatusCode { StatusCodeId = 2, Entity = StatusDomains.CycleCountStatus, InternalCode = CycleCountStatuses.Reconciled, LabelJson = "{\"es\":\"Concordancia\",\"en\":\"Matched\"}" },
            new StatusCode { StatusCodeId = 3, Entity = StatusDomains.CycleCountStatus, InternalCode = CycleCountStatuses.ReconciledVariance, LabelJson = "{\"es\":\"Diferencia\",\"en\":\"Variance\"}" });
        db.Warehouses.Add(new Teikem.Domain.Wms.Warehouse { WarehouseId = 5, TenantId = TenantId, Code = "WH1", Name = "Central", StatusCodeId = 1, IsActive = true });
        var reconciledAt = new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc);
        Teikem.Domain.Wms.CycleCount Cc(int id, int status, DateTime? reconciled, bool active = true, int tenantId = TenantId) => new()
        {
            CycleCountId = id, TenantId = tenantId, WarehouseId = 5, Number = "CC-" + id.ToString("00000"), StatusCodeId = status,
            CreatedAtUtc = reconciledAt.AddDays(-1), ReconciledAtUtc = reconciled, IsActive = active,
        };
        db.CycleCounts.AddRange(
            Cc(1, 3, reconciledAt),                   // Diferencia, +2 y −2: neta 0 pero con diferencia
            Cc(2, 2, reconciledAt),                   // Concordancia; la foto difería pero el saldo asentado cuadró: sin diferencia
            Cc(3, 1, null),                           // abierto con captura distinta a la foto
            Cc(4, 3, reconciledAt, active: false),    // eliminado: no aparece
            Cc(5, 3, reconciledAt, tenantId: 2),      // otro tenant
            Cc(6, 3, reconciledAt));                  // Diferencia; cuadra pero con ajuste enlazado (sustitución de serie)
        Teikem.Domain.Wms.CycleCountLine L(int id, int cc, decimal system, decimal? counted, decimal? reconciled = null, long? adj = null) => new()
        {
            CycleCountLineId = id, CycleCountId = cc, WarehouseBinId = 1, ProductId = id, SystemQty = system, CountedQty = counted,
            ReconciledSystemQty = reconciled, AdjustmentTxnId = adj,
        };
        db.CycleCountLines.AddRange(
            L(1, 1, 10, 12, 10), L(2, 1, 5, 3, 5), L(3, 1, 7, 7, 7),
            L(4, 2, 10, 8, 8),
            L(5, 3, 4, 6), L(6, 3, 4, null),
            L(7, 5, 1, 9, 1),
            L(8, 6, 1, 1, 1, adj: 900));
        await db.SaveChangesAsync();

        var source = new CycleCountDataSource(db, tenant);
        var rows = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (int)r["Id"]!);
        Assert.Equal(new[] { 1, 2, 3, 6 }, rows.Keys.OrderBy(k => k));

        Assert.Equal(CycleCountStatuses.ReconciledVariance, rows[1]["StatusCode"]);
        Assert.Equal("Diferencia", rows[1]["Status"]);
        Assert.Equal(CycleCountStatuses.Reconciled, rows[2]["StatusCode"]);
        Assert.Equal("WH1", rows[1]["WarehouseCode"]);
        Assert.Equal(3, rows[1]["LineCount"]);
        Assert.Equal(2, rows[1]["VarianceLines"]);
        Assert.Equal(0m, rows[1]["NetVariance"]);
        Assert.Equal(true, rows[1]["HasVariance"]);

        Assert.Equal(0, rows[2]["VarianceLines"]);
        Assert.Equal(false, rows[2]["HasVariance"]);

        Assert.Equal(1, rows[3]["CountedLines"]);
        Assert.Equal(2m, rows[3]["NetVariance"]);
        Assert.Equal(true, rows[3]["HasVariance"]);

        // Cuadra, pero la línea tiene ajuste enlazado (maestro: 'o con ajuste enlazado'): cuenta como diferencia.
        Assert.Equal(1, rows[6]["VarianceLines"]);
        Assert.Equal(0m, rows[6]["NetVariance"]);
        Assert.Equal(true, rows[6]["HasVariance"]);

        // Rango sobre ReconciledAtUtc: el abierto (sin fecha) queda fuera; desde inclusivo, hasta exclusivo.
        var ranged = await source.LoadAsync(new DataQuery { FromUtc = reconciledAt, ToUtc = reconciledAt.AddSeconds(1) }, default);
        Assert.Equal(new[] { 1, 2, 6 }, ranged.Select(r => (int)r["Id"]!).OrderBy(i => i));
        Assert.Empty(await source.LoadAsync(new DataQuery { FromUtc = reconciledAt.AddSeconds(1) }, default));

        // El filtro sembrado de 'Conteos con diferencia' (Lote 14: estatus Diferencia) selecciona los conteos 1 y 6.
        Assert.Equal(new[] { 1, 6 }, rows.Values.Where(r => Teikem.Infrastructure.Dsl.RuleEvaluator.Matches(r, SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilter))
            .Select(r => (int)r["Id"]!).OrderBy(i => i));
    }

    // ================================================================ Lote 15 — Pulso del día

    [Fact]
    public async Task Lote15_the_two_warehouse_charts_are_seeded_as_company_charts_in_the_first_row()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        var value = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.SeedKey == ChartSeedKeys.InventoryValue);
        Assert.Equal((SystemAnalyticsSeeder.InventoryValueChartName, EntityTypes.StockBalance, "Category", "CostValue"), (value.Name, value.DataSourceKey, value.GroupByField, value.FieldKey));
        Assert.Equal((ChartTypes.Donut, AggregateFns.Sum), (lookups.CodeOf(value.ChartTypeLookupId), lookups.CodeOf(value.AggregateFnLookupId)));   // D10: dona (no PIE)
        Assert.True(value.IsMoney);
        Assert.Null(value.DateRangeModeLookupId);   // foto de ahora: sin rango
        Assert.Equal(1, value.SortOrder);

        var moves = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.SeedKey == ChartSeedKeys.MovementsByType);
        Assert.Equal((SystemAnalyticsSeeder.MovementsByTypeChartName, EntityTypes.InventoryTransaction, "TxnType", "Units"), (moves.Name, moves.DataSourceKey, moves.GroupByField, moves.FieldKey));
        Assert.Equal((ChartTypes.Bar, AggregateFns.Sum, DateRangeModes.Last7),
            (lookups.CodeOf(moves.ChartTypeLookupId), lookups.CodeOf(moves.AggregateFnLookupId), lookups.CodeOf(moves.DateRangeModeLookupId!.Value)));
        Assert.Equal(2, moves.SortOrder);
        Assert.Contains("positivo", moves.DescriptionJson);

        foreach (var c in new[] { value, moves })
        {
            Assert.False(c.IsSystem);            // de la compañía: editable con analytics.manage
            Assert.Null(c.OwnerUserId);
            Assert.True(c.IsActive && c.ShowInPulse);
            Assert.Equal(BusinessModules.Warehouse, lookups.CodeOf(c.BusinessModuleLookupId));
            Assert.Equal(ReportVisibilities.Tenant, lookups.CodeOf(c.VisibilityLookupId));
        }
        // Van antes que cualquier otro gráfico sembrado (el primero de fábrica tiene orden 10).
        Assert.True(await db.ChartDefinitions.Where(c => c.SeedKey == null).AllAsync(c => c.SortOrder > 2));
        // "Movimientos por tipo" (dona de 30 días) se queda como está (D15).
        Assert.True((await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == "Movimientos por tipo")).IsSystem);
    }

    [Fact]
    public async Task Lote15_renamed_or_deleted_company_charts_are_not_seeded_again()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        // Se renombra el de valor y se borra (soft delete) el de movimientos.
        var value = await db.ChartDefinitions.SingleAsync(c => c.SeedKey == ChartSeedKeys.InventoryValue);
        value.Name = "Mi valor de inventario";
        var moves = await db.ChartDefinitions.SingleAsync(c => c.SeedKey == ChartSeedKeys.MovementsByType);
        moves.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.SeedKey == ChartSeedKeys.InventoryValue));
        Assert.False(await db.ChartDefinitions.AnyAsync(c => c.Name == SystemAnalyticsSeeder.InventoryValueChartName));   // no reaparece
        Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.SeedKey == ChartSeedKeys.MovementsByType));
        Assert.False(await db.ChartDefinitions.AnyAsync(c => c.SeedKey == ChartSeedKeys.MovementsByType && c.IsActive));   // borrado: no vuelve
        Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.Name == SystemAnalyticsSeeder.MovementsByTypeChartName));
    }

    [Fact]
    public async Task Lote15_existing_system_charts_are_converted_once_keeping_an_organized_order()
    {
        foreach (var organized in new[] { false, true })
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
            var lookups = new FakeLookups();
            using var db = InMemoryDb(tenant);
            var bar = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Bar);
            var last30 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last30);
            Teikem.Domain.Analytics.ChartDefinition Old(string name, string source, string groupBy, string field, int sort, int? range) => new()
            {
                TenantId = TenantId, Name = name, DataSourceKey = source, GroupByField = groupBy, FieldKey = field, ChartTypeLookupId = bar, IsSystem = true,
                SortOrder = sort, DateRangeModeLookupId = range, ShowInPulse = true,
                DescriptionJson = "{\"es\":\"vieja\",\"en\":\"old\"}",
            };
            // Compañía sembrada antes del lote (sistema, barras, Quantity con signo). La compañía cambió el rango a 30 días.
            db.ChartDefinitions.AddRange(
                Old(SystemAnalyticsSeeder.InventoryValueChartName, EntityTypes.StockBalance, "Category", "CostValue", organized ? 30 : 90, null),
                Old(SystemAnalyticsSeeder.MovementsByTypeChartName, EntityTypes.InventoryTransaction, "TxnType", "Quantity", organized ? 90 : 96, last30));
            if (organized)   // Organizar guardó índice × 10 desde 0
                db.ChartDefinitions.Add(Old("Cambios por acción", EntityTypes.AuditLog, "Action", "x", 0, null));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

            var value = Assert.Single(await db.ChartDefinitions.AsNoTracking().Where(c => c.Name == SystemAnalyticsSeeder.InventoryValueChartName).ToListAsync());
            Assert.Equal((false, (int?)null, ChartSeedKeys.InventoryValue, ChartTypes.Donut, "CostValue"),
                (value.IsSystem, value.OwnerUserId, value.SeedKey, lookups.CodeOf(value.ChartTypeLookupId), value.FieldKey));
            Assert.Equal(organized ? 30 : 1, value.SortOrder);
            var moves = Assert.Single(await db.ChartDefinitions.AsNoTracking().Where(c => c.Name == SystemAnalyticsSeeder.MovementsByTypeChartName).ToListAsync());
            Assert.Equal((false, (int?)null, ChartSeedKeys.MovementsByType, ChartTypes.Bar, "Units"),
                (moves.IsSystem, moves.OwnerUserId, moves.SeedKey, lookups.CodeOf(moves.ChartTypeLookupId), moves.FieldKey));
            Assert.Equal(organized ? 90 : 2, moves.SortOrder);
            Assert.Equal(DateRangeModes.Last30, lookups.CodeOf(moves.DateRangeModeLookupId!.Value));   // el rango de la compañía se conserva
            Assert.Contains("positivo", moves.DescriptionJson);
            Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.SeedKey == ChartSeedKeys.MovementsByType));
        }
    }

    [Fact]
    public async Task Lote15_pending_discrepancies_indicator_is_seeded_off_the_pulse()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        var d = Assert.Single(await db.IndicatorDefinitions.AsNoTracking().Where(i => i.Name == "Descuadres pendientes").ToListAsync());
        Assert.Equal((EntityTypes.InventoryDiscrepancy, (string?)null, AggregateFns.Count), (d.DataSourceKey, d.FieldKey, lookups.CodeOf(d.AggregateFnLookupId)));
        Assert.Equal(SystemAnalyticsSeeder.PendingDiscrepanciesFilter, d.FilterJson);
        Assert.Equal(DateRangeModes.All, lookups.CodeOf(d.DateRangeModeLookupId!.Value));   // estado actual (la fuente tiene DateField)
        Assert.False(d.ShowInPulse);   // D16: "Necesita tu atención" ya los muestra
        Assert.True(d.IsSystem);
        Assert.Equal(BusinessModules.Warehouse, lookups.CodeOf(d.BusinessModuleLookupId));

        // Sus campos existen en la fuente INVENTORY_DISCREPANCY.
        var t2 = new TenantContext { TenantId = TenantId, UserId = 1 };
        var db2 = InMemoryDb(t2);
        var source = new InventoryDiscrepancyDataSource(db2, t2, new FakeLookups(),
            new Teikem.Infrastructure.Services.InventoryReadService(db2, t2, new FakeLookups(), TenantClock.Default));
        Assert.Equal("DetectedAtUtc", source.DateField);
        foreach (var f in JsonFields(d.FilterJson)) Assert.Contains(source.Fields, x => x.Key == f);
    }

    [Fact]
    public async Task Lote14_counts_with_variance_v1_filter_is_corrected_once_and_custom_filter_is_kept()
    {
        // Compañía sembrada con el filtro del Lote 7A (RECONCILED + HasVariance): el seeder lo pasa a RECONCILED_VARIANCE; un
        // filtro personalizado se respeta.
        const string custom = "{\"and\":[{\"field\":\"HasVariance\",\"op\":\"isTrue\"}]}";
        foreach (var (initial, expected) in new[]
                 {
                     (SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilterV1, SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilter),
                     (custom, custom),
                 })
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
            var lookups = new FakeLookups();
            using var db = InMemoryDb(tenant);
            db.IndicatorDefinitions.Add(new Teikem.Domain.Analytics.IndicatorDefinition
            {
                TenantId = TenantId, Name = SystemAnalyticsSeeder.CountsWithVarianceIndicatorName, IsSystem = true,
                DataSourceKey = EntityTypes.CycleCount, FilterJson = initial,
            });
            await db.SaveChangesAsync();
            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
            await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

            var counts = Assert.Single(await db.IndicatorDefinitions.AsNoTracking()
                .Where(i => i.Name == SystemAnalyticsSeeder.CountsWithVarianceIndicatorName).ToListAsync());
            Assert.Equal(expected, counts.FilterJson);
        }
        Assert.Equal("{\"and\":[{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"RECONCILED_VARIANCE\"}]}", SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilter);
    }

    // ================================================================ Lote 29 — Rentas (R3)

    private static readonly string[] RentalSources = { EntityTypes.Rental, EntityTypes.RentalReturn, EntityTypes.RentalProcess };

    private static Teikem.Infrastructure.Analytics.IDataSource RentalSource(string key)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var db = InMemoryDb(tenant);
        var lookups = new FakeLookups();
        return key switch
        {
            EntityTypes.Rental => new RentalDataSource(db, lookups, tenant),
            EntityTypes.RentalReturn => new RentalReturnDataSource(db, lookups, tenant),
            EntityTypes.RentalProcess => new RentalProcessDataSource(db, lookups, tenant),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    [Fact]
    public async Task Lote29_rental_reports_indicators_and_chart_are_seeded_in_the_warehouse_module_off_the_pulse()
    {
        var (_, reports, indicators, charts) = await SeedAsync(RentalSources, null);
        Assert.Equal(new[]
        {
            RentalAnalyticsRules.OnRentByClientReportName, RentalAnalyticsRules.DueSoonReportName, RentalAnalyticsRules.OverdueReportName,
            RentalAnalyticsRules.ReturnsByReasonReportName, RentalAnalyticsRules.InProcessReportName,
        }.OrderBy(n => n, StringComparer.Ordinal), reports.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { (RentalAnalyticsRules.DueSoonIndicatorName, EntityTypes.Rental), (RentalAnalyticsRules.OverdueIndicatorName, EntityTypes.Rental) },
            indicators.OrderBy(i => i.Name, StringComparer.Ordinal));
        Assert.Equal((RentalAnalyticsRules.ReturnsByReasonChartName, EntityTypes.RentalReturn), Assert.Single(charts));
        Assert.Equal(("Rentas por vencer (7 días)", "Rentas vencidas", "Devoluciones de renta por motivo"),
            (RentalAnalyticsRules.DueSoonIndicatorName, RentalAnalyticsRules.OverdueIndicatorName, RentalAnalyticsRules.ReturnsByReasonChartName));

        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        foreach (var name in new[] { RentalAnalyticsRules.DueSoonIndicatorName, RentalAnalyticsRules.OverdueIndicatorName })
        {
            var i = await db.IndicatorDefinitions.AsNoTracking().SingleAsync(x => x.Name == name);
            Assert.Equal((AggregateFns.Count, DateRangeModes.All, BusinessModules.Warehouse, ReportVisibilities.Tenant),
                (lookups.CodeOf(i.AggregateFnLookupId), lookups.CodeOf(i.DateRangeModeLookupId!.Value), lookups.CodeOf(i.BusinessModuleLookupId), lookups.CodeOf(i.VisibilityLookupId)));
            Assert.True(i.IsSystem && i.IsActive);
            Assert.False(i.ShowInPulse);   // como 'Descuadres pendientes' (D16): "Necesita tu atención" ya las muestra
            Assert.Null(i.FieldKey);
        }
        Assert.Equal(RentalAnalyticsRules.DueSoonFilter, (await db.IndicatorDefinitions.AsNoTracking().SingleAsync(x => x.Name == RentalAnalyticsRules.DueSoonIndicatorName)).FilterJson);
        Assert.Equal(RentalAnalyticsRules.OverdueFilter, (await db.IndicatorDefinitions.AsNoTracking().SingleAsync(x => x.Name == RentalAnalyticsRules.OverdueIndicatorName)).FilterJson);

        var chart = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.Name == RentalAnalyticsRules.ReturnsByReasonChartName);
        Assert.Equal(("Reason", (string?)null, AggregateFns.Count, ChartTypes.Donut, DateRangeModes.Last30, BusinessModules.Warehouse),
            (chart.GroupByField, chart.FieldKey, lookups.CodeOf(chart.AggregateFnLookupId), lookups.CodeOf(chart.ChartTypeLookupId),
             lookups.CodeOf(chart.DateRangeModeLookupId!.Value), lookups.CodeOf(chart.BusinessModuleLookupId)));
        Assert.True(chart.IsSystem);
        Assert.False(chart.ShowInPulse);

        var byClient = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == RentalAnalyticsRules.OnRentByClientReportName);
        Assert.Equal((EntityTypes.Rental, RentalAnalyticsRules.OnRentFilter, RentalAnalyticsRules.OnRentByClientGroup, "[]"),
            (lookups.CodeOf(byClient.BaseEntityTypeLookupId), byClient.FilterJson, byClient.GroupJson, byClient.ColumnsJson));
        var byReason = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == RentalAnalyticsRules.ReturnsByReasonReportName);
        Assert.Equal((EntityTypes.RentalReturn, RentalAnalyticsRules.ReturnsByReasonGroup), (lookups.CodeOf(byReason.BaseEntityTypeLookupId), byReason.GroupJson));
        var inProcess = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == RentalAnalyticsRules.InProcessReportName);
        Assert.Equal((EntityTypes.RentalProcess, RentalAnalyticsRules.OpenProcessFilter), (lookups.CodeOf(inProcess.BaseEntityTypeLookupId), inProcess.FilterJson));
        var dueView = await db.ReportDefinitions.AsNoTracking().SingleAsync(r => r.Name == RentalAnalyticsRules.DueSoonReportName && r.BaseEntityTypeLookupId == byClient.BaseEntityTypeLookupId);
        Assert.Equal(RentalAnalyticsRules.DueSoonFilter, dueView.FilterJson);
        Assert.Contains("\"PickupDate\"", dueView.SortJson);
        Assert.All(await db.ReportDefinitions.AsNoTracking().Where(r => r.BaseEntityTypeLookupId == byClient.BaseEntityTypeLookupId).ToListAsync(),
            r => Assert.True(r.IsSystem && r.OwnerUserId == null && lookups.CodeOf(r.VisibilityLookupId) == ReportVisibilities.Tenant));
    }

    [Fact]
    public async Task Every_lote29_seeded_field_exists_in_its_rental_data_source()
    {
        var (uses, _, _, _) = await SeedAsync(RentalSources, null);
        Assert.NotEmpty(uses);
        var known = RentalSources.ToDictionary(s => s, s => RentalSource(s).Fields.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var missing = uses.Where(u => !known[u.Source].Contains(u.Field)).Select(u => $"{u.Source} · {u.Where}: '{u.Field}'").Distinct().ToList();
        Assert.True(missing.Count == 0, "Campos sembrados que la fuente no expone:\n" + string.Join("\n", missing));
        // Los campos de los filtros tienen el tipo que el filtro compara.
        var rental = RentalSource(EntityTypes.Rental).Fields.ToDictionary(f => f.Key, f => f.Type);
        Assert.Equal((DataFieldType.Bool, DataFieldType.Number, DataFieldType.Bool), (rental["IsOpen"], rental["DaysToPickup"], rental["IsOverdue"]));
    }

    [Fact]
    public async Task Lote29_reseeding_is_idempotent_and_does_not_depend_on_the_rentals_module()
    {
        // El seeder no lee los módulos de la compañía (aquí no hay ninguno encendido: Rentas "apagado") y siembra igual; el API oculta
        // el contenido con el módulo apagado (RentalAnalyticsTests). Sembrar dos veces no duplica.
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        Assert.False(await db.TenantModules.AnyAsync());
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);

        foreach (var name in new[] { RentalAnalyticsRules.DueSoonIndicatorName, RentalAnalyticsRules.OverdueIndicatorName })
            Assert.Equal(1, await db.IndicatorDefinitions.CountAsync(i => i.TenantId == TenantId && i.Name == name));
        Assert.Equal(1, await db.ChartDefinitions.CountAsync(c => c.TenantId == TenantId && c.Name == RentalAnalyticsRules.ReturnsByReasonChartName));
        foreach (var name in new[] { RentalAnalyticsRules.OnRentByClientReportName, RentalAnalyticsRules.DueSoonReportName, RentalAnalyticsRules.OverdueReportName,
                     RentalAnalyticsRules.ReturnsByReasonReportName, RentalAnalyticsRules.InProcessReportName })
            Assert.Equal(1, await db.ReportDefinitions.CountAsync(r => r.TenantId == TenantId && r.Name == name));

        // Un indicador de sistema que la compañía apagó en su Pulso o al que le cambió el rango no se pisa al resembrar.
        var due = await db.IndicatorDefinitions.SingleAsync(i => i.Name == RentalAnalyticsRules.DueSoonIndicatorName);
        due.ShowInPulse = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        Assert.True((await db.IndicatorDefinitions.AsNoTracking().SingleAsync(i => i.Name == RentalAnalyticsRules.DueSoonIndicatorName)).ShowInPulse);
    }

    [Fact]
    public async Task Lote29_the_sql_seed_gives_the_same_rental_content_to_existing_companies()
    {
        var seed = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")).Replace("\r\n", "\n");
        var start = seed.IndexOf("Lote 29 (Rentas R3) — reportes, indicadores y gráfico de rentas en las compañías YA CREADAS", StringComparison.Ordinal);
        Assert.True(start > 0, "Falta el bloque del Lote 29 en logistica-db-seed.sql");
        var block = seed[start..];
        block = block[..block.IndexOf("\nGO", StringComparison.Ordinal)];

        // Mismos filtros, agrupaciones, columnas y nombres que SystemAnalyticsSeeder (las compañías nuevas).
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var lookups = new FakeLookups();
        using var db = InMemoryDb(tenant);
        await new SystemAnalyticsSeeder(db, tenant, lookups).SeedForTenantAsync(TenantId, default);
        foreach (var i in await db.IndicatorDefinitions.AsNoTracking().Where(i => i.DataSourceKey == EntityTypes.Rental).ToListAsync())
        {
            Assert.Contains($"N'{i.Name}'", block);
            Assert.Contains($"N'{i.FilterJson}', {i.SortOrder})", block);
        }
        var chart = await db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.DataSourceKey == EntityTypes.RentalReturn);
        Assert.Contains($"N'{chart.Name}'", block);
        Assert.Contains("'RENTAL_RETURN', 'Reason', NULL, @L29Count, @L29Donut, NULL, @L29Wh, 0, 1, NULL, @L29Tenant, @L29Last30, 0, 100, 1", block);
        var rentalTypes = RentalSources.Select(s => lookups.GetIdAsync(LookupDomains.EntityType, s).Result).ToList();
        var reports = await db.ReportDefinitions.AsNoTracking().Where(r => rentalTypes.Contains(r.BaseEntityTypeLookupId)).ToListAsync();
        Assert.Equal(5, reports.Count);
        foreach (var r in reports)
        {
            Assert.Contains($"N'{r.Name}'", block);
            Assert.Contains($"N'{r.ColumnsJson}'", block);
            if (r.FilterJson is not null) Assert.Contains($"N'{r.FilterJson}'", block);
            if (r.GroupJson is not null) Assert.Contains($"N'{r.GroupJson}'", block);
            if (r.SortJson is not null) Assert.Contains($"N'{r.SortJson}'", block);
        }
        // Solo compañías con contenido de análisis sembrado e idempotente por nombre (como el bloque del Lote 15).
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM dbo.IndicatorDefinition i WHERE i.TenantId = t.TenantId AND i.Name = v.Name)", block);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM dbo.ChartDefinition c WHERE c.TenantId = t.TenantId AND c.Name = N'Devoluciones de renta por motivo')", block);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM dbo.ReportDefinition r WHERE r.TenantId = t.TenantId AND r.BaseEntityTypeLookupId = v.EntityTypeLookupId AND r.Name = v.Name)", block);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(block, @"WHERE EXISTS \(SELECT 1 FROM dbo\.\w+Definition x WHERE x\.TenantId = t\.TenantId AND x\.IsSystem = 1\)").Count);
    }
}

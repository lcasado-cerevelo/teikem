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
    }

    // ================================================================ Lote 6 — Inventario y almacén

    private static readonly string[] WmsSources =
    {
        EntityTypes.Warehouse, EntityTypes.Product, EntityTypes.StockBalance, EntityTypes.InventoryTransaction, EntityTypes.Receipt,
        EntityTypes.WarehouseTask, EntityTypes.PickBatch,
    };

    private static Teikem.Infrastructure.Analytics.IDataSource WmsSource(string key)
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
        var db = InMemoryDb(tenant);
        var lookups = new FakeLookups();
        var reads = new Teikem.Infrastructure.Services.InventoryReadService(db, tenant, lookups);
        return key switch
        {
            EntityTypes.Warehouse => new WarehouseDataSource(db, tenant),
            EntityTypes.Product => new ProductDataSource(db, lookups),
            EntityTypes.StockBalance => new StockBalanceDataSource(db, reads),
            EntityTypes.InventoryTransaction => new InventoryTransactionDataSource(db, reads),
            EntityTypes.Receipt => new ReceiptDataSource(db, tenant, lookups),
            EntityTypes.WarehouseTask => new WarehouseTaskDataSource(db, tenant, lookups),
            EntityTypes.PickBatch => new PickBatchDataSource(db, tenant),
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
        Assert.Equal(7, indicators.Count);
        foreach (var name in new[] { "Valor de inventario por categoría", "Disponible por categoría", "Movimientos por tipo", "Movimientos por usuario",
                     "Productos por categoría", "Movimientos por día" })
            Assert.Contains(charts, c => c.Name == name);
        Assert.Equal(6, charts.Count);

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
            [EntityTypes.Warehouse] = (null, new[] { "Id", "PublicId", "Code", "Name", "City", "Status", "StatusCode", "IsActive", "ZoneCount", "BinCount", "ActiveBinCount", "DockCount", "QtyOnHand" }, Array.Empty<string>()),
            [EntityTypes.Product] = (null, new[] { "Id", "PublicId", "Sku", "Name", "CategoryId", "Category", "OwnerClientId", "OwnerName", "IsOwn", "BaseUom", "TrackingType", "Barcode",
                "PurchaseCost", "SalePrice", "QtyOnHand", "QtyReserved", "QtyAvailable", "CostValue", "SaleValue", "MinQty", "IsBelowMin", "IsActive" }, new[] { "PurchaseCost", "SalePrice", "CostValue", "SaleValue" }),
            [EntityTypes.StockBalance] = (null, new[] { "Id", "WarehouseId", "WarehouseCode", "ZoneCode", "ZoneType", "BinCode", "ProductId", "Sku", "ProductName", "Category", "OwnerName",
                "IsOwn", "LotNumber", "ExpiryDate", "DaysToExpiry", "QtyOnHand", "QtyReserved", "QtyAvailable", "CostValue", "SaleValue", "UpdatedAtUtc" }, new[] { "CostValue", "SaleValue" }),
            [EntityTypes.InventoryTransaction] = ("CreatedAtUtc", new[] { "Id", "CreatedAtUtc", "Date", "TxnType", "TxnTypeCode", "ProductId", "Sku", "ProductName", "Category",
                "Quantity", "SignedQuantity", "FromWarehouse", "FromBin", "ToWarehouse", "ToBin", "Position", "LotNumber", "SerialNumber", "RefEntity", "RefId", "RefLabel",
                "Reason", "ReasonCode", "UserName" }, Array.Empty<string>()),
            [EntityTypes.Receipt] = ("ReceivedAtUtc", new[] { "Id", "PublicId", "Number", "Type", "TypeCode", "Origin", "WarehouseCode", "SupplierName", "ClientName",
                "PurchaseOrderNumber", "Status", "StatusCode", "LineCount", "ExpectedQty", "ReceivedQty", "VarianceQty", "HasVariance", "ReceivedCost", "CreatedAtUtc",
                "ReceivedAtUtc" }, new[] { "ReceivedCost" }),
            [EntityTypes.WarehouseTask] = ("CreatedAtUtc", new[] { "Id", "CreatedAtUtc", "Type", "TypeCode", "Status", "StatusCode", "Priority", "WarehouseCode", "Sku",
                "Quantity", "FromBin", "ToBin", "AssignedTo", "CompletedAtUtc", "AgeHours", "RefLabel" }, Array.Empty<string>()),
            [EntityTypes.PickBatch] = ("CollectedAtUtc", new[] { "Id", "PublicId", "Number", "CollectedAtUtc", "Status", "StatusCode", "WarehouseCode", "LineCount", "TotalQty",
                "TotalCost", "PackBatchNumber", "OrderNumber", "ClientInvoiceNumber", "ClientName", "PackedAtUtc", "IsActive" }, new[] { "TotalCost" }),
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
}

using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P0: las constantes de Trips y rutas coinciden con los literales de Diseño/logistica-db-seed.sql y
/// Diseño/logistica-db-estructura.sql, y el catálogo de permisos suma trips.view y trips.scan (54). Si alguien cambia un
/// código aquí o en los scripts sin el otro, esta prueba lo delata.
/// </summary>
public class TripCatalogTests
{
    /// <summary>Raíz del repositorio (donde vive Teikem.sln), subiendo desde la carpeta de salida de las pruebas.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró Teikem.sln subiendo desde la salida de las pruebas.");
    }

    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(RepoRoot(), "Diseño", "logistica-db-seed.sql")));
    private static readonly Lazy<string> Structure = new(() => File.ReadAllText(Path.Combine(RepoRoot(), "Diseño", "logistica-db-estructura.sql")));

    private static bool SeedHasStatus(string domain, string code) => Seed.Value.Contains($"('{domain}','{code}',", StringComparison.Ordinal);
    private static bool SeedHasLookup(string domain, string code) => Seed.Value.Contains($"('{domain}','{code}',", StringComparison.Ordinal);

    [Fact]
    public void Status_domains_and_codes_match_the_seed()
    {
        Assert.Equal("TripStatus", StatusDomains.TripStatus);
        Assert.Equal("RouteStatus", StatusDomains.RouteStatus);
        Assert.Equal("RouteStopStatus", StatusDomains.RouteStopStatus);
        Assert.Equal("OptimizationRunStatus", StatusDomains.OptimizationRunStatus);

        var trip = new[] { TripStatuses.Draft, TripStatuses.Planned, TripStatuses.Dispatched, TripStatuses.InProgress, TripStatuses.Completed, TripStatuses.Cancelled };
        Assert.Equal(new[] { "DRAFT", "PLANNED", "DISPATCHED", "IN_PROGRESS", "COMPLETED", "CANCELLED" }, trip);
        Assert.All(trip, c => Assert.True(SeedHasStatus(StatusDomains.TripStatus, c), $"TripStatus {c}"));

        var route = new[] { RouteStatuses.Draft, RouteStatuses.Optimized, RouteStatuses.Active, RouteStatuses.Archived };
        Assert.Equal(new[] { "DRAFT", "OPTIMIZED", "ACTIVE", "ARCHIVED" }, route);
        Assert.All(route, c => Assert.True(SeedHasStatus(StatusDomains.RouteStatus, c), $"RouteStatus {c}"));

        var stop = new[] { RouteStopStatuses.Pending, RouteStopStatuses.OnTheWay, RouteStopStatuses.Arrived, RouteStopStatuses.Completed, RouteStopStatuses.Failed };
        Assert.Equal(new[] { "PENDING", "ON_THE_WAY", "ARRIVED", "COMPLETED", "FAILED" }, stop);
        Assert.All(stop, c => Assert.True(SeedHasStatus(StatusDomains.RouteStopStatus, c), $"RouteStopStatus {c}"));

        var run = new[] { OptimizationRunStatuses.Pending, OptimizationRunStatuses.Ok, OptimizationRunStatuses.Error };
        Assert.Equal(new[] { "PENDING", "OK", "ERROR" }, run);
        Assert.All(run, c => Assert.True(SeedHasStatus(StatusDomains.OptimizationRunStatus, c), $"OptimizationRunStatus {c}"));
    }

    [Fact]
    public void Lookup_domains_and_codes_match_the_seed()
    {
        Assert.Equal("ZoneMatchType", LookupDomains.ZoneMatchType);
        Assert.Equal("OptimizerEngine", LookupDomains.OptimizerEngine);
        Assert.Equal("GeocodeAccuracy", LookupDomains.GeocodeAccuracy);

        var zone = new[] { ZoneMatchTypes.PostalCode, ZoneMatchTypes.PostalRange, ZoneMatchTypes.Municipality, ZoneMatchTypes.Polygon };
        Assert.Equal(new[] { "POSTAL_CODE", "POSTAL_RANGE", "MUNICIPALITY", "POLYGON" }, zone);
        Assert.All(zone, c => Assert.True(SeedHasLookup(LookupDomains.ZoneMatchType, c), $"ZoneMatchType {c}"));

        var engines = new[] { OptimizerEngines.Vroom, OptimizerEngines.OrTools, OptimizerEngines.Manual, OptimizerEngines.Heuristic };
        Assert.Equal(new[] { "VROOM", "ORTOOLS", "MANUAL", "HEURISTIC" }, engines);
        Assert.All(engines, c => Assert.True(SeedHasLookup(LookupDomains.OptimizerEngine, c), $"OptimizerEngine {c}"));

        var accuracy = new[] { GeocodeAccuracies.Exact, GeocodeAccuracies.ZipCentroid, GeocodeAccuracies.CityCentroid, GeocodeAccuracies.Manual };
        Assert.Equal(new[] { "EXACT", "ZIP_CENTROID", "CITY_CENTROID", "MANUAL" }, accuracy);
        Assert.All(accuracy, c => Assert.True(SeedHasLookup(LookupDomains.GeocodeAccuracy, c), $"GeocodeAccuracy {c}"));

        Assert.Equal("EDIT_TRIP", Capabilities.EditTrip);
        Assert.True(SeedHasLookup(LookupDomains.Capability, Capabilities.EditTrip));
    }

    [Fact]
    public void Entity_types_are_seeded()
    {
        Assert.Equal("TRIP", EntityTypes.Trip);
        Assert.Equal("ROUTE", EntityTypes.Route);
        Assert.Equal("ROUTE_STOP", EntityTypes.RouteStop);
        Assert.Equal("OPTIMIZATION_RUN", EntityTypes.OptimizationRun);
        foreach (var code in new[] { EntityTypes.Trip, EntityTypes.Route, EntityTypes.RouteStop, EntityTypes.OptimizationRun })
            Assert.True(SeedHasLookup(LookupDomains.EntityType, code), $"EntityType {code}");
    }

    [Fact]
    public void Permission_catalog_has_54_codes_with_trips_view_and_scan()
    {
        Assert.Equal(54, PermissionCatalog.All.Count);
        Assert.Equal(54, PermissionCatalog.All.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var view = Assert.Single(PermissionCatalog.All, p => p.Code == "trips.view");
        Assert.Equal(("TRIPS", "Ver rutas y despacho", "View trips & dispatch"), (view.Category, view.LabelEs, view.LabelEn));
        var scan = Assert.Single(PermissionCatalog.All, p => p.Code == "trips.scan");
        Assert.Equal(("TRIPS", "Escanear salida (Outbound)", "Scan outbound"), (scan.Category, scan.LabelEs, scan.LabelEn));
        Assert.Equal("trips.view", PermissionCatalog.TripsView);
        Assert.Equal("trips.scan", PermissionCatalog.TripsScan);

        // Espejo en el seed: #P y el PRINT final.
        Assert.Contains("('trips.view','TRIPS','Ver rutas y despacho','View trips & dispatch')", Seed.Value);
        Assert.Contains("('trips.scan','TRIPS','Escanear salida (Outbound)','Scan outbound')", Seed.Value);
        Assert.Contains("permisos (54)", Seed.Value);

        // Cada permiso del catálogo aparece en el seed.
        foreach (var p in PermissionCatalog.All)
            Assert.True(Seed.Value.Contains($"('{p.Code}','", StringComparison.Ordinal), $"Permiso {p.Code} no está en el seed");
    }

    [Fact]
    public void Role_templates_receive_trips_view_and_scan()
    {
        var t = PermissionCatalog.RoleTemplates;
        Assert.Contains(PermissionCatalog.TripsView, t["Dispatcher"]);
        Assert.Contains(PermissionCatalog.TripsScan, t["Dispatcher"]);
        Assert.Contains(PermissionCatalog.TripsView, t["WarehouseOperator"]);
        Assert.Contains(PermissionCatalog.TripsScan, t["WarehouseOperator"]);
        Assert.DoesNotContain(PermissionCatalog.TripsPlan, t["WarehouseOperator"]);
        Assert.Equal(new[] { "trips.view" }, t["ReadOnly"].Where(c => c.StartsWith("trips.", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain(t["Driver"], c => c.StartsWith("trips.", StringComparison.Ordinal));
        Assert.Contains(PermissionCatalog.TripsView, t["TenantAdmin"]);
        Assert.Contains(PermissionCatalog.TripsScan, t["TenantAdmin"]);

        // Espejo en #RP del seed.
        foreach (var (role, code) in new[] { ("Dispatcher", "trips.view"), ("Dispatcher", "trips.scan"), ("WarehouseOperator", "trips.view"),
                     ("WarehouseOperator", "trips.scan"), ("ReadOnly", "trips.view") })
            Assert.Contains($"('{role}','{code}')", Seed.Value);

        // Propagación a roles clonados.
        var newCodes = new HashSet<string> { "trips.view", "trips.scan" };
        Assert.Equal(new[] { "trips.view", "trips.scan" }, PermissionCatalog.CodesToPropagate("WarehouseOperator", newCodes, new HashSet<string>()));
        Assert.Equal(new[] { "trips.view" }, PermissionCatalog.CodesToPropagate("ReadOnly", newCodes, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Driver", newCodes, new HashSet<string>()));
    }

    [Theory]
    [InlineData("TRIP", "trips.view", "trips.plan")]
    [InlineData("ROUTE", "trips.view", "trips.plan")]
    [InlineData("ROUTE_STOP", "trips.view", "trips.plan")]
    public void Owner_permissions_of_the_trip_entity_types(string entityType, string read, string write)
    {
        Assert.Equal(read, PermissionCatalog.OwnerReadPermission[entityType]);
        Assert.Equal(write, PermissionCatalog.OwnerWritePermission[entityType]);
    }

    [Fact]
    public void Optimization_run_is_read_only_for_owners()
    {
        Assert.Equal("trips.view", PermissionCatalog.OwnerReadPermission[EntityTypes.OptimizationRun]);
        Assert.False(PermissionCatalog.OwnerWritePermission.ContainsKey(EntityTypes.OptimizationRun));
    }

    [Fact]
    public void Trip_number_kind_is_known_and_allowed_by_the_check()
    {
        Assert.Equal("TRIP", NumberKinds.Trip);
        Assert.True(NumberingRules.IsKnownKind("TRIP"));
        Assert.False(NumberingRules.IsKnownKind("ROUTE"));
        Assert.Matches(new Regex(@"CK_NumberSequence_Kind CHECK \(Kind IN \([^)]*'TRIP'[^)]*\)\)"), Structure.Value);
    }

    [Fact]
    public void Seed_has_edit_trip_capability_and_lateral_entries()
    {
        var seed = Seed.Value;
        var capability = Block(seed, "3E) STATUS CAPABILITY");
        Assert.Contains("et.InternalCode='TRIP'", capability);
        Assert.Contains("s.InternalCode IN ('DISPATCHED','IN_PROGRESS','COMPLETED','CANCELLED')", capability);
        Assert.Contains("c.InternalCode='EDIT_TRIP'", capability);
        Assert.Contains("VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0)", capability);

        var lateral = Block(seed, "3F) STATUS LATERAL ENTRY");
        Assert.Contains("et.InternalCode='TRIP'  AND lat.Entity='TripStatus'  AND lat.InternalCode='CANCELLED'", lateral);
        Assert.Contains("frm.InternalCode IN ('DRAFT','PLANNED')", lateral);
        Assert.Contains("et.InternalCode='ROUTE' AND lat.Entity='RouteStatus' AND lat.InternalCode='ARCHIVED'", lateral);
        Assert.Contains("frm.InternalCode IN ('DRAFT','OPTIMIZED')", lateral);
        Assert.Contains("MERGE dbo.StatusLateralEntry", lateral);
    }

    [Fact]
    public void Structure_declares_the_lote5_guards()
    {
        var sql = Structure.Value;
        Assert.Contains("OptimizationRunId INT IDENTITY", sql);
        Assert.DoesNotContain("OptimizationRunId BIGINT", sql);
        Assert.Contains("CREATE INDEX IX_LocationPing_Driver ON dbo.DriverLocationPing(TenantId, DriverId, CapturedAtUtc)", sql);
        Assert.Contains("CONSTRAINT UQ_Trip_IdTenant UNIQUE (TripId, TenantId)", sql);
        Assert.Contains("CONSTRAINT UQ_DispatchZone_IdTenant UNIQUE (DispatchZoneId, TenantId)", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_TripOrder_Current ON dbo.TripOrder(TransportOrderId) WHERE IsCurrent = 1", sql);
        Assert.Contains("CREATE UNIQUE INDEX UX_Route_Trip_Active ON dbo.Route(TripId) WHERE IsActive = 1", sql);
        Assert.Contains("CONSTRAINT UQ_Route_Version UNIQUE (TripId, Version)", sql);
        Assert.Contains("CONSTRAINT UQ_DispatchZoneMember UNIQUE (DispatchZoneId, MatchTypeLookupId, MatchValue)", sql);
        Assert.Contains("FOREIGN KEY (TripId, TenantId) REFERENCES dbo.Trip(TripId, TenantId)", sql);
        Assert.Contains("FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId)", sql);
        Assert.DoesNotContain("IX_Route_Trip ON", sql);

        // Orden de capas: UQ_Trip_IdTenant antes de TripOrder y OptimizationRun; FK_Trip_DispatchZone después de DispatchZone.
        Assert.True(sql.IndexOf("UQ_Trip_IdTenant", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE dbo.TripOrder", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("UQ_Trip_IdTenant", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE dbo.OptimizationRun", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("CREATE TABLE dbo.DispatchZone (", StringComparison.Ordinal) < sql.IndexOf("ADD CONSTRAINT FK_Trip_DispatchZone", StringComparison.Ordinal));
    }

    /// <summary>Texto del bloque del seed que empieza en el título dado (hasta el siguiente bloque de comentario).</summary>
    private static string Block(string seed, string title)
    {
        var start = seed.IndexOf(title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No está el bloque '{title}' en el seed.");
        var end = seed.IndexOf("/* ----", start + title.Length, StringComparison.Ordinal);
        return end < 0 ? seed[start..] : seed[start..end];
    }
}

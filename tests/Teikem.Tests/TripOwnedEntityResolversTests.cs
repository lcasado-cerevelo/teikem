using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;
using OptimizationRun = Teikem.Domain.Trips.OptimizationRun;
using RouteStop = Teikem.Domain.Trips.RouteStop;
using Trip = Teikem.Domain.Trips.Trip;
using TripRoute = Teikem.Domain.Trips.Route;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 — resolvers de pertenencia de rutas (asociaciones polimórficas ContactPoint / CustomFieldValue, maestro L123):
/// TRIP, ROUTE y ROUTE_STOP existen solo para el tenant dueño (Route y RouteStop no tienen TenantId: se alcanzan a través
/// de db.Trips ya filtrado, BOLA por id hijo); OPTIMIZATION_RUN está cerrado (siempre false) aunque la corrida exista.
/// Mismo almacén InMemory visto desde dos tenants.
/// </summary>
public class TripOwnedEntityResolversTests
{
    private const int TenantA = 1, TenantB = 2;
    private const int TripA = 100, TripB = 200, RouteA = 10, RouteB = 20, StopA = 1000, StopB = 2000, RunA = 5;

    private static TeikemDbContext Db(string name, int tenantId)
        => new(new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase(name, b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new TenantContext { TenantId = tenantId, UserId = 1 });

    private static async Task<string> SeedAsync()
    {
        var name = "trip-resolvers-" + Guid.NewGuid();
        await using var db = Db(name, TenantA);
        db.Trips.AddRange(
            new Trip { TripId = TripA, PublicId = Guid.NewGuid(), TenantId = TenantA, Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26), StatusCodeId = 1, IsActive = true },
            new Trip { TripId = TripB, PublicId = Guid.NewGuid(), TenantId = TenantB, Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26), StatusCodeId = 1, IsActive = true });
        db.Routes.AddRange(
            new TripRoute { RouteId = RouteA, TripId = TripA, Version = 1, IsActive = true, StatusCodeId = 1 },
            new TripRoute { RouteId = RouteB, TripId = TripB, Version = 1, IsActive = true, StatusCodeId = 1 });
        db.RouteStops.AddRange(
            new RouteStop { RouteStopId = StopA, RouteId = RouteA, OrderStopId = 1, Sequence = 1, StatusCodeId = 1 },
            new RouteStop { RouteStopId = StopB, RouteId = RouteB, OrderStopId = 2, Sequence = 1, StatusCodeId = 1 });
        db.OptimizationRuns.Add(new OptimizationRun
        {
            OptimizationRunId = RunA, TenantId = TenantA, TripId = TripA, RouteId = RouteA, EngineLookupId = 1, StatusCodeId = 1,
            RequestJson = "{}", StartedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return name;
    }

    private static IOwnedEntityResolver[] Resolvers(TeikemDbContext db) => new IOwnedEntityResolver[]
    {
        new TripOwnedEntityResolver(db),
        new RouteOwnedEntityResolver(db),
        new RouteStopOwnedEntityResolver(db),
        new ClosedOwnedEntityResolver(EntityTypes.OptimizationRun),
    };

    private static async Task<bool> ExistsAsync(TeikemDbContext db, string entityType, int id)
        => await Resolvers(db).Single(r => r.EntityTypeCode == entityType).ExistsInTenantAsync(id, default);

    [Fact]
    public async Task Trip_route_and_route_stop_exist_only_for_the_owner_tenant()
    {
        var name = await SeedAsync();

        await using (var a = Db(name, TenantA))
        {
            Assert.True(await ExistsAsync(a, EntityTypes.Trip, TripA));
            Assert.True(await ExistsAsync(a, EntityTypes.Route, RouteA));
            Assert.True(await ExistsAsync(a, EntityTypes.RouteStop, StopA));
            Assert.False(await ExistsAsync(a, EntityTypes.Trip, TripB));
            Assert.False(await ExistsAsync(a, EntityTypes.Route, RouteB));
            Assert.False(await ExistsAsync(a, EntityTypes.RouteStop, StopB));
            Assert.False(await ExistsAsync(a, EntityTypes.Trip, 999999));
        }

        await using (var b = Db(name, TenantB))
        {
            Assert.False(await ExistsAsync(b, EntityTypes.Trip, TripA));
            Assert.False(await ExistsAsync(b, EntityTypes.Route, RouteA));
            Assert.False(await ExistsAsync(b, EntityTypes.RouteStop, StopA));
            Assert.True(await ExistsAsync(b, EntityTypes.Trip, TripB));
            Assert.True(await ExistsAsync(b, EntityTypes.Route, RouteB));
            Assert.True(await ExistsAsync(b, EntityTypes.RouteStop, StopB));
        }
    }

    [Fact]
    public async Task Optimization_run_resolver_is_closed_even_for_an_existing_run_of_the_tenant()
    {
        var name = await SeedAsync();
        await using var a = Db(name, TenantA);
        Assert.True(await a.OptimizationRuns.AsNoTracking().AnyAsync(r => r.OptimizationRunId == RunA));
        Assert.False(await ExistsAsync(a, EntityTypes.OptimizationRun, RunA));
    }
}

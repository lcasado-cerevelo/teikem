using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 / P0: todo EntityType con permiso de dueño (OwnerReadPermission ∪ OwnerWritePermission) tiene un
/// IOwnedEntityResolver. Sin resolver, CustomFieldService omite la verificación de pertenencia y la ruta polimórfica
/// (contactos, campos personalizados, historial) queda abierta a ids de otro tenant — el hueco que tenía PORTAL_USER
/// (Lote 2). Se comprueba por reflexión sobre el ensamblado y sobre el contenedor real de DependencyInjection.
/// </summary>
public class OwnedEntityResolverCoverageTests
{
    /// <summary>Resolvers con constructor no inyectable: se registran con factory en DependencyInjection.</summary>
    private static readonly string[] ClosedResolverCodes = { EntityTypes.DriverRate, EntityTypes.FleetDocument };

    private static HashSet<string> OwnerCodes()
        => PermissionCatalog.OwnerReadPermission.Keys.Concat(PermissionCatalog.OwnerWritePermission.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static TeikemDbContext InMemoryDb(ITenantContext tenant)
        => new(new DbContextOptionsBuilder<TeikemDbContext>().UseInMemoryDatabase("resolver-coverage-" + Guid.NewGuid()).Options, tenant);

    /// <summary>Instancia por reflexión (ActivatorUtilities) todas las implementaciones del ensamblado + las cerradas.</summary>
    private static List<IOwnedEntityResolver> AllResolversByReflection()
    {
        var tenant = new TenantContext { TenantId = 1, UserId = 1 };
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton(InMemoryDb(tenant));
        using var sp = services.BuildServiceProvider();

        var types = typeof(IOwnedEntityResolver).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOwnedEntityResolver).IsAssignableFrom(t))
            .ToList();
        var result = new List<IOwnedEntityResolver>();
        foreach (var t in types)
        {
            if (t == typeof(ClosedOwnedEntityResolver)) continue;
            result.Add((IOwnedEntityResolver)ActivatorUtilities.CreateInstance(sp, t));
        }
        result.AddRange(ClosedResolverCodes.Select(c => new ClosedOwnedEntityResolver(c)));
        return result;
    }

    [Fact]
    public void Every_entity_type_with_owner_permission_has_a_resolver()
    {
        var covered = AllResolversByReflection().Select(r => r.EntityTypeCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = OwnerCodes().Where(c => !covered.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "EntityTypes con permiso de dueño sin IOwnedEntityResolver: " + string.Join(", ", missing));
        Assert.Contains(EntityTypes.PortalUser, covered);
    }

    [Fact]
    public void No_entity_type_has_two_resolvers()
    {
        var duplicated = AllResolversByReflection().GroupBy(r => r.EntityTypeCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicated);
    }

    [Fact]
    public async Task Closed_resolver_always_answers_not_found()
    {
        foreach (var code in ClosedResolverCodes)
        {
            var r = new ClosedOwnedEntityResolver(code);
            Assert.Equal(code, r.EntityTypeCode);
            Assert.False(await r.ExistsInTenantAsync(1, CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------- contenedor real (DependencyInjection)

    private static ServiceProvider BuildContainer()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registered_resolvers_cover_every_owner_entity_type_once()
    {
        using var sp = BuildContainer();
        using var scope = sp.CreateScope();
        var codes = scope.ServiceProvider.GetServices<IOwnedEntityResolver>().Select(r => r.EntityTypeCode).ToList();
        var missing = OwnerCodes().Where(c => !codes.Contains(c, StringComparer.OrdinalIgnoreCase)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Sin resolver registrado: " + string.Join(", ", missing));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Fleet_services_effects_and_data_sources_resolve_from_the_container()
    {
        using var sp = BuildContainer();
        using var scope = sp.CreateScope();
        var p = scope.ServiceProvider;
        foreach (var t in new[]
                 {
                     typeof(VehicleService), typeof(VehicleDocumentService), typeof(DriverService), typeof(DriverDocumentService),
                     typeof(DispatchZoneService), typeof(FleetDocumentService), typeof(IFleetAvailabilityService), typeof(MaintenanceScheduleService),
                     typeof(MaintenanceWorkOrderService), typeof(FuelLogService), typeof(DriverRateService), typeof(DriverPayPolicyService),
                     typeof(IDriverRateResolver), typeof(DriverTripService), typeof(SpecialDeliveryDispatchService), typeof(OrderService),
                 })
            Assert.NotNull(p.GetRequiredService(t));

        var effects = p.GetServices<IStatusTransitionEffect>().Select(e => e.GetType().Name).ToList();
        foreach (var name in new[] { "VehicleStatusEffect", "DriverStatusEffect", "DriverRatesRetirementEffect", "WorkOrderStatusEffect", "DriverTripOrderEffect", "DriverTripStatusEffect" })
            Assert.Contains(name, effects);

        var sources = p.GetRequiredService<IDataSourceRegistry>();
        foreach (var key in new[] { EntityTypes.Vehicle, EntityTypes.Driver, EntityTypes.WorkOrder, EntityTypes.FuelLog, EntityTypes.FleetDocument })
            Assert.True(sources.TryGet(key, out _), $"Falta la fuente {key}");
        // Sin fuentes de compensación: Análisis solo exige analytics.view.
        Assert.False(sources.TryGet(EntityTypes.DriverTrip, out _));
        Assert.False(sources.TryGet(EntityTypes.DriverRate, out _));
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P8: el módulo y el permiso de cada acción de Trips y rutas quedan fijados por reflexión. Quitar un
/// [RequireModule] o un [RequirePermission], cambiar el permiso de una acción o agregar una acción sin mapearla rompe CI
/// aunque el smoke no pase por ella. También se fija que ninguna acción reciba un 'tenantId' (sale del principal) y la
/// convención del lote: ningún archivo de src/Teikem.Api importa Teikem.Domain.Trips (choque con Microsoft.AspNetCore.Routing.Route).
/// </summary>
public class TripControllerSecurityTests
{
    private static readonly Type[] TripControllers =
    {
        typeof(TripsController), typeof(TripOrdersController), typeof(TripRoutesController), typeof(TripDispatchController),
        typeof(TripMonitorController), typeof(TripPlanningController), typeof(ScanController),
    };

    /// <summary>(controlador, acción) → permiso esperado. Exactamente uno por acción.</summary>
    private static readonly Dictionary<(Type Controller, string Action), string> Expected = new()
    {
        [(typeof(TripsController), nameof(TripsController.List))] = PermissionCatalog.TripsView,
        [(typeof(TripsController), nameof(TripsController.Get))] = PermissionCatalog.TripsView,
        [(typeof(TripsController), nameof(TripsController.Create))] = PermissionCatalog.TripsPlan,
        [(typeof(TripsController), nameof(TripsController.Update))] = PermissionCatalog.TripsPlan,
        [(typeof(TripsController), nameof(TripsController.Delete))] = PermissionCatalog.TripsPlan,
        [(typeof(TripsController), nameof(TripsController.ReassignZone))] = PermissionCatalog.TripsPlan,

        [(typeof(TripOrdersController), nameof(TripOrdersController.Unassigned))] = PermissionCatalog.TripsView,
        [(typeof(TripOrdersController), nameof(TripOrdersController.Add))] = PermissionCatalog.TripsPlan,
        [(typeof(TripOrdersController), nameof(TripOrdersController.Remove))] = PermissionCatalog.TripsPlan,

        [(typeof(TripRoutesController), nameof(TripRoutesController.Optimize))] = PermissionCatalog.TripsOptimize,
        [(typeof(TripRoutesController), nameof(TripRoutesController.Sequence))] = PermissionCatalog.TripsPlan,
        [(typeof(TripRoutesController), nameof(TripRoutesController.Location))] = PermissionCatalog.TripsPlan,
        [(typeof(TripRoutesController), nameof(TripRoutesController.Runs))] = PermissionCatalog.TripsView,

        [(typeof(TripDispatchController), nameof(TripDispatchController.Dispatchable))] = PermissionCatalog.TripsDispatch,
        [(typeof(TripDispatchController), nameof(TripDispatchController.Dispatch))] = PermissionCatalog.TripsDispatch,
        [(typeof(TripDispatchController), nameof(TripDispatchController.DispatchBatch))] = PermissionCatalog.TripsDispatch,
        [(typeof(TripDispatchController), nameof(TripDispatchController.Start))] = PermissionCatalog.TripsDispatch,

        [(typeof(TripMonitorController), nameof(TripMonitorController.Get))] = PermissionCatalog.TripsView,
        [(typeof(TripPlanningController), nameof(TripPlanningController.PlanDay))] = PermissionCatalog.TripsPlan,
        [(typeof(ScanController), nameof(ScanController.Outbound))] = PermissionCatalog.TripsScan,
    };

    public static IEnumerable<object[]> ActionMap() => Expected.Select(kv => new object[] { kv.Key.Controller, kv.Key.Action, kv.Value });

    private static List<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

    [Theory]
    [MemberData(nameof(ActionMap))]
    public void Each_trip_action_requires_exactly_its_permission(Type controller, string action, string permission)
    {
        var method = Actions(controller).SingleOrDefault(m => m.Name == action);
        Assert.True(method is not null, $"{controller.Name}.{action} no existe o no es una acción HTTP.");
        var policies = method!.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy).ToList();
        Assert.True(policies.Count == 1, $"{controller.Name}.{action} debe llevar exactamente un [RequirePermission] (tiene {policies.Count}).");
        Assert.True(RequirePermissionAttribute.Prefix + permission == policies[0],
            $"{controller.Name}.{action}: se esperaba '{permission}' y tiene '{policies[0]}'.");
    }

    [Fact]
    public void The_map_covers_every_public_action_of_the_trip_controllers()
    {
        var unmapped = TripControllers
            .SelectMany(c => Actions(c).Select(m => (c, m.Name)))
            .Where(k => !Expected.ContainsKey(k))
            .Select(k => $"{k.c.Name}.{k.Name}")
            .ToList();
        Assert.True(unmapped.Count == 0, "Acciones sin permiso esperado en el mapa: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Every_trip_controller_requires_the_ltl_ground_module_only()
    {
        foreach (var controller in TripControllers)
        {
            var modules = controller.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();
            Assert.True(modules.SequenceEqual(new[] { ModuleKeys.LtlGround }),
                $"{controller.Name} debe llevar exactamente [RequireModule(LTL_GROUND)] (tiene: {string.Join(", ", modules)}).");
        }
    }

    [Fact]
    public void No_trip_action_receives_a_tenant_id()
    {
        foreach (var controller in TripControllers)
        foreach (var action in Actions(controller))
        foreach (var p in action.GetParameters())
        {
            Assert.False(string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase),
                $"{controller.Name}.{action.Name} recibe un parámetro tenantId: el tenant sale del principal.");
            var props = p.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.DoesNotContain(props, pr => string.Equals(pr.Name, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void No_api_file_imports_the_trips_domain_namespace()
    {
        // El patrón se arma por partes para que este archivo no contenga la directiva literal (los chequeos por grep lo verían).
        var forbidden = "using Teikem.Domain." + "Trips;";
        var api = Path.Combine(TripCatalogTests.RepoRoot(), "src", "Teikem.Api");
        var offenders = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(forbidden))
            .Select(f => Path.GetRelativePath(api, f))
            .ToList();
        Assert.True(offenders.Count == 0, "Archivos de la API que importan Teikem.Domain.Trips: " + string.Join(", ", offenders));
    }
}

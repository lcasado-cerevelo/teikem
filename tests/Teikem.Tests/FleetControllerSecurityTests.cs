using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Hallazgo de revisión (Lote 4): el módulo y el permiso de cada acción de flota, choferes y pago quedan fijados por reflexión.
/// Quitar un [RequireModule] o un [RequirePermission], o cambiar el permiso de una acción (p. ej. dejar una escritura de
/// tarifas con driverpay.view), rompe CI aunque el smoke no pase por esa acción. Separación flota/compensación (R8):
/// lectura con *.view, escritura con fleet.manage / fleet.maintenance / driverpay.manage.
/// </summary>
public class FleetControllerSecurityTests
{
    /// <summary>(controlador, módulo, permiso de lectura (GET), permiso de escritura (resto)).</summary>
    public static IEnumerable<object[]> Controllers() => new[]
    {
        new object[] { typeof(VehiclesController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetManage },
        new object[] { typeof(DriversController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetManage },
        new object[] { typeof(DispatchZonesController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetManage },
        new object[] { typeof(FleetController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetManage },
        new object[] { typeof(MaintenanceSchedulesController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetMaintenance },
        new object[] { typeof(MaintenanceWorkOrdersController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetMaintenance },
        new object[] { typeof(FuelLogsController), ModuleKeys.Catalog, PermissionCatalog.FleetView, PermissionCatalog.FleetMaintenance },
        new object[] { typeof(DriverRatesController), ModuleKeys.Catalog, PermissionCatalog.DriverPayView, PermissionCatalog.DriverPayManage },
        new object[] { typeof(DriverPayPolicyController), ModuleKeys.Catalog, PermissionCatalog.DriverPayView, PermissionCatalog.DriverPayManage },
        new object[] { typeof(DriverTripsController), ModuleKeys.Catalog, PermissionCatalog.DriverPayView, PermissionCatalog.DriverPayManage },
        new object[] { typeof(SpecialDeliveryController), ModuleKeys.LtlGround, PermissionCatalog.TripsDispatch, PermissionCatalog.TripsDispatch },
    };

    /// <summary>Acciones que no siguen la regla GET = lectura: la vista previa de la fórmula es un POST sin escritura.</summary>
    private static readonly Dictionary<(Type, string), string> Exceptions = new()
    {
        [(typeof(DriverPayPolicyController), "Preview")] = PermissionCatalog.DriverPayView,
    };

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Fleet_controllers_require_module_and_the_expected_permission_per_action(Type controller, string module, string readPermission, string writePermission)
    {
        var modules = controller.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();
        Assert.Equal(new[] { module }, modules);

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var policies = action.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy).ToList();
            Assert.True(policies.Count == 1, $"{controller.Name}.{action.Name} debe llevar exactamente un [RequirePermission] (tiene {policies.Count}).");

            var isRead = action.GetCustomAttributes<HttpMethodAttribute>().All(h => h.HttpMethods.All(v => v == "GET"));
            var expected = Exceptions.TryGetValue((controller, action.Name), out var special) ? special : isRead ? readPermission : writePermission;
            Assert.True(RequirePermissionAttribute.Prefix + expected == policies[0],
                $"{controller.Name}.{action.Name}: se esperaba '{expected}' y tiene '{policies[0]}'.");
        }
    }
}

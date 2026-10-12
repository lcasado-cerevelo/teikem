using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 38 (F-B): módulo, permiso y ruta de cada acción de la importación de consignatarios quedan fijados por reflexión
/// (patrón de FleetControllerSecurityTests), y también los de la importación de órdenes, que comparte el servicio de
/// plantillas: quitar un [RequireModule]/[RequirePermission] o cambiar el permiso de una acción rompe CI.
/// </summary>
public class ConsigneeImportControllerSecurityTests
{
    /// <summary>(controlador, módulo, permiso de lectura (GET), permiso de escritura (resto)).</summary>
    public static IEnumerable<object[]> Controllers() => new[]
    {
        new object[] { typeof(ConsigneeImportTemplatesController), ModuleKeys.Catalog, PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate },
        new object[] { typeof(ConsigneeImportController), ModuleKeys.Catalog, PermissionCatalog.LocationsRead, PermissionCatalog.LocationsCreate },
        new object[] { typeof(ImportTemplatesController), ModuleKeys.LtlGround, PermissionCatalog.OrdersEdit, PermissionCatalog.OrdersEdit },
        new object[] { typeof(OrderImportController), ModuleKeys.LtlGround, PermissionCatalog.OrdersView, PermissionCatalog.OrdersCreate },
    };

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Import_controllers_require_module_and_the_expected_permission_per_action(Type controller, string module, string readPermission, string writePermission)
    {
        Assert.NotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
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
            var expected = isRead ? readPermission : writePermission;
            Assert.True(RequirePermissionAttribute.Prefix + expected == policies[0],
                $"{controller.Name}.{action.Name}: se esperaba '{expected}' y tiene '{policies[0]}'.");
            Assert.Null(action.GetCustomAttribute<RequireAal2Attribute>()); // sin AAL2, como consignatarios y órdenes
        }
    }

    [Fact]
    public void Consignee_import_routes_hang_from_locations()
    {
        Assert.Equal("api/v1/locations/import-templates", typeof(ConsigneeImportTemplatesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("api/v1/locations/import", typeof(ConsigneeImportController).GetCustomAttribute<RouteAttribute>()!.Template);

        var templates = typeof(ConsigneeImportController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => $"{h.HttpMethods.Single()} {h.Template}"))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "GET fields", "GET {batchPublicId:guid}", "POST validate", "POST validate-file", "POST {batchPublicId:guid}/confirm", "POST {batchPublicId:guid}/discard",
        }, templates);
    }
}

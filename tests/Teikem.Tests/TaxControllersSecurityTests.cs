using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

// Lote 248 (por reflexión): los controladores nuevos de impuestos exigen el módulo TAX y tax.manage en cada acción, y no tienen DELETE (todo se da de baja).
public sealed class TaxControllersSecurityTests
{
    [Theory]
    [InlineData(typeof(ClientTaxExemptionsController), "api/v1/clients/{publicId:guid}/tax-exemptions")]
    [InlineData(typeof(TaxExemptionPeriodsController), "api/v1/tax/exemption-periods")]
    [InlineData(typeof(TaxController), "api/v1/tax")]
    public void Every_action_requires_the_tax_module_and_tax_manage(Type controller, string route)
    {
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(ModuleKeys.Tax, controller.GetCustomAttribute<RequireModuleAttribute>()!.ModuleKey);
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToList();
        Assert.NotEmpty(actions);
        foreach (var a in actions)
        {
            var perm = a.GetCustomAttribute<RequirePermissionAttribute>();
            Assert.True(perm is not null, $"{controller.Name}.{a.Name} sin RequirePermission");
            Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.TaxManage, perm!.Policy);
            Assert.DoesNotContain(a.GetCustomAttributes<HttpMethodAttribute>(), h => h is HttpDeleteAttribute);
        }
    }

    [Fact]
    public void The_capture_is_multipart_and_the_period_routes_are_the_documented_ones()
    {
        var capture = typeof(ClientTaxExemptionsController).GetMethod(nameof(ClientTaxExemptionsController.Capture))!;
        Assert.Contains("multipart/form-data", capture.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.NotNull(capture.GetCustomAttribute<HttpPostAttribute>());
        var routes = typeof(TaxExemptionPeriodsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => $"{h.HttpMethods.Single()} {h.Template}")).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "GET ", "GET {publicId:guid}", "PATCH {publicId:guid}", "POST ", "POST {publicId:guid}/deactivate", "POST {publicId:guid}/reactivate" },
            routes.Select(r => r.Replace("  ", " ")).ToArray());
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan) — el controlador de cadenas de clientes exige [Authorize], el módulo SALES_ORDERS y la ruta api/v1/client-chains; las lecturas piden
/// sales.chain|clients.read|sales.view y las mutaciones sales.chain. Ninguna acción es anónima ni recibe el tenant del request.
/// </summary>
public class ClientChainsControllerSecurityTests
{
    private const string View = PermissionCatalog.SalesChain + "|" + PermissionCatalog.ClientsRead + "|" + PermissionCatalog.SalesView;
    private const string Manage = PermissionCatalog.SalesChain;

    [Fact]
    public void Client_chains_need_the_sales_orders_module_and_each_action_its_permission()
    {
        var t = typeof(ClientChainsController);
        Assert.Equal(new[] { ModuleKeys.SalesOrders }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/client-chains", t.GetCustomAttribute<RouteAttribute>()!.Template);

        var expected = new Dictionary<string, (string Perm, string Verb, string Template)>
        {
            ["List"] = (View, "GET", ""),
            ["Get"] = (View, "GET", "{publicId:guid}"),
            ["Clients"] = (View, "GET", "{publicId:guid}/clients"),
            ["Create"] = (Manage, "POST", ""),
            ["Update"] = (Manage, "PATCH", "{publicId:guid}"),
            ["Deactivate"] = (Manage, "POST", "{publicId:guid}/deactivate"),
            ["Reactivate"] = (Manage, "POST", "{publicId:guid}/reactivate"),
            ["Assign"] = (Manage, "POST", "{publicId:guid}/clients"),
            ["Remove"] = (Manage, "POST", "{publicId:guid}/clients/remove"),
            ["SetClientChain"] = (Manage, "PUT", "by-client/{clientPublicId:guid}"),
            ["BulkPreview"] = (Manage, "POST", "{publicId:guid}/bulk-assign/preview"),
            ["BulkApply"] = (Manage, "POST", "{publicId:guid}/bulk-assign/apply"),
        };
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(expected.Keys.OrderBy(x => x), actions.Select(a => a.Name).OrderBy(x => x));
        foreach (var a in actions)
        {
            var (perm, verb, template) = expected[a.Name];
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + perm }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
            var http = Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>());
            Assert.Equal(new[] { verb }, http.HttpMethods);
            Assert.Equal(template, http.Template);
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }
}

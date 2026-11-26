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
/// Lote 247 (Lote C, P4) — «Precios especiales» exige [Authorize], el módulo SALES_ORDERS y la ruta api/v1/special-prices; las lecturas piden
/// sales.view|sales.pricelist y las escrituras sales.pricelist. Las acciones nuevas de cadenas en listas y ofertas piden sales.pricelist. Ninguna acción es anónima
/// ni recibe el tenant del request.
/// </summary>
public class SpecialPricesControllerSecurityTests
{
    private const string View = PermissionCatalog.SalesView + "|" + PermissionCatalog.SalesPricelist;
    private const string Manage = PermissionCatalog.SalesPricelist;

    private static void AssertAction(MethodInfo a, string perm, string verb, string template)
    {
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + perm }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        var http = Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(new[] { verb }, http.HttpMethods);
        Assert.Equal(template, http.Template);
        Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Special_prices_need_the_sales_orders_module_and_each_action_its_permission()
    {
        var t = typeof(SpecialPricesController);
        Assert.Equal(new[] { ModuleKeys.SalesOrders }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(t.GetCustomAttribute<ApiControllerAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/special-prices", t.GetCustomAttribute<RouteAttribute>()!.Template);

        var expected = new Dictionary<string, (string Perm, string Verb, string Template)>
        {
            ["GetClient"] = (View, "GET", "clients/{clientPublicId:guid}"),
            ["AddClient"] = (Manage, "POST", "clients/{clientPublicId:guid}"),
            ["UpdateClient"] = (Manage, "PATCH", "clients/{clientPublicId:guid}/{itemId:int}"),
            ["RemoveClient"] = (Manage, "DELETE", "clients/{clientPublicId:guid}/{itemId:int}"),
            ["GetChain"] = (View, "GET", "chains/{chainPublicId:guid}"),
            ["AddChain"] = (Manage, "POST", "chains/{chainPublicId:guid}"),
            ["UpdateChain"] = (Manage, "PATCH", "chains/{chainPublicId:guid}/{itemId:int}"),
            ["RemoveChain"] = (Manage, "DELETE", "chains/{chainPublicId:guid}/{itemId:int}"),
        };
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(expected.Keys.OrderBy(x => x), actions.Select(a => a.Name).OrderBy(x => x));
        foreach (var a in actions)
        {
            var (perm, verb, template) = expected[a.Name];
            AssertAction(a, perm, verb, template);
        }
    }

    [Fact]
    public void Chain_actions_of_price_lists_and_offers_need_sales_pricelist()
    {
        foreach (var t in new[] { typeof(PriceListsController), typeof(OffersController) })
        {
            Assert.Contains(ModuleKeys.SalesOrders, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
            AssertAction(t.GetMethod("AddChains")!, Manage, "POST", "{publicId:guid}/chains");
            AssertAction(t.GetMethod("RemoveChain")!, Manage, "DELETE", "{publicId:guid}/chains/{chainPublicId:guid}");
        }

        // la lista de listas sigue pidiendo sales.view y acepta includeSpecial
        var list = typeof(PriceListsController).GetMethod("List")!;
        AssertAction(list, PermissionCatalog.SalesView, "GET", null!);
        Assert.Contains(list.GetParameters(), p => p.Name == "includeSpecial" && p.ParameterType == typeof(bool) && p.GetCustomAttribute<FromQueryAttribute>() is not null);
    }
}

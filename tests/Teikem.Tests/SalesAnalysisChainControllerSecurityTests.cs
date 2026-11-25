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
/// Lote 245 (Lote B del plan) — el controlador «Por cadena» del análisis de ventas vive en api/v1/sales-analysis (mismo prefijo que SalesAnalysisController, sin
/// tocarlo), exige sesión y el módulo SALES_ORDERS; by-chain y by-chain/series piden sales.analysis.view|sales.analysis.manage; ninguna acción es anónima ni recibe el
/// tenant del request, y ninguna plantilla choca con las del controlador del análisis.
/// </summary>
public class SalesAnalysisChainControllerSecurityTests
{
    private const string View = PermissionCatalog.SalesAnalysisView + "|" + PermissionCatalog.SalesAnalysisManage;

    [Fact]
    public void By_chain_needs_session_the_sales_orders_module_and_the_analysis_permission()
    {
        var t = typeof(SalesAnalysisChainController);
        Assert.Equal(new[] { ModuleKeys.SalesOrders }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/sales-analysis", t.GetCustomAttribute<RouteAttribute>()!.Template);

        var expected = new Dictionary<string, string> { ["ByChain"] = "by-chain", ["Series"] = "by-chain/series" };
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(expected.Keys.OrderBy(x => x), actions.Select(a => a.Name).OrderBy(x => x));
        foreach (var a in actions)
        {
            var get = Assert.IsType<HttpGetAttribute>(a.GetCustomAttribute<HttpGetAttribute>());
            Assert.Equal(expected[a.Name], get.Template);
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + View }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void No_route_template_collides_with_the_sales_analysis_controller()
    {
        static IEnumerable<string> Routes(Type t)
        {
            var prefix = t.GetCustomAttribute<RouteAttribute>()!.Template;
            return t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
                .SelectMany(h => h.HttpMethods.Select(v => $"{v} {prefix}/{h.Template}".ToLowerInvariant()));
        }
        var analysis = Routes(typeof(SalesAnalysisController)).ToHashSet();
        var chain = Routes(typeof(SalesAnalysisChainController)).ToList();
        Assert.Equal(2, chain.Count);
        Assert.Empty(chain.Where(analysis.Contains));
        Assert.Equal(typeof(SalesAnalysisController).GetCustomAttribute<RouteAttribute>()!.Template,
            typeof(SalesAnalysisChainController).GetCustomAttribute<RouteAttribute>()!.Template);
    }
}

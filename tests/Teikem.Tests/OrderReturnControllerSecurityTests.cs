using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 112 — devolver una orden exige el módulo LTL_GROUND y el permiso orders.return; la acción no es anónima ni recibe el tenant del request.</summary>
public class OrderReturnControllerSecurityTests
{
    [Fact]
    public void Returning_an_order_needs_the_ltl_module_and_orders_return()
    {
        var t = typeof(OrderReturnController);
        Assert.Equal(new[] { ModuleKeys.LtlGround }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        var action = Assert.Single(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.OrdersReturn }, action.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        Assert.DoesNotContain(action.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 111 — el controlador de reclamaciones exige el módulo LTL_GROUND, las lecturas piden claims.view y las mutaciones claims.manage;
/// ninguna acción es anónima ni recibe el tenant del request.
/// </summary>
public class ClaimsControllerSecurityTests
{
    [Fact]
    public void Claims_need_the_ltl_module_and_each_action_its_permission()
    {
        var t = typeof(ClaimsController);
        Assert.Equal(new[] { ModuleKeys.LtlGround }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/claims", t.GetCustomAttribute<RouteAttribute>()!.Template);

        var expected = new Dictionary<string, string>
        {
            ["ToVerify"] = PermissionCatalog.ClaimsView, ["List"] = PermissionCatalog.ClaimsView, ["Get"] = PermissionCatalog.ClaimsView,
            ["MarkLost"] = PermissionCatalog.ClaimsManage, ["Create"] = PermissionCatalog.ClaimsManage, ["Resolve"] = PermissionCatalog.ClaimsManage,
        };
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(expected.Keys.OrderBy(x => x), actions.Select(a => a.Name).OrderBy(x => x));
        foreach (var a in actions)
        {
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + expected[a.Name] }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }
}

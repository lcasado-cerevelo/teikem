using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 7A (P1) — GET /api/v1/analytics/activity exige analytics.view y el módulo ANALYTICS (maestro, módulo 12) además del
/// permiso del módulo pedido, que exige ActivityFeedService (lo fija ActivityRulesTests). Se fija por reflexión: quitar el
/// [RequirePermission] o el [RequireModule] del controlador, o abrir la acción, rompe CI aunque el smoke no pase por ahí.
/// </summary>
public class ActivityControllerSecurityTests
{
    [Fact]
    public void Activity_feed_requires_analytics_view_and_the_analytics_module()
    {
        var t = typeof(ActivityController);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.AnalyticsView },
            t.GetCustomAttributes<RequirePermissionAttribute>(inherit: true).Select(a => a.Policy));
        Assert.Equal(new[] { ModuleKeys.Analytics },
            t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.Equal("api/v1/analytics/activity", t.GetCustomAttribute<RouteAttribute>()!.Template);

        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        var get = Assert.Single(actions);
        Assert.NotNull(get.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(get.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Empty(get.GetCustomAttributes<RequirePermissionAttribute>());   // ningún permiso de acción reemplaza al del controlador
        Assert.DoesNotContain(get.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }
}

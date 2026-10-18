using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 116 — los avisos al consignatario: la configuración vive en el portal (sesión de portal y módulo CLIENT_PORTAL); lo del consignatario (seguimiento por enlace y baja) es anónimo y limitado por tasa.</summary>
public class ConsigneeNoticeControllersSecurityTests
{
    [Fact]
    public void The_portal_settings_need_a_session_and_the_client_portal_module_and_never_take_the_tenant_or_client_from_the_request()
    {
        var t = typeof(PortalConsigneeNoticesController);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(new[] { ModuleKeys.ClientPortal }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.Equal("api/v1/portal/consignee-notices", t.GetCustomAttribute<RouteAttribute>()!.Template);
        foreach (var a in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => p.Name is not null && (p.Name.Equals("tenantId", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("clientId", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void The_consignee_endpoints_are_anonymous_and_only_those_two()
    {
        var t = typeof(PublicConsigneeController);
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).ToList();
        Assert.Equal(new[] { nameof(PublicConsigneeController.Track), nameof(PublicConsigneeController.Unsubscribe) }, actions.Select(a => a.Name).OrderBy(n => n).ToArray());
        Assert.All(actions, a => Assert.NotNull(a.GetCustomAttribute<AllowAnonymousAttribute>()));
        Assert.Equal("api/v1/public", t.GetCustomAttribute<RouteAttribute>()!.Template);
    }
}

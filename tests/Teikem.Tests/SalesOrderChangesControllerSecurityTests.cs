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
/// Lote 249 (P5) — por reflexión: cada acción de SalesOrderChangesController y SalesChangeSettingsController lleva [Authorize] (de la clase), el módulo
/// SALES_ORDERS y el permiso del plan; la ruta vieja PATCH lines/{lineId} de SalesOrdersController pasó de sales.create a sales.modify. Ninguna recibe el tenant.
/// </summary>
public class SalesOrderChangesControllerSecurityTests
{
    private static void AssertActions(Type t, string route, IReadOnlyDictionary<string, (string Perm, string Verb, string Template)> expected)
    {
        Assert.Equal(new[] { ModuleKeys.SalesOrders }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(route, t.GetCustomAttribute<RouteAttribute>()!.Template);
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(expected.Keys.OrderBy(k => k), actions.Select(a => a.Name).OrderBy(n => n));
        foreach (var (name, (perm, verb, template)) in expected)
        {
            var a = actions.Single(m => m.Name == name);
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + perm }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
            var http = Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>());
            Assert.Equal(new[] { verb }, http.HttpMethods);
            Assert.Equal(template, http.Template);
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Each_change_action_has_its_permission_and_the_sales_orders_module()
    {
        AssertActions(typeof(SalesOrderChangesController), "api/v1/sales-orders/{publicId:guid}", new Dictionary<string, (string, string, string)>
        {
            ["EditOptions"] = (PermissionCatalog.SalesView, "GET", "edit-options"),
            ["Preview"] = (PermissionCatalog.SalesModify, "POST", "changes/preview"),
            ["Create"] = (PermissionCatalog.SalesModify, "POST", "changes"),
            ["List"] = (PermissionCatalog.SalesView, "GET", "changes"),
            ["Get"] = (PermissionCatalog.SalesView, "GET", "changes/{changePublicId:guid}"),
            ["Approve"] = (PermissionCatalog.SalesModifyApprove, "POST", "changes/{changePublicId:guid}/approve"),
            ["Reject"] = (PermissionCatalog.SalesModifyApprove, "POST", "changes/{changePublicId:guid}/reject"),
            ["Withdraw"] = (PermissionCatalog.SalesModify, "POST", "changes/{changePublicId:guid}/withdraw"),
            ["UploadAttachment"] = (PermissionCatalog.SalesModify, "POST", "change-attachments"),
            ["CreateAddition"] = (PermissionCatalog.SalesModify, "POST", "additions"),
        });
        var upload = typeof(SalesOrderChangesController).GetMethod("UploadAttachment")!;
        Assert.NotNull(upload.GetCustomAttribute<RequestSizeLimitAttribute>());   // como la firma del pedido
        Assert.Contains("multipart/form-data", upload.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
    }

    [Fact]
    public void The_tray_and_the_settings_have_their_permissions()
    {
        AssertActions(typeof(SalesChangeSettingsController), "api/v1/sales-order-changes", new Dictionary<string, (string, string, string)>
        {
            ["Pending"] = (PermissionCatalog.SalesModifyApprove, "GET", "pending"),
            ["Settings"] = (PermissionCatalog.SalesView + "|" + PermissionCatalog.AdminTenant, "GET", "settings"),
            ["UpdateSettings"] = (PermissionCatalog.AdminTenant, "PATCH", "settings"),
            ["ClientSettings"] = (PermissionCatalog.SalesView + "|" + PermissionCatalog.ClientsRead, "GET", "settings/clients/{clientPublicId:guid}"),
            ["UpdateClientSettings"] = (PermissionCatalog.ClientsUpdate, "PATCH", "settings/clients/{clientPublicId:guid}"),
        });
    }

    [Fact]
    public void The_old_patch_line_route_now_needs_sales_modify()
    {
        var a = typeof(SalesOrdersController).GetMethod("ModifyLine", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.SalesModify }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        var http = Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(new[] { "PATCH" }, http.HttpMethods);
        Assert.Equal("{publicId:guid}/lines/{lineId:int}", http.Template);
        // el constructor recibe el servicio de modificaciones (la ruta es su adaptador)
        Assert.Contains(typeof(SalesOrdersController).GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(Teikem.Infrastructure.Services.SalesOrderChangeService));
    }
}

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
/// Lote 248 (Lote D del plan) — la lista «Documentos por vencer» exige [Authorize], el módulo CATALOG y clients.read; los documentos de un cliente (listar, subir, editar y
/// quitar) siguen en ClientsController con clients.read para ver y clients.update|clients.create_basic para escribir. Ninguna acción es anónima ni recibe el tenant.
/// </summary>
public class ClientDocumentsControllerSecurityTests
{
    private const string Write = PermissionCatalog.ClientsUpdate + "|" + PermissionCatalog.ClientsCreateBasic;

    [Fact]
    public void The_expiring_list_needs_the_catalog_module_and_clients_read()
    {
        var t = typeof(ClientDocumentsController);
        Assert.Equal(new[] { ModuleKeys.Catalog }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/client-documents", t.GetCustomAttribute<RouteAttribute>()!.Template);
        var action = Assert.Single(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Expiring", action.Name);
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.ClientsRead }, action.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        var http = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(new[] { "GET" }, http.HttpMethods);
        Assert.Equal("expiring", http.Template);
        Assert.DoesNotContain(action.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_client_document_actions_keep_their_permissions_and_patch_is_new()
    {
        var t = typeof(ClientsController);
        Assert.Equal(new[] { ModuleKeys.Catalog }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        var expected = new Dictionary<string, (string Perm, string Verb, string Template)>
        {
            ["Documents"] = (PermissionCatalog.ClientsRead, "GET", "{publicId:guid}/documents"),
            ["UploadDocument"] = (Write, "POST", "{publicId:guid}/documents"),
            ["UpdateDocument"] = (Write, "PATCH", "{publicId:guid}/documents/{documentPublicId:guid}"),
            ["RemoveDocument"] = (Write, "DELETE", "{publicId:guid}/documents/{documentPublicId:guid}"),
        };
        foreach (var (name, (perm, verb, template)) in expected)
        {
            var a = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + perm }, a.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
            var http = Assert.Single(a.GetCustomAttributes<HttpMethodAttribute>());
            Assert.Equal(new[] { verb }, http.HttpMethods);
            Assert.Equal(template, http.Template);
            Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }
}

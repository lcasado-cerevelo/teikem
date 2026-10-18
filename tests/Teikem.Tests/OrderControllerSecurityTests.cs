using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 39: el módulo y el permiso de cada acción de OrdersController y OrderStatusController quedan fijados por reflexión (como
/// TripControllerSecurityTests). Confirmar no lleva [RequirePermission]: decide en código entre orders.edit y
/// orders.credit_override (ajuste C). La cotización (GET /quote) exige orders.view por atributo y orders.view_price en código (403).
/// También fija que los filtros nuevos del listado se bindean desde la query como arreglos (?serviceTypes=A&amp;serviceTypes=B).
/// </summary>
public class OrderControllerSecurityTests
{
    private static readonly Type[] OrderControllers = { typeof(OrdersController), typeof(OrderStatusController) };

    /// <summary>(controlador, acción) → permiso del atributo; null = sin [RequirePermission] (decide en código).</summary>
    private static readonly Dictionary<(Type Controller, string Action), string?> Expected = new()
    {
        [(typeof(OrdersController), nameof(OrdersController.List))] = PermissionCatalog.OrdersView,
        [(typeof(OrdersController), nameof(OrdersController.Lookup))] = PermissionCatalog.OrdersView,
        [(typeof(OrdersController), nameof(OrdersController.Get))] = PermissionCatalog.OrdersView,
        [(typeof(OrdersController), nameof(OrdersController.Create))] = PermissionCatalog.OrdersCreate,
        [(typeof(OrdersController), nameof(OrdersController.Update))] = PermissionCatalog.OrdersEdit,
        [(typeof(OrdersController), nameof(OrdersController.Delete))] = PermissionCatalog.OrdersCancel,

        // GET /quote: orders.view por atributo + orders.view_price en código (OrderPriceVisibility.EnsureAsync).
        [(typeof(OrderStatusController), nameof(OrderStatusController.PreviewQuote))] = PermissionCatalog.OrdersView,
        [(typeof(OrderStatusController), nameof(OrderStatusController.Confirm))] = null,
        [(typeof(OrderStatusController), nameof(OrderStatusController.Reprice))] = PermissionCatalog.OrdersEdit,
        [(typeof(OrderStatusController), nameof(OrderStatusController.Cancel))] = PermissionCatalog.OrdersCancel,
        // Lote 106: reabrir una orden en estatus final, permiso propio (el servicio lo vuelve a exigir en código).
        [(typeof(OrderStatusController), nameof(OrderStatusController.Reopen))] = PermissionCatalog.OrdersReopen,
        [(typeof(OrderStatusController), nameof(OrderStatusController.TransitionStatus))] = PermissionCatalog.OrdersEdit,
    };

    public static IEnumerable<object?[]> ActionMap() => Expected.Select(kv => new object?[] { kv.Key.Controller, kv.Key.Action, kv.Value });

    private static List<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

    [Theory]
    [MemberData(nameof(ActionMap))]
    public void Each_order_action_requires_exactly_its_permission(Type controller, string action, string? permission)
    {
        var method = Actions(controller).SingleOrDefault(m => m.Name == action);
        Assert.True(method is not null, $"{controller.Name}.{action} no existe o no es una acción HTTP.");
        var policies = method!.GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy).ToList();
        if (permission is null)
        {
            Assert.True(policies.Count == 0, $"{controller.Name}.{action} decide el permiso en código: no lleva [RequirePermission].");
            return;
        }
        Assert.True(policies.Count == 1, $"{controller.Name}.{action} debe llevar exactamente un [RequirePermission] (tiene {policies.Count}).");
        Assert.Equal(RequirePermissionAttribute.Prefix + permission, policies[0]);
    }

    [Fact]
    public void The_map_covers_every_public_action_of_the_order_controllers()
    {
        var unmapped = OrderControllers
            .SelectMany(c => Actions(c).Select(m => (c, m.Name)))
            .Where(k => !Expected.ContainsKey(k))
            .Select(k => $"{k.c.Name}.{k.Name}")
            .ToList();
        Assert.True(unmapped.Count == 0, "Acciones sin permiso esperado en el mapa: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Both_controllers_require_the_ltl_ground_module_only()
    {
        foreach (var controller in OrderControllers)
        {
            var modules = controller.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();
            Assert.True(modules.SequenceEqual(new[] { ModuleKeys.LtlGround }), $"{controller.Name} debe llevar exactamente [RequireModule(LTL_GROUND)].");
        }
    }

    [Fact]
    public void The_quote_controller_receives_the_price_decision()
    {
        // GET /quote no puede llamar al servicio sin pasar antes por OrderPriceVisibility (se inyecta en el controlador).
        var ctor = Assert.Single(typeof(OrderStatusController).GetConstructors());
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(Teikem.Infrastructure.Orders.OrderPriceVisibility));
    }

    [Theory]
    [InlineData("serviceTypes", typeof(string[]))]
    [InlineData("packageTypes", typeof(string[]))]
    [InlineData("cities", typeof(string[]))]
    [InlineData("postalCodePrefix", typeof(string))]
    [InlineData("clientIds", typeof(Guid[]))]
    [InlineData("statuses", typeof(string[]))]
    [InlineData("clientId", typeof(Guid?))]
    [InlineData("status", typeof(string))]
    public void List_binds_the_new_filters_from_the_query(string name, Type type)
    {
        var p = Assert.Single(typeof(OrdersController).GetMethod(nameof(OrdersController.List))!.GetParameters(), x => x.Name == name);
        Assert.Equal(type, p.ParameterType);
        Assert.NotNull(p.GetCustomAttribute<FromQueryAttribute>());
    }

    [Fact]
    public void No_order_action_receives_a_tenant_id()
    {
        foreach (var controller in OrderControllers)
        foreach (var action in Actions(controller))
        foreach (var p in action.GetParameters())
            Assert.False(string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase), $"{controller.Name}.{action.Name} recibe tenantId.");
    }
}
